using EchoSharp.Onnx.SileroVad;

namespace ASTranscriber.Transcription.Whisper;

public record WhisperTranscriberOptions(string WhisperModelPath, SileroVadOptions? SileroVadOptions)
    : ITranscriberOptions;
