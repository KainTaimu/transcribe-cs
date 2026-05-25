using System.Text;

namespace Subtitles;

public struct SrtLine
{
    public string Text;
    public int LineNumber;
    public TimeSpan Start;
    public TimeSpan End;

    private const string FORMAT = @"hh\:mm\:ss\,fff";

    public SrtLine(string text, int lineNumber, TimeSpan start, TimeSpan end)
        : this()
    {
        Text = text;
        LineNumber = lineNumber;
        Start = start;
        End = end;
    }

    public SrtLine(string text, int lineNumber, string start, string end)
    {
        Text = text;
        LineNumber = lineNumber;
        Start = TimeSpan.Parse(start);
        End = TimeSpan.Parse(end);
    }

    public string StartString
    {
        get => Start.ToString(FORMAT);
        set => Start = TimeSpan.Parse(value);
    }

    public string EndString
    {
        get => End.ToString(FORMAT);
        set => End = TimeSpan.Parse(value);
    }

    public override string ToString()
    {
        var builder = new StringBuilder();
        builder.AppendLine(LineNumber.ToString());
        builder.AppendLine($"{Start.ToString(FORMAT)} --> {End.ToString(FORMAT)}");
        builder.AppendLine(Text);
        return builder.ToString();
    }
}
