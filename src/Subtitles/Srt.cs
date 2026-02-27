namespace Subtitles;

public class Srt : ISubtitles
{
    public readonly List<SrtLine> Lines = [];
    public int LineCount => Lines.Count;
    
    public IEnumerator<SrtLine> GetEnumerator() => Lines.GetEnumerator();
    
    public SrtLine GetLine(int index) => Lines[index];

    public void AddLine(string text, string start, string end)
    {
        var newLine = new SrtLine(text, LineCount+1, start, end);
        Lines.Add(newLine);
    }

    public void WriteFile(string filename)
    {
        var file = File.OpenWrite(filename);
        using var writer = new StreamWriter(file);
        foreach (var line in Lines)
        {
            writer.WriteLine(line);
        }
        writer.Close();
        file.Close();
    }
}