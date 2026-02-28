using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;
using PathLib;
using Whisper.net.Ggml;

namespace ASTranscriber.Transcription.Whisper;

public class WhisperModelDownloader
{
    private readonly ILogger<WhisperTranscriber> _logger = LoggerFactory
        .Create(builder => builder.SetMinimumLevel(LogLevel.Debug).AddNLog())
        .CreateLogger<WhisperTranscriber>();
    
    public async Task DownloadWhisperModel(IPath modelDir)
    {
        var (ggmlType, quantizationType) = WhisperModels.GetModel(modelDir.Basename);
        
        _logger.LogDebug("{ModelDirBasename} is ({GgmlType}, {QuantizationType})", modelDir.Basename, ggmlType, quantizationType);   

        await using var modelStream = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(
            ggmlType,
            quantizationType
        );
        await using var fileWriter = modelDir.Open(FileMode.Create);
        await modelStream.CopyToAsync(fileWriter);
    }
}
