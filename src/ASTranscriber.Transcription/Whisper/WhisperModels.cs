using Whisper.net.Ggml;

namespace ASTranscriber.Transcription.Whisper;

public static class WhisperModels
{
    public static readonly string[] ModelNames =
    [
        "tiny",
        "tiny.en",
        "tiny-q5_1",
        "tiny.en-q5_1",
        "tiny-q8_0",
        "base",
        "base.en",
        "base-q5_1",
        "base.en-q5_1",
        "base-q8_0",
        "small",
        "small.en",
        "small.en-tdrz",
        "small-q5_1",
        "small.en-q5_1",
        "small-q8_0",
        "medium",
        "medium.en",
        "medium-q5_0",
        "medium.en-q5_0",
        "medium-q8_0",
        "large-v1",
        "large-v2",
        "large-v2-q5_0",
        "large-v2-q8_0",
        "large-v3",
        "large-v3-q5_0",
        "large-v3-turbo",
        "large-v3-turbo-q5_0",
        "large-v3-turbo-q8_0",
    ];

    public static readonly Dictionary<string, QuantizationType> QuantizationToQuantizationType =
        new()
        {
            { "q5_0", QuantizationType.Q5_0 },
            { "q5_1", QuantizationType.Q5_1 },
            { "q8_0", QuantizationType.Q8_0 },
        };

    public static readonly Dictionary<string, GgmlType> ModelNameToGgmlType = new()
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

    // public static (GgmlType, QuantizationType) GetModel(string modelName) {
    // }
}
