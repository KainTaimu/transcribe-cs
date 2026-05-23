using ASTranscriber.Core.Extensions;
using FFMpegCore;
using FFMpegCore.Enums;
using FluentResults;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;
using PathLib;
using Spectre.Console;
using Subtitles;
using Whisper.net;

namespace ASTranscriber.Transcription.Whisper;

public class WhisperTranscriber : ITranscriber
{
    private readonly Queue<TranscriptionJob> _jobs = new();
    private readonly IPath _inputDirectory;
    private readonly IPath _outputDirectory;
    private readonly IPath _modelPath;

    private readonly ILogger<WhisperTranscriber> _logger = LoggerFactory
        .Create(builder => builder.SetMinimumLevel(LogLevel.Debug).AddNLog())
        .CreateLogger<WhisperTranscriber>();

    private ProgressContext? _transcriptionProgressContext;

    private readonly List<string> _allowedExtensions = [".wav", ".mp3", ".opus", ".m4a"];

    public WhisperTranscriber(IPath inputDirPath, IPath outputDirPath, string whisperModel)
    {
        if (!inputDirPath.IsDir())
            throw new ArgumentException($"Input directry \"{inputDirPath}\" is not a directory");
        if (!outputDirPath.IsDir())
            throw new ArgumentException($"Output directry \"{outputDirPath}\" is not a directory");

        var factory = new PathFactory();
        var whisperModelPath = factory.Create(whisperModel);
        if (!whisperModel.EndsWith(".bin"))
            whisperModelPath = whisperModelPath.WithExtension(".bin");

        if (!whisperModelPath.Parent().IsDir())
            whisperModelPath.Parent().Mkdir();

        if (!whisperModelPath.IsFile())
        {
            var whisperModelDownloader = new WhisperModelDownloader();
            _logger.LogInformation($"Downloading \"{whisperModelPath}\"");

            var cts = new CancellationTokenSource();
            var handler = CreateCancelEventHandler(cts);

            Console.CancelKeyPress += handler;
            AnsiConsole
                .Progress()
                .Columns(
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn() { CompletedStyle = new Style(Color.Blue) },
                    new DownloadedColumn(),
                    new ElapsedTimeColumn()
                )
                .Start(ctx =>
                {
                    var task = ctx.AddTask($"Downloading model \"{whisperModel}\"");
                    task.IsIndeterminate = true;
                    try
                    {
                        var downloadTask = whisperModelDownloader.DownloadWhisperModel(
                            whisperModelPath,
                            cts.Token
                        );
                        downloadTask.Wait(cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        AnsiConsole.MarkupLine("[red]Cancelled[/]");
                        task.StopTask();
                        _transcriptionProgressContext = null;
                        Console.CancelKeyPress -= handler;
                        throw;
                    }
                    Console.CancelKeyPress -= handler;
                    task.StopTask();
                });
        }

        _inputDirectory = inputDirPath.Resolve();
        _outputDirectory = outputDirPath.Resolve();
        _modelPath = whisperModelPath.Resolve();
    }

    public Result Start()
    {
        var count = 1;
        var len = 0;
        foreach (var entry in _inputDirectory.ListDir().OrderBy(x => x.Basename))
        {
            _logger.LogDebug(
                $"{count++}: {entry.Filename} {entry.Parents()} {_inputDirectory.Parents()}"
            );
            if (!entry.IsFile())
                continue;
            if (entry.Parents().All(x => x.ToPathString() == "exlude"))
                continue;
            if (!_allowedExtensions.Contains(entry.Extension))
            {
                _logger.LogWarning($"Ignoring unsupported file: {entry.Filename}");
                continue;
            }

            var outputPath = _outputDirectory.Join(entry.BasenameWithoutExtensions);
            outputPath.Mkdir(true);

            var job = new TranscriptionJob(entry, outputPath);
            _jobs.Enqueue(job);
            len++;
        }

        AnsiConsole
            .Progress()
            .Columns(
                new TaskDescriptionColumn() { Alignment = Justify.Left },
                new ProgressBarColumn() { CompletedStyle = new Style(Color.Blue) },
                new DownloadedColumn() { Base = FileSizeBase.Decimal },
                new ElapsedTimeColumn()
            )
            .HideCompleted(true)
            .AutoClear(true)
            .Start(ctx =>
            {
                _transcriptionProgressContext = ctx;
                var transcriptionTask = ctx.AddTask("Transcribing", maxValue: len);
                while (_jobs.Count != 0)
                {
                    var currentJob = _jobs.Dequeue();
                    var wav = ToWhisperSupportedWav(currentJob.OriginalAudioPath);
                    if (wav.IsFailed)
                    {
                        transcriptionTask.Increment(1);
                        continue;
                    }

                    using var cts = new CancellationTokenSource();
                    var handler = CreateCancelEventHandler(cts);
                    Console.CancelKeyPress += handler;

                    try
                    {
                        var srt = RunWhisper(
                            wav.Value,
                            _modelPath.ToString()!,
                            currentJob.WorkingDir,
                            cts.Token
                        );
                        srt.Wait(cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        AnsiConsole.MarkupLine("[red]Cancelled[/]");
                        transcriptionTask.StopTask();
                        _transcriptionProgressContext = null;
                        Console.CancelKeyPress -= handler;
                        break;
                    }
                    Console.CancelKeyPress -= handler;

                    transcriptionTask.Increment(1);
                }
            });

        return Result.Ok();
    }

    private async Task<Result<IPath>> RunWhisper(
        IPath wavFilePath,
        string modelPath,
        IPath outputPath,
        CancellationToken ct
    )
    {
        var wavFileName = wavFilePath.ToString()!;
        using var whisperFactory = WhisperFactory.FromPath(modelPath);
        await using var processor = whisperFactory
            .CreateBuilder()
            .WithLanguage("auto")
            .WithNoContext()
            .WithTemperature(0)
            .Build();
        await using var fileStream = File.OpenRead(wavFileName);

        var max = (await FFProbe.AnalyseAsync(wavFileName, cancellationToken: ct)).Duration;
        var currentTask = _transcriptionProgressContext?.AddTask(
            TruncateString(wavFilePath.Basename, 16),
            maxValue: 100
        );

        var srt = new Srt();

        await foreach (var result in processor.ProcessAsync(fileStream, ct))
        {
            currentTask?.Value = (result.End / max) * 100;

            var escapedTime = Markup.Escape($"[{result.Start:G}->{result.End:G}]");
            AnsiConsole.MarkupLine($"[bold]{escapedTime}[/] {result.Text}");
            srt.AddLine(result.Text, result.Start.ToString(), result.End.ToString());

            if (ct.IsCancellationRequested)
            {
                currentTask?.StopTask();
                break;
            }
        }

        currentTask?.Value = 100;

        var outPath = outputPath.Join($"{Path.GetFileNameWithoutExtension(wavFileName)}.srt");

        srt.WriteFile(outPath.ToPathString());

        return Result.Ok(outPath);
    }

    private static string TruncateString(string s, int length)
    {
        const string ellipse = " ... ";
        var sLen = s.Length;
        var termWidth = Console.WindowWidth / 4;
        if (sLen <= length + ellipse.Length || termWidth < ellipse.Length)
            return s;
        var keep = (termWidth - ellipse.Length) / 2;
        return s[..keep] + " ... " + s[(sLen - keep)..];
    }

    public IPath GetTranscriptionSrt()
    {
        throw new NotImplementedException();
    }

    private Result<IPath> ToWhisperSupportedWav(IPath path)
    {
        var primaryAudioStream = FFProbe.Analyse(path.ToPathString()).PrimaryAudioStream;
        if (primaryAudioStream is null)
        {
            return Result.Fail("Could not convert. Lacks a primary audio stream");
        }

        var pFac = new PathFactory();
        var outputPath = pFac.Create(Path.GetTempPath(), path.BasenameWithoutExtensions + ".wav");
        _logger.LogDebug($"saving to {outputPath}");

        var convertingTask = _transcriptionProgressContext?.AddTask(
            "Converting to WAV",
            maxValue: 1
        );
        convertingTask?.IsIndeterminate = true;

        FFMpegArguments
            .FromFileInput(path.ToPathString())
            .OutputToFile(
                outputPath.ToPathString(),
                true,
                opt =>
                    opt.WithoutMetadata().WithAudioSamplingRate(16_000).WithCustomArgument("-ac 1")
            )
            .WithLogLevel(FFMpegLogLevel.Quiet)
            .ProcessSynchronously();
        convertingTask?.Value = 1;

        return Result.Ok(outputPath);
    }

    private static ConsoleCancelEventHandler CreateCancelEventHandler(CancellationTokenSource cts)
    {
        return (_, args) =>
        {
            args.Cancel = true;
            cts.Cancel();
            args.Cancel = false;
        };
    }

    public class TranscriptionJob(IPath originalAudioPath, IPath workingDir)
    {
        public IPath OriginalAudioPath { get; } = originalAudioPath;
        public IPath WorkingDir { get; } = workingDir;
    }
}
