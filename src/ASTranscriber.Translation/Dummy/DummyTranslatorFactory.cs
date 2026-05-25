namespace ASTranscriber.Translation.Dummy;

public class DummyTranslatorFactory : ITranslatorFactory
{
    public ITranslator Create(ITranslatorOptions options)
    {
        return new DummyTranslator();
    }
}
