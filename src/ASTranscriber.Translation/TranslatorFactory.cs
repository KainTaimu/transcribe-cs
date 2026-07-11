using ASTranscriber.Translation.Dummy;
using ASTranscriber.Translation.LlamaServer;

namespace ASTranscriber.Translation;

public class TranslatorFactory : ITranslatorFactory
{
    public ITranslator Create(ITranslatorOptions options)
    {
        return options switch
        {
            DummyTranslatorOptions => new DummyTranslator(),
            SugoiTranslatorOptions sugoiOptions => new SugoiTranslator(sugoiOptions.SugoiModelPath),
            LlamaServerTranslatorOptions llamaOptions => new LlamaServerTranslator(llamaOptions),
            _ => throw new ArgumentException(
                $"Unsupported translator options type: {options.GetType().FullName}"
            ),
        };
    }
}
