using LLama;
using LLama.Common;
using PathLib;

namespace ASTranscriber.Translation;

public class SugoiTranslator : ITranslator
{
    public uint ContextSize { get; set; } = 2048;
    public int GpuLayerCount { get; set; } = -1;
    public bool FlashAttention { get; set; } = true;

    public bool IsModelLoaded => _llamaWeights is not null;

    private readonly IPath _sugoiGgufModelPath;
    private LLamaWeights? _llamaWeights;

    public SugoiTranslator(string sugoiGgufModelPath)
    {
        // TODO: Do valid model file checking
        var factory = new PathFactory();
        var path = factory.Create(sugoiGgufModelPath);

        if (!path.IsFile())
            throw new ArgumentException($"{sugoiGgufModelPath} is not a file");

        _sugoiGgufModelPath = path;
    }

    ~SugoiTranslator()
    {
        _llamaWeights?.Dispose();
    }

    public void LoadModel()
    {
        var parameters = new ModelParams(_sugoiGgufModelPath.ReadAsText())
        {
            ContextSize = ContextSize,
            GpuLayerCount = GpuLayerCount,
            FlashAttention = FlashAttention,
        };
        _llamaWeights = LLamaWeights.LoadFromFile(parameters);
    }

    public void TranslateString(string s)
    {
        throw new NotImplementedException();
    }
}
