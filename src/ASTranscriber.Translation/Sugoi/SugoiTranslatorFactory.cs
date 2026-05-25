namespace ASTranscriber.Translation;

public class SugoiTranslatorFactory : ITranslatorFactory
{
    public ITranslator Create(ITranslatorOptions options)
    {
        if (options is not SugoiTranslatorOptions optionsResult)
            throw new ArgumentException(
                $"Invalid options type. Expected {nameof(SugoiTranslatorOptions)}, got {options.GetType().Name}"
            );

        return new SugoiTranslator(optionsResult.SugoiModelPath);
    }
}
