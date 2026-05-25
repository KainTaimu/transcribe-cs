using ASTranscriber.Transcription.Whisper;
using FluentResults;
using Subtitles;

namespace ASTranscriber.Transcription;

public interface ITranscriber
{
    event EventHandler<ProgressEventHandler>? ProgressChanged;

    public Result<ISubtitles> Transcribe(
        string fileName,
        CancellationToken cancellationToken = default
    );
}
