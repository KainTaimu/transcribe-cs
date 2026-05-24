using ASTranscriber.Core.Extensions;
using FFMpegCore;
using FFMpegCore.Enums;
using FFMpegCore.Exceptions;
using FluentResults;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;
using PathLib;
using Spectre.Console;
using Subtitles;
using Whisper.net;

namespace ASTranscriber.Transcription.Whisper;

public class WhisperTranscriber(IPath modelPath) : ITranscriber
{
    public IPath ModelPath { get; } = modelPath;

    private readonly ILogger<WhisperTranscriber> _logger = LoggerFactory
        .Create(builder => builder.SetMinimumLevel(LogLevel.Debug).AddNLog())
        .CreateLogger<WhisperTranscriber>();

    private static readonly string[] _allowedExtensions = [".wav", ".mp3", ".opus", ".m4a"];

    public Result<IPath> Start(
        IPath inputFile,
        IPath outputDirectory,
        ProgressContext? progressContext = null
    )
    {
        if (!ValidateInputFile(inputFile, out var validationError))
            return Result.Fail(validationError);

        if (!EnsureOutputDirectory(outputDirectory, out var dirError))
            return Result.Fail(dirError);

        var resolvedModelPath = ResolveModelPath(ModelPath);

        var ensureModelResult = EnsureModelExists(resolvedModelPath);
        if (ensureModelResult.IsFailed)
            return ensureModelResult;

        if (!TryCreateDirectory(outputDirectory, out var mkdirError))
            return Result.Fail(mkdirError);

        var wavResult = ToWhisperSupportedWav(inputFile);
        if (wavResult.IsFailed)
            return Result.Fail(wavResult.Errors.First().Message);

        var result = ProcessTranscription(
            wavResult.Value,
            resolvedModelPath,
            outputDirectory,
            progressContext
        );

        if (result.IsSuccess)
            AnsiConsole.MarkupLine(
                $"[blue]Transcription completed: {Markup.Escape(inputFile.Basename)}[/]"
            );

        return result;
    }

    private bool ValidateInputFile(IPath inputFile, out string error)
    {
        error = string.Empty;

        if (!inputFile.IsFile())
        {
            error = $"Input file \"{inputFile}\" is not a file or does not exist";
            return false;
        }

        if (!_allowedExtensions.Contains(inputFile.Extension))
        {
            error = $"Unsupported file format: {inputFile.Extension}";
            return false;
        }

        return true;
    }

    private bool EnsureOutputDirectory(IPath directory, out string error)
    {
        error = string.Empty;

        if (directory.IsDir())
            return true;

        try
        {
            directory.Mkdir(true);
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to create output directory \"{directory}\": {ex.Message}";
            return false;
        }
    }

    private bool TryCreateDirectory(IPath directory, out string error)
    {
        error = string.Empty;

        try
        {
            directory.Mkdir(true);
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to create directory \"{directory}\": {ex.Message}";
            return false;
        }
    }

    private IPath ResolveModelPath(IPath modelPath)
    {
        var resolved = modelPath;
        if (!modelPath.ToString()!.EndsWith(".bin"))
            resolved = modelPath.WithExtension(".bin");

        if (!resolved.Parent().IsDir())
            resolved.Parent().Mkdir();

        return resolved;
    }

    private Result EnsureModelExists(IPath modelPath)
    {
        if (modelPath.IsFile())
            return Result.Ok();

        return DownloadModel(modelPath);
    }

    private Result DownloadModel(IPath modelPath, ProgressContext? progressContext = null)
    {
        var whisperModelDownloader = new WhisperModelDownloader();
        _logger.LogInformation($"Downloading \"{modelPath}\"");

        var cts = new CancellationTokenSource();
        var handler = CreateCancelEventHandler(cts);

        Console.CancelKeyPress += handler;

        try
        {
            var task = progressContext?.AddTask($"Downloading model \"{modelPath.Basename}\"");
            task?.IsIndeterminate = true;
            try
            {
                var downloadTask = whisperModelDownloader.DownloadWhisperModel(
                    modelPath,
                    cts.Token
                );
                downloadTask.Wait(cts.Token);
            }
            catch (OperationCanceledException)
            {
                AnsiConsole.MarkupLine("[red]Cancelled[/]");
                task?.StopTask();
                Console.CancelKeyPress -= handler;
                throw;
            }
            Console.CancelKeyPress -= handler;
            task?.StopTask();

            return Result.Ok();
        }
        catch (OperationCanceledException)
        {
            return Result.Fail("Model download cancelled");
        }
    }

    private Result<IPath> ProcessTranscription(
        IPath wavFile,
        IPath modelPath,
        IPath outputDir,
        ProgressContext? progressContext = null
    )
    {
        using var cts = new CancellationTokenSource();
        var handler = CreateCancelEventHandler(cts);
        Console.CancelKeyPress += handler;

        IPath? srtPath = null;
        try
        {
            var srtTask = RunWhisper(
                wavFile,
                modelPath.ToString()!,
                outputDir,
                cts.Token,
                progressContext
            );
            srtTask.Wait(cts.Token);
            if (srtTask.IsCompleted)
            {
                var result = srtTask.Result;
                if (result.IsSuccess)
                    srtPath = srtTask.Result.Value;
                else
                    return result.WithError("Transcription failed");
            }
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.MarkupLine("[red]Cancelled[/]");
            return Result.Fail("Transcription cancelled");
        }
        finally
        {
            Console.CancelKeyPress -= handler;
        }

        return Result.Ok(srtPath)!;
    }

    private async Task<Result<IPath>> RunWhisper(
        IPath wavFilePath,
        string modelPath,
        IPath outputPath,
        CancellationToken ct,
        ProgressContext? progressContext = null
    )
    {
        var wavFileName = wavFilePath.ToString()!;
        using var whisperFactory = WhisperFactory.FromPath(modelPath);
        await using var processor = whisperFactory
            .CreateBuilder()
            .WithLanguage("auto")
            .WithNoContext()
            .WithTemperature(0)
            .WithSuppressRegex("")
            .Build();
        await using var fileStream = File.OpenRead(wavFileName);

        var max = (await FFProbe.AnalyseAsync(wavFileName, cancellationToken: ct)).Duration;
        var currentTask = progressContext?.AddTask($"[bold]Transcribing[/]", maxValue: 100);

        var srt = new Srt();

        await foreach (var result in processor.ProcessAsync(fileStream, ct))
        {
            currentTask?.Value = (result.End / max) * 100;

            AnsiConsole.MarkupLine($"[[{result.Start:G}->{result.End:G}]] {result.Text}");
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

    public IPath GetTranscriptionSrt()
    {
        throw new NotImplementedException();
    }

    private Result<IPath> ToWhisperSupportedWav(IPath path)
    {
        try
        {
            var primaryAudioStream = FFProbe.Analyse(path.ToPathString()).PrimaryAudioStream;
            if (primaryAudioStream is null)
                return Result.Fail("Could not convert. Lacks a primary audio stream");
        }
        catch (FFMpegException e)
        {
            return Result.Fail($"Failed to get audio streams: {e}");
        }

        var pFac = new PathFactory();
        var outputPath = pFac.Create(Path.GetTempPath(), path.BasenameWithoutExtensions + ".wav");
        _logger.LogDebug($"saving to {outputPath}");

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
}
