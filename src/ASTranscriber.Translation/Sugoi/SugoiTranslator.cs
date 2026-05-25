using System.Diagnostics;
using System.Threading.Channels;
using FluentResults;
using Spectre.Console;
using Subtitles;

namespace ASTranscriber.Translation;

public class SugoiTranslator(string sugoiModelPath) : ITranslator
{
    private string SugoiModelPath { get; } = sugoiModelPath;

    public Result<ISubtitles> Translate(ISubtitles subtitles)
    {
        if (Environment.OSVersion.Platform != PlatformID.Unix)
            throw new PlatformNotSupportedException(
                "SugoiTranslator is only supported on Unix-based systems."
            );
        if (subtitles is not Srt)
            throw new ArgumentException("Only SRT subtitles are supported.");

        var srtPath = $"orig.{Path.GetTempFileName()}.srt";
        subtitles.WriteFile(srtPath);

        var outputPath = $"translated.{Path.GetTempFileName()}.srt";

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
                    srtPath,
                    "--output",
                    outputPath,
                    "--model",
                    SugoiModelPath,
                ],
                cts.Token
            );

            processTask.Wait(cts.Token);
        }
        catch (OperationCanceledException)
        {
            Console.CancelKeyPress -= handler;
            return Result.Fail("Translation cancelled");
        }
        finally
        {
            Console.CancelKeyPress -= handler;
        }

        var outSrt = new Srt().Read(outputPath);
        return Result.Ok(outSrt);
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
