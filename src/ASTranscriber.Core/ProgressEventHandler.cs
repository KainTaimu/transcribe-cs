namespace ASTranscriber.Core;

public class ProgressEventHandler(float current, float total, bool isIndeterminate = true)
    : EventArgs
{
    public float Current { get; } = current;
    public float Total { get; } = total;
    public bool IsIndeterminate { get; } = isIndeterminate;
}
