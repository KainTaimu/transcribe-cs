using FluentResults;
using PathLib;
using Spectre.Console;

namespace ASTranscriber.Transcription;

public interface ITranscriber
{
    public Result<IPath> Start(
        IPath inputFile,
        IPath outputDirectory,
        ProgressContext? progressContext = null
    );

    public IPath? GetTranscriptionSrt();
}
