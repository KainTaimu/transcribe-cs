using ASTranscriber.Core.Extensions;
using FFMpegCore;
using FFMpegCore.Enums;
using FluentResults;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;
using PathLib;
using Spectre.Console;
using Spectre.Console.Rendering;
using Subtitles;
using Whisper.net;

namespace ASTranscriber.Transcription.Whisper;

public class WhisperTranscriber : ITranscriber
{
    private readonly ILogger<WhisperTranscriber> _logger = LoggerFactory
        .Create(builder => builder.SetMinimumLevel(LogLevel.Debug).AddNLog())
        .CreateLogger<WhisperTranscriber>();

    private static readonly string[] _allowedExtensions = [".wav", ".mp3", ".opus", ".m4a"];

    public Result Start(IPath inputFile, IPath outputDirectory, IPath modelPath)
    {
        if (!ValidateInputFile(inputFile, out var validationError))
            return Result.Fail(validationError);

        if (!EnsureOutputDirectory(outputDirectory, out var dirError))
            return Result.Fail(dirError);

        var resolvedModelPath = ResolveModelPath(modelPath);

        var ensureModelResult = EnsureModelExists(resolvedModelPath);
        if (ensureModelResult.IsFailed)
            return ensureModelResult;

        var outputFileDir = outputDirectory.Join(inputFile.BasenameWithoutExtensions);
        if (!TryCreateDirectory(outputFileDir, out var mkdirError))
            return Result.Fail(mkdirError);

        var wavResult = ToWhisperSupportedWav(inputFile);
        if (wavResult.IsFailed)
            return Result.Fail(wavResult.Errors.First().Message);

        return ProcessTranscription(wavResult.Value, resolvedModelPath, outputFileDir);
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

    private Result DownloadModel(IPath modelPath)
    {
        var whisperModelDownloader = new WhisperModelDownloader();
        _logger.LogInformation($"Downloading \"{modelPath}\"");

        var cts = new CancellationTokenSource();
        var handler = CreateCancelEventHandler(cts);

        Console.CancelKeyPress += handler;

        try
        {
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
                    var task = ctx.AddTask($"Downloading model \"{modelPath.Basename}\"");
                    task.IsIndeterminate = true;
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
                        task.StopTask();
                        Console.CancelKeyPress -= handler;
                        throw;
                    }
                    Console.CancelKeyPress -= handler;
                    task.StopTask();
                });

            return Result.Ok();
        }
        catch (OperationCanceledException)
        {
            return Result.Fail("Model download cancelled");
        }
    }

    private Result ProcessTranscription(IPath wavFile, IPath modelPath, IPath outputDir)
    {
        using var cts = new CancellationTokenSource();
        var handler = CreateCancelEventHandler(cts);
        Console.CancelKeyPress += handler;

        try
        {
            AnsiConsole
                .Progress()
                .Columns(
                    new TaskDescriptionColumn() { Alignment = Justify.Left },
                    new ProgressBarColumn() { CompletedStyle = new Style(Color.Blue) },
                    new CountColumn() { Style = Color.Blue },
                    new ElapsedTimeColumn() { Style = Style.Plain },
                    new SquareBracedRemainingTimeColumn() { Style = Style.Plain }
                )
                .HideCompleted(true)
                .AutoClear(true)
                .Start(ctx =>
                {
                    var task = ctx.AddTask("[bold]Transcribing[/]", maxValue: 1);
                    var srtTask = RunWhisper(
                        wavFile,
                        modelPath.ToString()!,
                        outputDir,
                        cts.Token,
                        ctx
                    );
                    srtTask.Wait(cts.Token);
                    task.Increment(1);
                });
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

        AnsiConsole.MarkupLine($"[bold][blue]Finished {wavFile.BasenameWithoutExtensions}[/][/]");
        return Result.Ok();
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
            .Build();
        await using var fileStream = File.OpenRead(wavFileName);

        var max = (await FFProbe.AnalyseAsync(wavFileName, cancellationToken: ct)).Duration;
        var currentTask = progressContext?.AddTask(
            $"[bold]{TruncateString(wavFilePath.Basename, 16)}[/]",
            maxValue: 100
        );

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
            return Result.Fail("Could not convert. Lacks a primary audio stream");

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

internal class CountColumn : ProgressColumn
{
    public string? Unit { get; set; }

    /// <summary>
    /// Gets or sets the style of the remaining text.
    /// </summary>
    public Style Style { get; set; } = Color.Blue;

    public override IRenderable Render(RenderOptions options, ProgressTask task, TimeSpan deltaTime)
    {
        var current = (long)task.Value;
        var total = (long)task.MaxValue;

        var text = total > 0 ? $"{current}/{total}" : $"{current}/?";

        if (Unit is not null)
            text += $" {Unit}";

        return new Markup($"{text}", Style);
    }
}

internal class SquareBracedRemainingTimeColumn : ProgressColumn
{
    /// <inheritdoc/>
    protected override bool NoWrap => true;

    /// <summary>
    /// Gets or sets the style of the remaining time text.
    /// </summary>
    public Style Style { get; set; } = Color.Blue;

    /// <inheritdoc/>
    public override IRenderable Render(RenderOptions options, ProgressTask task, TimeSpan deltaTime)
    {
        var remaining = task.RemainingTime;
        if (remaining == null)
        {
            return new Markup("[[--:--:--]]");
        }

        if (remaining.Value.TotalHours > 99 || task.IsIndeterminate)
        {
            return new Markup("[[**:**:**]]");
        }

        return new Text($"[{remaining.Value:hh\\:mm\\:ss}]", Style);
    }

    /// <inheritdoc/>
    public override int? GetColumnWidth(RenderOptions options)
    {
        return 10;
    }
}
