using FFMpegCore;
using FFMpegCore.Enums;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;

namespace ASTranscriber.Transcription.Whisper;

public class WhisperPreprocessor
{
    private static ILogger<WhisperTranscriber> _logger = LoggerFactory
        .Create(builder => builder.SetMinimumLevel(LogLevel.Debug).AddNLog())
        .CreateLogger<WhisperTranscriber>();

    public static string? ToWhisperSupportedWav(string path)
    {
        _logger.LogInformation($"Converting \"{path}\" to whisper supported format");

        var primaryAudioStream = FFProbe.Analyse(path).PrimaryAudioStream;
        if (primaryAudioStream is null)
        {
            _logger.LogError($"Could not convert. Lacks a primary audio stream");
            return null;
        }

        var outputPath = Path.GetTempFileName();
        outputPath = Path.ChangeExtension(outputPath, ".wav");
        _logger.LogDebug($"saving to {outputPath}");
        FFMpegArguments
            .FromFileInput(path)
            .OutputToFile(
                outputPath,
                true,
                opt =>
                {
                    opt.WithoutMetadata().WithAudioSamplingRate(16_000).WithCustomArgument("-ac 2");
                }
            )
            .WithLogLevel(FFMpegLogLevel.Quiet)
            .ProcessSynchronously();

        return outputPath;
    }
}
