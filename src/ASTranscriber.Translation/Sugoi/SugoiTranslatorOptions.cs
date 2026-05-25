namespace ASTranscriber.Translation;

public class SugoiTranslatorOptions(string sugoiModelPath) : ITranslatorOptions
{
    public string SugoiModelPath { get; init; } = sugoiModelPath;
}
