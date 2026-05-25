namespace Subtitles;

public class Subtitles
{
    public static ISubtitles ReadSrt(string filename)
    {
        var srt = new Srt();
        return srt.Read(filename);
    }
}
