using ASTranscriber.Transcription.Dummy;
using ASTranscriber.Transcription.Whisper;
using ASTranscriber.Transcription.WhisperCli;

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
            WhisperCliTranscriberOptions whisperCliOptions => new WhisperCliTranscriber(
                whisperCliOptions
            ),
            _ => throw new ArgumentException(
                $"Unsupported translator options type: {options.GetType().FullName}"
            ),
        };
    }
}
