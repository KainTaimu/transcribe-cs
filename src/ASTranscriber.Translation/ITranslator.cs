using FluentResults;
using PathLib;
using Spectre.Console;

namespace ASTranscriber.Translation;

public interface ITranslator
{
    public Result<IPath> Start(
        IPath srtPath,
        IPath outputDirectory,
        ProgressContext? progressContext = null
    );

    public Task<Result<IPath>> StartAsyc(
        IPath srtPath,
        IPath outputDirectory,
        ProgressContext? progressContext = null
    );
}
