using FluentResults;
using PathLib;
using Spectre.Console;

namespace ASTranscriber.Translation;

public interface ITranslator
{
    public Result<IPath> Start(IPath srtPath, ProgressContext? progressContext = null);
}
