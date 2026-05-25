using ASTranscriber.Core;
using FluentResults;
using Subtitles;

namespace ASTranscriber.Translation;

public interface ITranslator
{
    event EventHandler<ProgressEventHandler>? ProgressChanged;

    public Result<ISubtitles> Translate(
        ISubtitles subtitles,
        CancellationToken cancellationToken = default
    );
}
