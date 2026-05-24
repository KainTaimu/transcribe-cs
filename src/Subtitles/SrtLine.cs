using System.Text;

namespace Subtitles;

public struct SrtLine(string text, int lineNumber, string start, string end)
{
    public string Text = text;
    public int LineNumber = lineNumber;
    public TimeSpan Start = TimeSpan.Parse(start);
    public TimeSpan End = TimeSpan.Parse(end);

    private const string FORMAT = @"hh\:mm\:ss\,fff";

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
