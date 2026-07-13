namespace ASTranscriber.Core;

public class TaskProgressEventHandler(
    float current,
    float total,
    string description,
    string? message = null,
    bool isIndeterminate = true
) : EventArgs
{
    public float Current { get; } = current;
    public float Total { get; } = total;
    public bool IsIndeterminate { get; } = isIndeterminate;
    public string Description { get; } = description;

    /// <summary>
    /// Additional text to print to AnsiConsole.MarkupLine
    /// </summary>
    public string? Message { get; } = message;
}
