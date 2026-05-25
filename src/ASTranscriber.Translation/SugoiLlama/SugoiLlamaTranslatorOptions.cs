namespace ASTranscriber.Translation.SugoiLlama;

public class SugoiLlamaTranslatorOptions(string llamaServerPath, string sugoiModelPath)
    : ITranslatorOptions
{
    public readonly string LlamaServerPath = llamaServerPath;
    public string SugoiModelPath { get; init; } = sugoiModelPath;
}
