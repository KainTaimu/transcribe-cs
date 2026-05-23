using ASTranscriber.Core;
using FFMpegCore;
using FFMpegCore.Enums;
using FluentResults;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;
using PathLib;
using ShellProgressBar;
using Subtitles;
using Whisper.net;

namespace ASTranscriber.Transcription.Whisper;

public class WhisperTranscriber : ITranscriber
{
    private readonly Queue<IPath> _jobs = new();
    private readonly IPath _inputDirectory;
    private readonly IPath _outputDirectory;
    private readonly IPath _modelPath;

    private readonly ILogger<WhisperTranscriber> _logger = LoggerFactory
        .Create(builder => builder.SetMinimumLevel(LogLevel.Debug).AddNLog())
        .CreateLogger<WhisperTranscriber>();

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
            try
            {
                using var pb = new IndeterminateProgressBar(
                    "Downloading",
                    _childProgressBarOptions
                );
                var downloadTask = whisperModelDownloader.DownloadWhisperModel(whisperModelPath);
                downloadTask.Wait();
                pb.Finished();
            }
            catch (ArgumentException e)
            {
                _logger.LogError($"Could not download model: {e.Message}");
                throw;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Could not download model: ");
                throw;
            }
        }

        _inputDirectory = inputDirPath;
        _outputDirectory = outputDirPath;
        _modelPath = whisperModelPath;
    }

    public Result Start()
    {
        var count = 1;
        var len = 0;
        foreach (var entry in _inputDirectory.ListDir().OrderBy(x => x.Basename))
        {
            if (!entry.IsFile())
                continue;
            if (!_allowedExtensions.Contains(entry.Extension))
            {
                _logger.LogWarning($"Ignoring unsupported file: {entry.Filename}");
                continue;
            }
            _logger.LogDebug($"{count++}: {entry.Filename}");
            _jobs.Enqueue(entry);
            len++;
        }

        count = 1;

        using var pb = new ProgressBar(len, "", _mainProgressBarOptions);
        while (_jobs.Count != 0)
        {
            var job = _jobs.Dequeue();
            pb.Message =
                $"[{count++}/{len}] Processing \"{Path.GetRelativePath(Environment.CurrentDirectory, job.ToString())}\"";

            var cts = new CancellationTokenSource();
            ConsoleCancelEventHandler onExit = (_, e) =>
            {
                e.Cancel = true; // Don't kill the process immediately
                cts.Cancel();
            };
            try
            {
                Console.CancelKeyPress += onExit;

                var wav = ToWhisperSupportedWav(
                    Path.GetRelativePath(Environment.CurrentDirectory, job.ToString()),
                    pb
                );
                if (wav.IsFailed)
                {
                    throw new Exception(wav.Errors.ToString());
                }

                var whisperTask = RunWhisper(wav.Value, _modelPath.ToString(), pb, cts.Token);
                whisperTask.Wait(cts.Token);
            }
            catch (OperationCanceledException e) { }
            catch (Exception e)
            {
                pb.WriteErrorLine("FAILED" + e.Message);
                _logger.LogError(e, "FAILURE ");
            }
            finally
            {
                Console.CancelKeyPress -= onExit;
                cts.Dispose();
            }

            pb.Tick();
        }
        _logger.LogInformation("Done.");

        return Result.Ok();
    }

    private async Task<WhisperTranscriptionException?> RunWhisper(
        string wavFileName,
        string modelPath,
        ProgressBar pb,
        CancellationToken ct
    )
    {
        using var whisperFactory = WhisperFactory.FromPath(modelPath);
        await using var processor = whisperFactory
            .CreateBuilder()
            .WithLanguage("auto")
            .WithAudioContextSize(0)
            .WithTemperature(0)
            .Build();
        await using var fileStream = File.OpenRead(wavFileName);

        var max = FFProbe.Analyse(wavFileName).Duration;
        var childPb = pb.Spawn(100, "Transcribing...");
        var progress = childPb.AsProgress<double>();

        // TODO: Use ISubtitles
        var srt = new Srt();

        await foreach (var result in processor.ProcessAsync(fileStream, ct))
        {
            if (ct.IsCancellationRequested)
            {
                _logger.LogError("Cancelling.");
                return null;
            }
            progress.Report(result.End / max);
            pb.WriteLine($"[{result.Start:g}->{result.End:g}] {result.Text}");
            srt.AddLine(result.Text, result.Start.ToString(), result.End.ToString());
        }

        srt.WriteFile(
            _outputDirectory
                .Join([$"{Path.GetFileNameWithoutExtension(wavFileName)}.srt"])
                .ToString()
        );

        return null;
    }

    public IPath? GetTranscriptionSrt()
    {
        throw new NotImplementedException();
    }

    public Result<string> ToWhisperSupportedWav(string path, ProgressBar pb)
    {
        _logger.LogInformation($"Converting \"{path}\" to whisper supported format");

        var primaryAudioStream = FFProbe.Analyse(path).PrimaryAudioStream;
        if (primaryAudioStream is null)
        {
            return Result.Fail("Could not convert. Lacks a primary audio stream");
        }

        var outputPath = Path.GetTempFileName();
        outputPath = Path.ChangeExtension(outputPath, ".wav");
        _logger.LogDebug($"saving to {outputPath}");

        var childPb = pb.SpawnIndeterminate("Converting to wav", _childProgressBarOptions);

        FFMpegArguments
            .FromFileInput(path)
            .OutputToFile(
                outputPath,
                true,
                opt =>
                    opt.WithoutMetadata().WithAudioSamplingRate(16_000).WithCustomArgument("-ac 1")
            )
            .WithLogLevel(FFMpegLogLevel.Quiet)
            .ProcessSynchronously();

        childPb.Finished();

        return Result.Ok(outputPath);
    }

    private class TranscriptionJob(IPath filePath)
    {
        public IPath FilePath = filePath;
    }

    private readonly ProgressBarOptions _mainProgressBarOptions = new()
    {
        ProgressCharacter = '─',
        ProgressBarOnBottom = true,
        CollapseWhenFinished = true,
    };

    private readonly ProgressBarOptions _childProgressBarOptions = new()
    {
        ProgressCharacter = '─',
        ProgressBarOnBottom = true,
        CollapseWhenFinished = true,
    };
}
