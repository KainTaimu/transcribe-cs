using FluentResults;
using Subtitles;

namespace ASTranscriber.Translation;

public interface ITranslator
{
    public Result<ISubtitles> Translate(ISubtitles subtitles);
}
