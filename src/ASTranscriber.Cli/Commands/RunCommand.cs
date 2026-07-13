using System.ComponentModel;
using ASTranscriber.Core;
using ASTranscriber.Core.Extensions;
using ASTranscriber.Transcription;
using ASTranscriber.Transcription.Dummy;
using ASTranscriber.Transcription.Whisper;
using ASTranscriber.Transcription.WhisperCli;
using ASTranscriber.Translation;
using ASTranscriber.Translation.Dummy;
using ASTranscriber.Translation.LlamaServer;
using EchoSharp.Onnx.SileroVad;
using FluentResults;
using Spectre.Console;
using Spectre.Console.Cli;
using Subtitles;

namespace ASTranscriber.Cli.Commands;

public class RunSettings : CommandSettings
{
    [CommandOption("-t|--transcriber")]
    [Description("The transcriber to use.")]
    [DefaultValue(TranscriptionMode.WhisperCli)]
    public TranscriptionMode TranscriptionMode { get; init; } = TranscriptionMode.WhisperCli;

    [CommandOption("-s|--translate")]
    [Description("The translator to use. (LlamaServer, Sugoi)")]
    [DefaultValue(TranslationMode.LlamaServer)]
    public TranslationMode TranslationMode { get; init; } = TranslationMode.LlamaServer;

    [CommandOption("-l|--language")]
    [Description("The language of the original files.")]
    [DefaultValue("auto")]
    public string Language { get; init; } = "auto";

    [CommandOption("-m|--whisper-model")]
    [Description("The path to the Whisper model to use.")]
    [DefaultValue("models/ggml-large-v3-q5_0.bin")]
    public string WhisperModelPath { get; init; } = "models/ggml-large-v3-q5_0.bin";

    [CommandOption("--no-whisper-vad")]
    [Description("Should not use Silero VAD for whisper transcriptions?")]
    [DefaultValue(false)]
    public bool DontUseWhisperVad { get; init; } = false;

    [CommandOption("--whisper-vad-model")]
    [Description("The path to the Silero VAD model to use.")]
    [DefaultValue("models/ggml-silero-v6.2.0.bin")]
    public string WhisperVadModelPath { get; init; } = "models/ggml-silero-v6.2.0.bin";

    [CommandOption("--whisper-vad-threshold")]
    [DefaultValue(0.005f)]
    public float WhisperVadThreshold { get; init; } = 0.005f;

    [CommandOption("--llama-model")]
    [Description("The path to the Llama server model to use.")]
    [DefaultValue("models/HY-MT2-7B-Q8_0.gguf")]
    public string LlamaServerModelPath { get; init; } = "models/HY-MT2-7B-Q8_0.gguf";
}

public class RunCommand(
    ITranscriberFactory transcriberFactory,
    ITranslatorFactory translatorFactory,
    IAnsiConsole console
) : Command<RunSettings>
{
    protected override int Execute(
        CommandContext context,
        RunSettings settings,
        CancellationToken cancellationToken
    )
    {
        if (context.Data is not List<string> data)
            throw new InvalidOperationException("Expected data to be of type List<string>.");

        var transcriberOptions = GetTranscriberOptions(settings);
        var transcriber = transcriberFactory.Create(transcriberOptions);

        var translatorOptions = GetTranslatorOptions(settings);
        var translator = translatorFactory.Create(translatorOptions);

        console
            .Progress()
            .Columns(
                new TaskDescriptionColumn() { Alignment = Justify.Center },
                new ProgressBarColumn() { CompletedStyle = Color.FromHex("#c6a0f6") },
                new CountColumn() { Style = Color.FromHex("c6a0f6") },
                new ElapsedTimeColumn() { Style = Style.Plain }
            )
            .HideCompleted(true)
            .AutoClear(true)
            .AutoRefresh(true)
            .StartAsync(async ctx =>
            {
                try
                {
                    await RunTranscriber(
                            data,
                            "outputs/",
                            transcriber,
                            translator,
                            ctx,
                            cancellationToken
                        )
                        .WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException) { }
                catch (AggregateException ex)
                {
                    if (ex.GetBaseException().Message == "A task was canceled.")
                        return;
                    throw;
                }
                catch (Exception ex)
                {
                    AnsiConsole.WriteLine($"Thrown exception: {ex}");
                }
            });

        console.MarkupLine("[blue][bold]Done.[/][/]");
        return 0;
    }

    private Task RunTranscriber(
        IEnumerable<string> inputs,
        string outputDirectory,
        ITranscriber transcriber,
        ITranslator translator,
        ProgressContext ctx,
        CancellationToken cancellationToken = default
    )
    {
        var inputFiles = inputs.ToList();
        var transcriptionTask = ctx.AddTask("...", maxValue: inputFiles.Count);

        foreach (var input in inputFiles)
        {
            var filename = Path.GetFileName(input);
            var filenameNoExt = Path.GetFileNameWithoutExtension(input);
            transcriptionTask?.Description = Markup.Escape(filename);

            var srtResult = Transcribe(input, transcriber, ctx, cancellationToken);
            if (srtResult.IsFailed)
            {
                console.WriteLine(srtResult.UnwindErrors("failed to transcribe"), Color.Red);
                transcriptionTask?.Increment(1);
                continue;
            }

            var translateResult = Translate(srtResult.Value, translator, ctx, cancellationToken);
            if (translateResult.IsFailed)
            {
                console.WriteLine(translateResult.UnwindErrors("failed to translate"), Color.Red);
                transcriptionTask?.Increment(1);
                continue;
            }
            translateResult.Value.WriteFile(
                Path.Join(
                    outputDirectory,
                    $"{Path.ChangeExtension(filenameNoExt, ".srt")
                        }"
                )
            );

            transcriptionTask?.Increment(1);
        }

        return Task.CompletedTask;
    }

    private static Result<ISubtitles> Transcribe(
        string input,
        ITranscriber transcriber,
        ProgressContext progress,
        CancellationToken cancellationToken = default
    )
    {
        var transcribeProgress = progress.AddTask("Transcribing...", maxValue: 100);
        HandleTranscriptionEvent(transcriber, transcribeProgress, progress);

        var result = transcriber.Transcribe(input, cancellationToken);

        transcribeProgress.StopTask();
        return result;
    }

    private static void HandleTranscriptionEvent(
        ITranscriber transcriber,
        ProgressTask? transcribeProgress,
        ProgressContext ctx
    )
    {
        transcriber.ProgressChanged += (_, e) =>
        {
            transcribeProgress?.Description = e.Description;
            transcribeProgress?.IsIndeterminate = e.IsIndeterminate;
            transcribeProgress?.Value = (e.Current / e.Total) * 100;
            if (e.Message != null)
                AnsiConsole.MarkupLine(Markup.Escape(e.Message));
            ctx.Refresh();
        };
        transcriber.MessageReceived += (_, e) =>
        {
            AnsiConsole.MarkupLine(Markup.Escape(e.Message));
            ctx.Refresh();
        };
    }

    private static Result<ISubtitles> Translate(
        ISubtitles input,
        ITranslator translator,
        ProgressContext progress,
        CancellationToken cancellationToken = default
    )
    {
        var translateTask = progress.AddTask("Translating...", maxValue: 100);
        HandleTranslatorEvents(translator, translateTask, progress);

        var result = translator.Translate(input, cancellationToken);

        translateTask.StopTask();
        return result;
    }

    private static void HandleTranslatorEvents(
        ITranslator translator,
        ProgressTask? translateTask,
        ProgressContext ctx
    )
    {
        translator.ProgressChanged += (_, e) =>
        {
            translateTask?.Description = e.Description;
            translateTask?.IsIndeterminate = e.IsIndeterminate;
            translateTask?.Value = (e.Current / e.Total) * 100;
            if (e.Message != null)
                AnsiConsole.MarkupLine(e.Message);
            ctx.Refresh();
        };
        translator.MessageReceived += (_, e) =>
        {
            AnsiConsole.MarkupLine(e.Message);
            ctx.Refresh();
        };
    }

    private ITranscriberOptions GetTranscriberOptions(RunSettings settings)
    {
        return settings.TranscriptionMode switch
        {
            TranscriptionMode.Dummy => new DummyTranscriberOptions(),
            TranscriptionMode.Whisper => GetWhisperTranscriberOptions(settings),
            TranscriptionMode.WhisperCli => new WhisperCliTranscriberOptions(
                settings.WhisperModelPath,
                settings.Language,
                settings.DontUseWhisperVad
                    ? null
                    : new WhisperCliVadOptions(
                        settings.WhisperVadModelPath,
                        settings.WhisperVadThreshold
                    )
            ),
            _ => throw new NotSupportedException(
                $"Transcription mode {settings.TranscriptionMode} is not supported."
            ),
        };
    }

    private WhisperTranscriberOptions GetWhisperTranscriberOptions(RunSettings settings)
    {
        return new WhisperTranscriberOptions(
            settings.WhisperModelPath,
            settings.DontUseWhisperVad
                ? null
                : new SileroVadOptions(settings.WhisperVadModelPath)
                {
                    Threshold = settings.WhisperVadThreshold,
                }
        );
    }

    private ITranslatorOptions GetTranslatorOptions(RunSettings settings)
    {
        return settings.TranslationMode switch
        {
            TranslationMode.Dummy => new DummyTranslatorOptions(),
            TranslationMode.Sugoi => new SugoiTranslatorOptions(settings.LlamaServerModelPath),
            TranslationMode.LlamaServer => new LlamaServerTranslatorOptions(
                "llama-server",
                settings.LlamaServerModelPath
            ),
            _ => throw new NotSupportedException(
                $"Translation mode {settings.TranslationMode} is not supported."
            ),
        };
    }
}
