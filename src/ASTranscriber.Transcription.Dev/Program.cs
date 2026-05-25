using ASTranscriber.Core.Extensions;
using ASTranscriber.Transcription.Whisper;
using PathLib;
using VadDotNet;

var audioPath = new PosixPath("/home/erwin/Projects/csharp/transcribe-cs/inputs/RJ01566391.mp3");
var audioFile = new FileInfo(audioPath.ToPathString());

var vadPath = new PosixPath(
    "/home/erwin/Projects/csharp/transcribe-cs/models/ggml-silero-v6.2.0.bin"
);

var silero = new SileroVadDetector(
    "/home/erwin/Projects/csharp/transcribe-cs/models/silero_vad.onnx",
    0.0001f,
    16_000,
    250,
    float.MaxValue,
    100,
    30
);

var segments = silero.GetSpeechSegmentList(audioFile);

foreach (var segment in segments)
{
    Console.WriteLine(
        $"{segment.StartSecond} -> {segment.EndSecond} | {segment.StartOffset} -> {segment.EndOffset}"
    );
}

return;

var whisperModel = new PosixPath(
    "/home/erwin/Projects/csharp/transcribe-cs/models/large-v3-turbo-q5_0.bin"
);

var transcriber = new WhisperTranscriber(whisperModel);
transcriber.Start(audioPath, new PosixPath("/home/erwin/Projects/csharp/transcribe-cs/outputs"));
