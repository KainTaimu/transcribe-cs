using ASTranscriber.Transcription.Whisper;

namespace ASTranscriber.Transcription;

public class TranscriberFactory : ITranscriberFactory
{
    public ITranscriber Create(ITranscriberOptions options)
    {
        return options switch
        {
            WhisperTranscriberOptions whisperTranscriberOptions => new WhisperTranscriber(
                whisperTranscriberOptions.WhisperModelPath,
                whisperTranscriberOptions.SileroVadOptions
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };
    }
}
