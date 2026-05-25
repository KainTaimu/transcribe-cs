using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;
using ASTranscriber.Core;
using FluentResults;
using Meziantou.Framework.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Spectre.Console;
using Subtitles;

namespace ASTranscriber.Translation.SugoiLlama;

public class SugoiLlamaTranslator(
    SugoiLlamaTranslatorOptions options,
    string llamaServerBinaryPath = "llama-server"
) : ITranslator
{
    private static readonly HttpClient _client = new()
    {
        BaseAddress = new Uri("http://127.0.0.1:8080"),
    };

    public event EventHandler<ProgressEventHandler>? ProgressChanged;

    private Process? _llamaServer;

    private const string SUGOI_SYSTEM_PROMPT =
        "You are a translation engine. Translate the following Japanese subtitle lines to natural English. "
        + "Return only the translated text. No preamble, no explanation, no notes.";

    private const string USER_PROMPT_PREAMBLE = (
        "Translate the following informal Japanese to English. Output only the translation. Do not add explanations, "
        + "romanization, or notes. One translation per line\n\n"
    );

    public Result<ISubtitles> Translate(
        ISubtitles subtitles,
        CancellationToken cancellationToken = default
    )
    {
        if (subtitles is not Srt srt)
            throw new ArgumentException("Only SRT subtitles are supported.");

        StartServer();
        WaitForServerAsync(cancellationToken: cancellationToken).Wait(cancellationToken);

        var srtPath = $"{Path.GetTempFileName()}_orig.srt";
        subtitles.WriteFile(srtPath);

        var outSrt = new Srt();
        foreach (var line in srt)
        {
            var translation = SendLine(line.Text, cancellationToken).Result;
            var retries = 1;
            const int maxTries = 3;
            while (!ResponseCleaner.IsTextValid(translation) && retries < maxTries)
            {
                AnsiConsole.Markup(
                    $"[dim]Got invalid line:\n{Markup.Escape(translation)}.\nAttempt "
                        + $"{retries++}/{maxTries}[/]\n"
                );
                translation = SendLine(line.Text, cancellationToken).Result;
            }

            if (retries == maxTries)
            {
                AnsiConsole.Markup(
                    $"[yellow]Got invalid line:\n{Markup.Escape(translation)}.\nSkipping[/]\n"
                );
                continue;
            }

            var translatedLine = new SrtLine(translation, line.LineNumber, line.Start, line.End);
            AnsiConsole.MarkupLine(
                $"[dim][[{line.Start:G}->{line.End:G}]][/] {translatedLine.Text}"
            );
            outSrt.AddLine(translatedLine);

            OnProgressChanged(
                new ProgressEventHandler(translatedLine.LineNumber, srt.LineCount, false)
            );
        }

        return Result.Ok<ISubtitles>(outSrt);
    }

    private string CreatePrompt(string text)
    {
        var s = new StringBuilder();
        s.AppendLine(USER_PROMPT_PREAMBLE);
        s.AppendLine($"Input: {text}\n");
        s.AppendLine("Output: ");
        return s.ToString();
    }

    private async Task<string> SendLine(string line, CancellationToken cancellationToken = default)
    {
        var msg = new LlamaServerChatRequest
        {
            Messages =
            [
                new LlamaServerChatMessage("system", SUGOI_SYSTEM_PROMPT),
                new LlamaServerChatMessage("user", CreatePrompt(line)),
            ],
        };

        var jsonString = JsonConvert.SerializeObject(
            msg,
            Formatting.Indented,
            new JsonSerializerSettings()
            {
                ContractResolver = new DefaultContractResolver()
                {
                    NamingStrategy = new SnakeCaseNamingStrategy(),
                },
            }
        );
        var msgJson = new StringContent(jsonString, Encoding.UTF8, "application/json");

        using var response = await _client.PostAsync(
            "v1/chat/completions",
            msgJson,
            cancellationToken
        );

        var responseString = await response.Content.ReadAsStringAsync(cancellationToken);
        var deserialized = JsonConvert.DeserializeObject<LlamaServerChatResponse>(responseString);

        return deserialized?.Choices[0].Message.Content ?? "...";
    }

    private void StartServer()
    {
        try
        {
            var res = _client.GetAsync("health");
            res.Wait();
            if (res.Result.IsSuccessStatusCode)
                return;
        }
        catch
        {
            // ignored
        }

        var llamaServerProcess = new Process()
        {
            StartInfo = new ProcessStartInfo()
            {
                FileName = llamaServerBinaryPath,
                ArgumentList = { "-m", options.SugoiModelPath, "-lv", "1", "--offline", "--no-ui" },
                UseShellExecute = false,
                CreateNoWindow = true,
            },
            EnableRaisingEvents = true,
        };
        llamaServerProcess.Start();
        _llamaServer = llamaServerProcess;

        Console.CancelKeyPress += (_, _) =>
        {
            TerminateLlamaServer();
        };

        if (OperatingSystem.IsLinux())
        {
            PosixSignalRegistration.Create(PosixSignal.SIGTERM, _ => TerminateLlamaServer());
            PosixSignalRegistration.Create(PosixSignal.SIGINT, _ => TerminateLlamaServer());
        }
        else if (OperatingSystem.IsWindowsVersionAtLeast(5, 1, 2600))
        {
            var job = new JobObject();
            job.SetLimits(
                new JobObjectLimits()
                {
                    Flags =
                        JobObjectLimitFlags.DieOnUnhandledException
                        | JobObjectLimitFlags.KillOnJobClose,
                }
            );
            job.AssignProcess(llamaServerProcess);
        }

        return;

        void TerminateLlamaServer()
        {
            _llamaServer?.Kill(true);
            _llamaServer?.WaitForExit();
        }
    }

    private static async Task WaitForServerAsync(
        int maxAttempts = 120,
        CancellationToken cancellationToken = default
    )
    {
        for (var i = 0; i < maxAttempts; i++)
        {
            await Task.Delay(250, cancellationToken);
            try
            {
                var res = await _client.GetAsync("health", cancellationToken);
                if (res.IsSuccessStatusCode)
                    return;
            }
            catch
            {
                // ignored
            }
        }

        throw new TimeoutException("Server did not become ready in time.");
    }

    private void OnProgressChanged(ProgressEventHandler e)
    {
        ProgressChanged?.Invoke(this, e);
    }

    [SuppressMessage("ReSharper", "UnusedMember.Local")]
    private class LlamaServerChatRequest
    {
        // ReSharper disable once UnusedAutoPropertyAccessor.Local
        public required LlamaServerChatMessage[] Messages { get; init; } = [];

        public float Temperature { get; init; } = 0.3f;

        public int TopK { get; init; } = 40;

        public float TopP { get; init; } = 0.95f;

        public float MinP { get; init; } = 0.05f;

        public float RepeatPenalty { get; init; } = 1.1f;

        public bool Stream { get; init; } = false;
    }

    [SuppressMessage("ReSharper", "UnusedMember.Local")]
    private class LlamaServerChatMessage(string role, string content)
    {
        public string Role = role;

        public string Content = content;
    }

    [SuppressMessage("ReSharper", "ClassNeverInstantiated.Local")]
    [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Local")]
    private class LlamaServerChatResponse
    {
        public required LlamaServerChatResponseChoices[] Choices { get; init; }

        public class LlamaServerChatResponseChoices
        {
            public required LlamaServerChatResponseMessage Message { get; init; }

            public class LlamaServerChatResponseMessage
            {
                public required string Content { get; init; }
            }
        }
    }
}
