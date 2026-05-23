namespace ASTranscriber.Core.Extensions;

public static class StringExtensions
{
    extension(string s)
    {
        public string Ellipsize(int length)
        {
            const string ellipse = " ... ";
            var sLen = s.Length;
            var termWidth = Console.WindowWidth / 4;
            if (sLen <= length + ellipse.Length || termWidth < ellipse.Length)
                return s;
            var keep = (termWidth - ellipse.Length) / 2;
            return s[..keep] + " ... " + s[(sLen - keep)..];
        }
    }
}
