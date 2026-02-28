using Whisper.net.Ggml;

namespace ASTranscriber.Transcription.Whisper;

public static class WhisperModels
{
    private static readonly Dictionary<string, QuantizationType> QuantizationToQuantizationType =
        new()
        {
            { "q5_0", QuantizationType.Q5_0 },
            { "q5_1", QuantizationType.Q5_1 },
            { "q8_0", QuantizationType.Q8_0 },
        };

    private static readonly Dictionary<string, GgmlType> ModelNameToGgmlType = new()
    {
        { "tiny", GgmlType.Tiny },
        { "tiny.en", GgmlType.TinyEn },
        { "base", GgmlType.Base },
        { "base.en", GgmlType.BaseEn },
        { "small", GgmlType.Small },
        { "small.en", GgmlType.SmallEn },
        { "medium", GgmlType.Medium },
        { "medium.en", GgmlType.MediumEn },
        { "large-v1", GgmlType.LargeV1 },
        { "large-v2", GgmlType.LargeV2 },
        { "large-v3", GgmlType.LargeV3 },
        { "large-v3-turbo", GgmlType.LargeV3Turbo },
    };

    public static (GgmlType, QuantizationType) GetModel(string modelName)
    {
        var match = QuantizationToQuantizationType
            .FirstOrDefault(kvp => modelName.EndsWith($"-{kvp.Key}", StringComparison.OrdinalIgnoreCase));

        var quantization = match.Key is not null ? match.Value : QuantizationType.NoQuantization;
        var baseName = match.Key is not null ? modelName[..^(match.Key.Length + 1)] : modelName;

        if (!ModelNameToGgmlType.TryGetValue(baseName, out var ggmlType))
            throw new ArgumentException("Unknown model name: " + modelName);

        return (ggmlType, quantization);
    }
}
