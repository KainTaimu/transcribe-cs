using ASTranscriber.Transcription.Dummy;
using ASTranscriber.Transcription.Whisper;

namespace ASTranscriber.Transcription;

public class TranscriberFactory : ITranscriberFactory
{
    public ITranscriber Create(ITranscriberOptions options)
    {
        return options switch
        {
            DummyTranscriberOptions => new DummyTranscriber(),
            WhisperTranscriberOptions whisperOptions => new WhisperTranscriber(
                whisperOptions.WhisperModelPath,
                whisperOptions.SileroVadOptions
            ),
            _ => throw new ArgumentException(
                $"Unsupported translator options type: {options.GetType().FullName}"
            ),
        };
    }
}
