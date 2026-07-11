namespace ASTranscriber.Cli;

public class RunSummary { }

public struct RunDetails(string fileName)
{
    public required string FileName = fileName;
    public RunResult? Result;
    public TimeSpan Time;
}

public enum RunResult
{
    Failed,
    Done,
    Unfinished,
}
