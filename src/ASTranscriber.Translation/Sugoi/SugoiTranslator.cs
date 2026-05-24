using System.Diagnostics;
using System.Threading.Channels;
using ASTranscriber.Core.Extensions;
using FluentResults;
using PathLib;
using Spectre.Console;
using Subtitles;

namespace ASTranscriber.Translation;

public class SugoiTranslator(IPath sugoiModelDirPath) : ITranslator
{
    public IPath SugoiModelDirPath { get; } = sugoiModelDirPath;

    public Result<IPath> Start(
        IPath srtPath,
        IPath outputDirectory,
        ProgressContext? progressContext = null
    )
    {
        if (Environment.OSVersion.Platform != PlatformID.Unix)
            throw new PlatformNotSupportedException(
                "SugoiTranslator is only supported on Unix-based systems."
            );
        var task = progressContext?.AddTask("Translating", maxValue: 1);
        task?.IsIndeterminate = true;

        if (!Srt.TryReadFile(srtPath.ToPathString(), out var srt))
            return Result.Fail("Failed to read SRT file.");

        var outputPath = outputDirectory / (srtPath.BasenameWithoutExtensions + "_translated.srt");

        using var cts = new CancellationTokenSource();
        var handler = CreateCancelEventHandler(cts);
        Console.CancelKeyPress += handler;

        try
        {
            var processTask = RunProcessAsync(
                "python3",
                [
                    "-u",
                    "/home/erwin/Projects/csharp/transcribe-cs/src/ASTranscriber.Translation/Sugoi/script/translate.py",
                    "--input",
                    srtPath.ToPathString(),
                    "--output",
                    outputPath.ToPathString(),
                    "--model",
                    SugoiModelDirPath.Resolve().ToPathString(),
                ],
                cts.Token
            );

            processTask.Wait(cts.Token);
        }
        catch (OperationCanceledException)
        {
            task?.StopTask();
            AnsiConsole.MarkupLine("[red]Cancelled[/]");
            Console.CancelKeyPress -= handler;
            return Result.Fail("Translation cancelled");
        }
        finally
        {
            Console.CancelKeyPress -= handler;
        }

        task?.StopTask();

        AnsiConsole.MarkupLine(
            $"[blue]Translation completed: {Markup.Escape(outputPath.Basename)}[/]"
        );
        return Result.Ok(outputPath);
    }

    private static async Task RunProcessAsync(
        string fileName,
        IList<string> arguments,
        CancellationToken cancellationToken = default
    )
    {
        var channel = Channel.CreateUnbounded<string>();
        var writer = channel.Writer;

        var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = string.Join(" ", arguments),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        process.Start();

        var stdoutReaderTask = Task.Run(
            async () =>
            {
                while (await process.StandardOutput.ReadLineAsync(cancellationToken) is { } line)
                    await writer.WriteAsync(line, cancellationToken);
            },
            cancellationToken
        );

        var stderrReaderTask = Task.Run(
            async () =>
            {
                while (await process.StandardError.ReadLineAsync(cancellationToken) is { } line)
                    await writer.WriteAsync(line, cancellationToken);
            },
            cancellationToken
        );

        _ = Task.WhenAll(stdoutReaderTask, stderrReaderTask)
            .ContinueWith(_ => channel.Writer.Complete(), cancellationToken);

        await foreach (var line in channel.Reader.ReadAllAsync(cancellationToken))
        {
            AnsiConsole.WriteLine(line);
        }

        await process.WaitForExitAsync(cancellationToken);
    }

    public Task<Result<IPath>> StartAsyc(
        IPath srtPath,
        IPath outputDirectory,
        ProgressContext? progressContext = null
    )
    {
        throw new NotImplementedException();
    }

    private static ConsoleCancelEventHandler CreateCancelEventHandler(CancellationTokenSource cts)
    {
        return (_, args) =>
        {
            args.Cancel = true;
            cts.Cancel();
            args.Cancel = false;
        };
    }
}
