using System.Text;

namespace Subtitles;

public class SrtParseException : Exception;

public class Srt : ISubtitles
{
    public readonly List<SrtLine> Lines = [];
    public int LineCount => Lines.Count;

    public IEnumerator<SrtLine> GetEnumerator() => Lines.GetEnumerator();

    public SrtLine GetLine(int index) => Lines[index];

    public void AddLine(string text, string start, string end)
    {
        var newLine = new SrtLine(text, LineCount + 1, start, end);
        Lines.Add(newLine);
    }

    public void AddLine(SrtLine line)
    {
        Lines.Add(line);
    }

    public static bool TryReadFile(string filename, out Srt srt)
    {
        srt = new Srt();

        using var reader = new StreamReader(filename);
        string line;

        while ((line = reader.ReadLine()) != null)
        {
            // Skip blank lines
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var entry = new SrtLine();

            // 1. Index line
            if (!int.TryParse(line.Trim(), out int index))
                continue;
            entry.LineNumber = index;

            // 2. Timestamp line
            line = reader.ReadLine();
            if (line == null)
                return false;

            var parts = line.Split([" --> "], StringSplitOptions.None);
            if (parts.Length != 2)
                continue;

            entry.StartString = parts[0].Trim().Replace(',', '.');
            entry.EndString = parts[1].Trim().Replace(',', '.');

            // 3. Text (may span multiple lines)
            var textLines = new List<string>();
            while ((line = reader.ReadLine()) != null && !string.IsNullOrWhiteSpace(line))
                textLines.Add(line);

            entry.Text = string.Join(Environment.NewLine, textLines);
            srt.AddLine(entry);
        }

        return true;
    }

    public void WriteFile(string filename)
    {
        using var file = File.OpenWrite(filename);
        using var writer = new StreamWriter(file);
        foreach (var line in Lines)
        {
            writer.WriteLine(line);
        }
    }
}
