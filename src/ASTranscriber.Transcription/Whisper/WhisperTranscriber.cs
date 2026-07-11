using ASTranscriber.Core;
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

public class WhisperTranscriber(string whisperModelPath, SileroVadOptions? sileroVadOptions)
    : ITranscriber
{
    public event EventHandler<ProgressEventHandler>? ProgressChanged;

    private readonly ILogger<WhisperTranscriber> _logger = LoggerFactory
        .Create(builder => builder.SetMinimumLevel(LogLevel.Debug).AddNLog())
        .CreateLogger<WhisperTranscriber>();

    public Result<ISubtitles> Transcribe(
        string fileName,
        CancellationToken cancellationToken = default
    )
    {
        var wavResult = ToWhisperSupportedWav(fileName);
        if (wavResult.IsFailed)
            return Result.Fail(wavResult.Errors[0].Message);

        var result = ProcessTranscription(wavResult.Value, cancellationToken);

        if (wavResult.IsSuccess)
            File.Delete(wavResult.Value);
        if (result.IsSuccess)
            result.Value.WriteFile(Path.GetTempFileName());

        return result.Map(ISubtitles (srt) => srt);
    }

    private Result<Srt> ProcessTranscription(string wavFile, CancellationToken ct)
    {
        Srt? srt = null;
        var srtTask = sileroVadOptions is null
            ? RunWhisper(wavFile, ct)
            : RunWhisperVad(wavFile, sileroVadOptions, ct);

        srtTask.Wait(ct);

        if (srtTask.IsCompleted)
        {
            var result = srtTask.Result;
            if (result.IsSuccess)
                srt = srtTask.Result.Value;
            else
                return result.WithError("Transcription failed");
        }

        return Result.Ok(srt)!;
    }

    private async Task<Result<Srt>> RunWhisperVad(
        string wavPath,
        SileroVadOptions vadOptions,
        CancellationToken ct
    )
    {
        using var whisperFactory = WhisperFactory.FromPath(whisperModelPath);
        var processor = whisperFactory
            .CreateBuilder()
            .WithLanguage("auto")
            .WithNoSpeechThreshold(vadOptions.Threshold);
        var whisperSpeechTranscriptor = new WhisperSpeechTranscriptorFactory(processor);

        var sileroFactory = new SileroVadDetectorFactory(vadOptions);

        var realTimeFactory = new EchoSharpRealtimeTranscriptorFactory(
            whisperSpeechTranscriptor,
            sileroFactory
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

        var total = (await FFProbe.AnalyseAsync(wavPath, cancellationToken: ct)).Duration;
        var srt = new Srt();

        await foreach (var segmentEvent in transcriptor.TranscribeAsync(awaitableSource, ct))
        {
            if (ct.IsCancellationRequested)
                break;

            var speechSegment = segmentEvent as RealtimeSegmentRecognized;
            if (speechSegment is null)
                continue;

            var result = speechSegment.Segment;
            var end = result.StartTime + result.Duration;

            AnsiConsole.MarkupLine($"[dim][[{result.StartTime:G}->{end:G}]][/] {result.Text}");
            srt.AddLine(result.Text, result.StartTime, end);

            OnProgressChanged(
                new ProgressEventHandler(
                    (float)result.StartTime.TotalSeconds,
                    (float)total.TotalSeconds,
                    "Transcribing",
                    false
                )
            );
        }

        return Result.Ok(srt);
    }

    private async Task<Result<Srt>> RunWhisper(string wavPath, CancellationToken ct)
    {
        using var whisperFactory = WhisperFactory.FromPath(whisperModelPath);
        await using var processor = whisperFactory
            .CreateBuilder()
            .WithLanguage("auto")
            .WithNoContext()
            .WithTemperature(0)
            .Build();
        await using var fileStream = File.OpenRead(wavPath);

        var total = (await FFProbe.AnalyseAsync(wavPath, cancellationToken: ct)).Duration;

        var srt = new Srt();

        await foreach (var result in processor.ProcessAsync(fileStream, ct))
        {
            AnsiConsole.MarkupLine($"[dim][[{result.Start:G}->{result.End:G}]][/] {result.Text}");
            srt.AddLine(result.Text, result.Start, result.End);

            OnProgressChanged(
                new ProgressEventHandler(
                    (float)result.Start.TotalSeconds,
                    (float)total.TotalSeconds,
                    "Transcribing",
                    false
                )
            );

            if (ct.IsCancellationRequested)
                break;
        }

        return Result.Ok(srt);
    }

    private Result<string> ToWhisperSupportedWav(string inputFilePath)
    {
        OnProgressChanged(new ProgressEventHandler(0, 1, "Converting to WAV"));

        try
        {
            var primaryAudioStream = FFProbe.Analyse(inputFilePath).PrimaryAudioStream;
            if (primaryAudioStream is null)
                return Result.Fail("Could not convert. Lacks a primary audio stream");
        }
        catch (FFMpegException e)
        {
            return Result.Fail($"Failed to get audio streams: {e}");
        }

        var outputPath = Path.GetTempFileName() + ".wav";
        _logger.LogDebug($"saving to {outputPath}");

        FFMpegArguments
            .FromFileInput(inputFilePath)
            .OutputToFile(
                outputPath,
                true,
                opt =>
                    opt.WithoutMetadata().WithAudioSamplingRate(16_000).WithCustomArgument("-ac 1")
            )
            .WithLogLevel(FFMpegLogLevel.Quiet)
            .ProcessSynchronously();

        return Result.Ok(outputPath);
    }

    private void OnProgressChanged(ProgressEventHandler e)
    {
        ProgressChanged?.Invoke(this, e);
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
                Console.CancelKeyPress -= handler;
                throw;
            }
            Console.CancelKeyPress -= handler;

            return Result.Ok();
        }
        catch (OperationCanceledException)
        {
            return Result.Fail("Model download cancelled");
        }
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
