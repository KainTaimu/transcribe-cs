using System.Diagnostics;
using System.Threading.Channels;
using ASTranscriber.Core;
using FluentResults;
using Spectre.Console;
using Subtitles;

namespace ASTranscriber.Transcription.WhisperCli;

public class WhisperCliTranscriber(WhisperCliTranscriberOptions options) : ITranscriber
{
    public event EventHandler<ProgressEventHandler>? ProgressChanged;

    public Result<ISubtitles> Transcribe(
        string fileName,
        CancellationToken cancellationToken = default
    )
    {
        ProgressChanged?.Invoke(this, new ProgressEventHandler(0, 100, "Transcribing"));
        var outputPath = Path.Join(Path.GetTempPath(), Path.GetFileNameWithoutExtension(fileName));

        var args = new List<string>();
        args.AddRange(
            "--model",
            options.WhisperModelPath,
            "--output-srt",
            "--max-context",
            "0",
            "--output-file",
            $"\"{outputPath}\"",
            "--print-progress",
            "--no-prints",
            "--language",
            options.SpokenLanguage
        );
        if (options.VadOptions is not null)
        {
            var vadOptions = (WhisperCliVadOptions)options.VadOptions;
            args.AddRange([
                "--vad",
                "--vad-model",
                vadOptions.VadModelPath,
                "--vad-threshold",
                vadOptions.VadThreshold.ToString(),
            ]);
        }
        args.AddRange(["-f", $"\"{fileName}\""]);

        var cliPath = Environment.GetEnvironmentVariable("WHISPERCLI") ?? "whisper-cli";

        var ct = new CancellationTokenSource();

        var processTask = RunProcessAsync(cliPath, args, ct.Token);
        ProgressChanged?.Invoke(this, new ProgressEventHandler(-1, -1, "Translating"));
        processTask.Wait(ct.Token);
        if (processTask.Result.IsFailed)
            return processTask.Result;

        var outSrt = new Srt();
        outSrt.Read(outputPath + ".srt");
        return Result.Ok<ISubtitles>(outSrt);
    }

    private async Task<Result> RunProcessAsync(
        string fileName,
        IList<string> arguments,
        CancellationToken cancellationToken = default
    )
    {
        // probably incorrect usage
        string? failReason = null;
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
                {
                    if (line.StartsWith("error:"))
                    {
                        process.Kill();
                        failReason = line;
                        return;
                    }
                    await writer.WriteAsync(line, cancellationToken);
                }
            },
            cancellationToken
        );

        var stderrReaderTask = Task.Run(
            async () =>
            {
                while (await process.StandardError.ReadLineAsync(cancellationToken) is { } line)
                {
                    if (line.StartsWith("whisper_lang_id: unknown language"))
                    {
                        process.Kill();
                        failReason = "unknown language";
                        return;
                    }
                    if (line.StartsWith("error:"))
                    {
                        process.Kill();
                        failReason = line;
                        return;
                    }
                    var percent = line[45..^1];
                    ProgressChanged?.Invoke(
                        this,
                        new ProgressEventHandler(int.Parse(percent), 100, "Transcribing", false)
                    );
                }
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

        if (process.ExitCode == 0)
            return Result.Ok();

        var result = Result.Fail("whisper-cpp failed");
        result.WithError($"exit code {process.ExitCode}");
        result.WithError(failReason);

        return result;
    }
}
