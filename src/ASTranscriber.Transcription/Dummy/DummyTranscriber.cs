using ASTranscriber.Core;
using FluentResults;
using Subtitles;

namespace ASTranscriber.Transcription.Dummy;

public class DummyTranscriber : ITranscriber
{
#pragma warning disable CS0067
    public event EventHandler<ProgressEventHandler>? ProgressChanged;

    public Result<ISubtitles> Transcribe(
        string fileName,
        CancellationToken cancellationToken = default
    )
    {
        var srt = new Srt();
        var dummySrtFile = Environment.GetEnvironmentVariable("DUMMY_SRTFILE");

        if (dummySrtFile is not null)
            srt.Read(dummySrtFile);

        return Result.Ok<ISubtitles>(srt);
    }
}
