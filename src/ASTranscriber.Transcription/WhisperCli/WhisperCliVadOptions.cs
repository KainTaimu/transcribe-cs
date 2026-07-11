namespace ASTranscriber.Transcription.WhisperCli;

public readonly record struct WhisperCliVadOptions(string VadModelPath, float VadThreshold);
