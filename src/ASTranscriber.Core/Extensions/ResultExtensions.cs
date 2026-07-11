using FluentResults;

namespace ASTranscriber.Core.Extensions;

public static class ResultExtensions
{
    extension(ResultBase result)
    {
        public string UnwindErrors(string? message = null)
        {
            var ss = result.Errors.Select(e => e.Message);
            return message is not null
                ? $"{message}: {string.Join(": ", ss)}"
                : string.Concat(": ", ss);
        }
    }
}
