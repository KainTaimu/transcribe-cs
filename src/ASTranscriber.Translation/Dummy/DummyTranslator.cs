using ASTranscriber.Core;
using FluentResults;
using Subtitles;

namespace ASTranscriber.Translation.Dummy;

public class DummyTranslator : ITranslator
{
#pragma warning disable CS0067
    public event EventHandler<TaskProgressEventHandler>? ProgressChanged;
    public event EventHandler<TaskMessageEventHandler>? MessageReceived;

    public Result<ISubtitles> Translate(
        ISubtitles subtitles,
        CancellationToken cancellationToken = default
    )
    {
        return Result.Ok(subtitles);
    }
}
