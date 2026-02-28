using PathLib;
using Whisper.net.Ggml;

namespace ASTranscriber.Transcription.Whisper;

public class WhisperModelDownloader
{
    public WhisperModelDownloader() { }

    public async Task DownloadWhisperModel(IPath modelDir)
    {
        GgmlType ggmlType;
        QuantizationType quantizationType;
        switch (modelDir.BasenameWithoutExtensions)
        {
            case "small-q5_1":
                ggmlType = GgmlType.Small;
                quantizationType = QuantizationType.Q5_1;
                break;
            case "large-v3-q5_0":
                ggmlType = GgmlType.LargeV3;
                quantizationType = QuantizationType.Q5_0;
                break;
            case "large-v3-turbo-q8_0":
                ggmlType = GgmlType.LargeV3Turbo;
                quantizationType = QuantizationType.Q8_0;
                break;
            default:
                throw new ArgumentException(
                    $"Unknown model name \"{modelDir.BasenameWithoutExtensions}\""
                );
        }

        await using var modelStream = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(
            ggmlType,
            quantizationType
        );
        await using var fileWriter = modelDir.Open(FileMode.Create);
        await modelStream.CopyToAsync(fileWriter);
    }
}
