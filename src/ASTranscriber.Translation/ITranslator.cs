using ASTranscriber.Core;
using FluentResults;
using Subtitles;

namespace ASTranscriber.Translation;

public interface ITranslator
{
    event EventHandler<TaskProgressEventHandler>? ProgressChanged;
    event EventHandler<TaskMessageEventHandler>? MessageReceived;

    public Result<ISubtitles> Translate(
        ISubtitles subtitles,
        CancellationToken cancellationToken = default
    );
}
