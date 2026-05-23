using PathLib;

namespace ASTranscriber.Core.Extensions;

public static class PathExtensions
{
    extension(IPath path)
    {
        public string ToPathString() =>
            path.ToString() ?? throw new InvalidOperationException("Path produced a null string.");
    }
}
