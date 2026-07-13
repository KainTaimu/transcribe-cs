using ASTranscriber.Core;
using FluentResults;
using Subtitles;

namespace ASTranscriber.Transcription;

public interface ITranscriber
{
    event EventHandler<TaskProgressEventHandler>? ProgressChanged;
    event EventHandler<TaskMessageEventHandler>? MessageReceived;

    public Result<ISubtitles> Transcribe(
        string fileName,
        CancellationToken cancellationToken = default
    );
}
