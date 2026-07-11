namespace ASTranscriber.Transcription.WhisperCli;

public record WhisperCliTranscriberOptions(
    string WhisperModelPath,
    string SpokenLanguage,
    WhisperCliVadOptions? VadOptions
) : ITranscriberOptions;
