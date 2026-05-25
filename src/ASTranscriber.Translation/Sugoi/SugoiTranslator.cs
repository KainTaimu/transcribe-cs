using System.Diagnostics;
using System.Threading.Channels;
using ASTranscriber.Core;
using FluentResults;
using Spectre.Console;
using Subtitles;

namespace ASTranscriber.Translation;

public class SugoiTranslator(string sugoiModelPath) : ITranslator
{
    private string SugoiModelPath { get; } = sugoiModelPath;

    public event EventHandler<ProgressEventHandler>? ProgressChanged;

    public Result<ISubtitles> Translate(
        ISubtitles subtitles,
        CancellationToken cancellationToken = default
    )
    {
        if (Environment.OSVersion.Platform != PlatformID.Unix)
            throw new PlatformNotSupportedException(
                "SugoiTranslator is only supported on Unix-based systems."
            );
        if (subtitles is not Srt)
            throw new ArgumentException("Only SRT subtitles are supported.");

        var srtPath = $"{Path.GetTempFileName()}_orig.srt";
        subtitles.WriteFile(srtPath);

        var outputPath = $"{Path.GetTempFileName()}_translated.srt";

        var processTask = RunProcessAsync(
            "python3",
            [
                "-u",
                "script/translate.py",
                "--input",
                srtPath,
                "--output",
                outputPath,
                "--model",
                SugoiModelPath,
            ],
            cancellationToken
        );

        ProgressChanged?.Invoke(this, new ProgressEventHandler(-1, -1));

        processTask.Wait(cancellationToken);

        var outSrt = new Srt();
        outSrt.Read(outputPath);
        return Result.Ok<ISubtitles>(outSrt);
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
}
