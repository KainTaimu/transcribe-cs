namespace Subtitles;

public interface ISubtitles
{
    public void AddLine(string text, string start, string end);

    public void WriteFile(string filename);

    /// <summary>
    /// Populate this subtitle from the contents of the file.
    /// </summary>
    /// <remarks>
    /// Will clear all previously added lines
    /// </remarks>
    /// <param name="filename"></param>
    /// <exception cref="SubtitleParseException"></exception>
    public void Read(string filename);

    public bool TryRead(string filename);
}
