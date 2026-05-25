namespace ASTranscriber.Transcription.Whisper;

public class ProgressEventHandler(float current, float total) : EventArgs
{
    public float Current { get; } = current;
    public float Total { get; } = total;
}
