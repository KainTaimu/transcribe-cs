namespace ASTranscriber.Core;

public class TaskMessageEventHandler(string message) : EventArgs
{
    public readonly string Message = message;
}
