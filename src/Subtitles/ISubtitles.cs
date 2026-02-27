namespace Subtitles;

public interface ISubtitles
{
    public void AddLine(string text, string start, string end);
    public void WriteFile(string filename);
}