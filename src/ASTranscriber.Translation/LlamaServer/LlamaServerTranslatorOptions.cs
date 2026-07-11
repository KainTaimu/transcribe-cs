namespace ASTranscriber.Translation.LlamaServer;

public class LlamaServerTranslatorOptions(string llamaServerPath, string sugoiModelPath)
    : ITranslatorOptions
{
    public readonly string LlamaServerPath = llamaServerPath;
    public string SugoiModelPath { get; init; } = sugoiModelPath;
}
