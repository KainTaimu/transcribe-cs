using ASTranscriber.Core.Extensions;
using EchoSharp.Abstractions.SpeechTranscription;
using EchoSharp.Abstractions.VoiceActivityDetection;
using EchoSharp.Audio;
using EchoSharp.Onnx.SileroVad;
using EchoSharp.SpeechTranscription;
using EchoSharp.Whisper.net;
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

public class WhisperTranscriber(IPath whisperModelPath, IPath? sileroVadModelPath) : ITranscriber
{
    private readonly ILogger<WhisperTranscriber> _logger = LoggerFactory
        .Create(builder => builder.SetMinimumLevel(LogLevel.Debug).AddNLog())
        .CreateLogger<WhisperTranscriber>();

    public Result<IPath> Start(
        IPath inputFile,
        IPath outputDirectory,
        ProgressContext? progressContext = null
    )
    {
        if (!whisperModelPath.IsFile())
        {
            var downloadResult = DownloadModel(whisperModelPath, progressContext);
            if (downloadResult.IsFailed)
                return downloadResult.WithError("Failed to download model");
        }

        var wavResult = ToWhisperSupportedWav(inputFile, progressContext);
        if (wavResult.IsFailed)
            return Result.Fail(wavResult.Errors[0].Message);

        var result = ProcessTranscription(
            wavResult.Value,
            whisperModelPath,
            outputDirectory,
            progressContext
        );

        if (result.IsSuccess)
            AnsiConsole.MarkupLine(
                $"[blue]Transcription completed: {Markup.Escape(inputFile.Basename)}[/]"
            );

        return result;
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
            Task<Result<IPath>> srtTask;
            if (sileroVadModelPath is null)
                srtTask = RunWhisper(
                    wavFile,
                    modelPath.ToString()!,
                    outputDir,
                    cts.Token,
                    progressContext
                );
            else
                srtTask = RunWhisperVad(
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

    private async Task<Result<IPath>> RunWhisperVad(
        IPath wavFilePath,
        string modelPath,
        IPath outputPath,
        CancellationToken ct,
        ProgressContext? progressContext = null
    )
    {
        if (sileroVadModelPath is null)
            return Result.Fail("Transcriber was not passed a silero model file");
        var wavPath = wavFilePath.ToPathString();

        using var whisperFactory = WhisperFactory.FromPath(modelPath);
        var processor = whisperFactory
            .CreateBuilder()
            .WithLanguage("auto")
            .WithNoContext()
            .WithTemperature(0)
            .WithSuppressRegex("");
        var whisperSpeechTranscriptor = new WhisperSpeechTranscriptorFactory(processor);

        var vadOptions = new SileroVadOptions(sileroVadModelPath.ToPathString())
        {
            Threshold = 0.0005f,
        };
        var sileroFactory = new SileroVadDetectorFactory(vadOptions);

        var realTimeFactory = new EchoSharpRealtimeTranscriptorFactory(
            whisperSpeechTranscriptor,
            sileroFactory,
            vadDetectorOptions: new VadDetectorOptions()
            {
                MinSilenceDuration = new TimeSpan(0, 0, 0, 0, 100),
                MinSpeechDuration = new TimeSpan(0, 0, 0, 0, 250),
            }
        );

        var transcriptor = realTimeFactory.Create(
            new RealtimeSpeechTranscriptorOptions()
            {
                AutodetectLanguageOnce = true,
                IncludeSpeechRecogizingEvents = true,
                RetrieveTokenDetails = false,
                LanguageAutoDetect = true,
            }
        );

        var awaitableSource = new AwaitableWaveFileSource();

        await using var wavStream = File.OpenRead(wavPath);
        var buf = new byte[4096];
        int bytesRead;

        while ((bytesRead = await wavStream.ReadAsync(buf, ct)) > 0)
        {
            awaitableSource.WriteData(buf.AsMemory(0, bytesRead));
        }
        awaitableSource.Flush();

        var max = (await FFProbe.AnalyseAsync(wavPath, cancellationToken: ct)).Duration;
        var currentTask = progressContext?.AddTask($"[bold]Transcribing[/]", maxValue: 100);
        var srt = new Srt();

        await foreach (var segmentEvent in transcriptor.TranscribeAsync(awaitableSource, ct))
        {
            var speechSegment = segmentEvent as RealtimeSegmentRecognized;
            if (speechSegment is null)
                continue;

            var result = speechSegment.Segment;

            var end = result.StartTime + result.Duration;
            currentTask?.Value = (end / max) * 100;

            AnsiConsole.MarkupLine($"[[{result.StartTime:G}->{end:G}]] {result.Text}");
            srt.AddLine(result.Text, result.StartTime.ToString(), end.ToString());

            if (ct.IsCancellationRequested)
            {
                currentTask?.StopTask();
                break;
            }
        }

        currentTask?.StopTask();

        var outPath = outputPath.Join($"{wavFilePath.BasenameWithoutExtensions}.srt");
        srt.WriteFile(outPath.ToPathString());

        return Result.Ok(outPath);
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

        currentTask?.StopTask();

        var outPath = outputPath.Join($"{Path.GetFileNameWithoutExtension(wavFileName)}.srt");
        srt.WriteFile(outPath.ToPathString());

        return Result.Ok(outPath);
    }

    private Result<IPath> ToWhisperSupportedWav(IPath path, ProgressContext? progressContext = null)
    {
        var progressTask = progressContext?.AddTask("[bold]Converting to WAV[/]");
        progressTask?.IsIndeterminate = true;

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

        progressTask?.StopTask();

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
