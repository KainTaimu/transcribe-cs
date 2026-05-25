namespace ASTranscriber.Transcription.Whisper;

public class WhisperTranscriberFactory : IConcreteTranscriberFactory
{
    public ITranscriber CreateTranscriber(ITranscriberOptions options)
    {
        if (options is not WhisperTranscriberOptions optionsResult)
            throw new ArgumentException(
                $"Invalid options type. Expected {nameof(WhisperTranscriberOptions)}, got {options.GetType().Name}"
            );

        var whisper = new WhisperTranscriber(
            optionsResult.WhisperModelPath,
            optionsResult.SileroVadOptions
        );
        return whisper;
    }
}
