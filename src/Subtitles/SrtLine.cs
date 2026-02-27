using System.Text;

namespace Subtitles;

public struct SrtLine(string text, int lineNumber, string start, string end)
{
    public string Text = text;
    public int LineNumber = lineNumber;
    public TimeSpan Start = TimeSpan.Parse(start);
    public TimeSpan End = TimeSpan.Parse(end);

    public override string ToString()
    {
        const string format = @"hh\:mm\:ss\,fff";
        var builder = new StringBuilder();
        builder.AppendLine(LineNumber.ToString());
        builder.AppendLine($"{Start.ToString(format)} --> {End.ToString(format)}");
        builder.AppendLine(Text);
        return builder.ToString();
    }
}