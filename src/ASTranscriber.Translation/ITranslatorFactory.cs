namespace ASTranscriber.Translation;

public interface ITranslatorFactory
{
    public ITranslator Create(ITranslatorOptions options);
}
