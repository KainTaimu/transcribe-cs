using FluentResults;
using PathLib;
using Spectre.Console;

namespace ASTranscriber.Translation;

public class SugoiTranslator : ITranslator
{
    public Result<IPath> Start(IPath srtPath, ProgressContext? progressContext = null)
    {
        var task = progressContext?.AddTask("Translating", maxValue: 100);
        while (!task!.IsFinished)
        {
            task.Increment(5);
            Thread.Sleep(1000);
        }

        return Result.Ok();
    }
}
