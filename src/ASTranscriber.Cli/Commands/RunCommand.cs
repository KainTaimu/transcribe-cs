using System.ComponentModel;
using ASTranscriber.Core;
using ASTranscriber.Transcription;
using ASTranscriber.Transcription.Dummy;
using ASTranscriber.Transcription.Whisper;
using ASTranscriber.Translation;
using ASTranscriber.Translation.Dummy;
using ASTranscriber.Translation.SugoiLlama;
using EchoSharp.Onnx.SileroVad;
using FluentResults;
using Spectre.Console;
using Spectre.Console.Cli;
using Subtitles;

namespace ASTranscriber.Cli.Commands;

public class RunSettings : CommandSettings
{
    [CommandOption("-t|--transcriber")]
    [Description("The transcriber to use. (Whisper)")]
    [DefaultValue(TranscriptionMode.Whisper)]
    public TranscriptionMode TranscriptionMode { get; init; } = TranscriptionMode.Whisper;

    [CommandOption("--whisper-model")]
    [Description("The path to the Whisper model to use.")]
    [DefaultValue("models/ggml-large-v3-q5_0.bin")]
    public string WhisperModelPath { get; init; } = "models/ggml-large-v3-q5_0.bin";

    [CommandOption("--whisper-vad-model")]
    [Description("The path to the Silero VAD model to use.")]
    [DefaultValue("models/silero_vad.onnx")]
    public string WhisperVadModelPath { get; init; } = "models/silero_vad.onnx";

    [CommandOption("--whisper-vad-threshold")]
    [DefaultValue(0.005f)]
    public float WhisperVadThreshold { get; init; } = 0.005f;

    [CommandOption("-s|--translate")]
    [Description("The translator to use. (Sugoi,SugoiLlama)")]
    [DefaultValue(TranslationMode.Sugoi)]
    public TranslationMode TranslationMode { get; init; } = TranslationMode.Sugoi;

    [CommandOption("--sugoi-model")]
    [Description("The path to the Sugoi model to use.")]
    [DefaultValue("models/Sugoi-14B-Ultra-Q2_K.gguf")]
    public string SugoiModelPath { get; init; } = "models/Sugoi-14B-Ultra-Q2_K.gguf";
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

        RunTranscriber(data, transcriber, translator, cancellationToken).Wait(cancellationToken);

        console.MarkupLine("[blue][bold]Done.[/][/]");
        return 0;
    }

    private Task RunTranscriber(
        IEnumerable<string> inputs,
        ITranscriber transcriber,
        ITranslator translator,
        CancellationToken cancellationToken = default
    )
    {
        console
            .Progress()
            .Columns(
                new TaskDescriptionColumn() { Alignment = Justify.Center },
                new ProgressBarColumn() { CompletedStyle = new Style(Color.Blue) },
                new CountColumn() { Style = Color.Blue },
                new ElapsedTimeColumn() { Style = Style.Plain }
            )
            .HideCompleted(true)
            .AutoClear(true)
            .Start(ctx =>
            {
                var inputFiles = inputs.ToList();
                // ctx = null;
                var transcriptionTask = ctx?.AddTask("...", maxValue: inputFiles.Count);

                foreach (var input in inputFiles)
                {
                    transcriptionTask?.Description = $"{Path.GetFileName(input)}";

                    var srtResult = Transcribe(input, transcriber, ctx, cancellationToken);
                    if (srtResult.IsFailed)
                    {
                        console.WriteLine($"Failed to transcribe {input}: {srtResult}", Color.Red);
                        transcriptionTask?.Increment(1);
                        continue;
                    }

                    var translateResult = Translate(
                        srtResult.Value,
                        translator,
                        ctx,
                        cancellationToken
                    );
                    if (translateResult.IsFailed)
                    {
                        console.WriteLine(
                            $"Failed to translate {input}: {translateResult}",
                            Color.Red
                        );
                        transcriptionTask?.Increment(1);
                        continue;
                    }

                    transcriptionTask?.Increment(1);
                }
            });

        return Task.CompletedTask;
    }

    private static Result<ISubtitles> Transcribe(
        string input,
        ITranscriber transcriber,
        ProgressContext? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        var transcribeProgress = progress?.AddTask("Transcribing...", maxValue: 100);
        transcriber.ProgressChanged += (_, e) =>
        {
            transcribeProgress?.IsIndeterminate = e.IsIndeterminate;
            transcribeProgress?.Value = (e.Current / e.Total) * 100;
        };

        var result = transcriber.Transcribe(input, cancellationToken);

        transcribeProgress?.StopTask();
        return result;
    }

    private static Result<ISubtitles> Translate(
        ISubtitles input,
        ITranslator translator,
        ProgressContext? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        var translateTask = progress?.AddTask("Translating...", maxValue: 100);
        translator.ProgressChanged += (_, e) =>
        {
            translateTask?.IsIndeterminate = e.IsIndeterminate;
            translateTask?.Value = (e.Current / e.Total) * 100;
        };

        var result = translator.Translate(input, cancellationToken);

        translateTask?.StopTask();
        return result;
    }

    private ITranscriberOptions GetTranscriberOptions(RunSettings settings)
    {
        return settings.TranscriptionMode switch
        {
            TranscriptionMode.Dummy => new DummyTranscriberOptions(),
            TranscriptionMode.Whisper => GetWhisperTranscriberOptions(settings),
            _ => throw new NotSupportedException(
                $"Transcription mode {settings.TranscriptionMode} is not supported."
            ),
        };
    }

    private WhisperTranscriberOptions GetWhisperTranscriberOptions(RunSettings settings)
    {
        return new WhisperTranscriberOptions(
            settings.WhisperModelPath,
            new SileroVadOptions(settings.WhisperVadModelPath)
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
            TranslationMode.Sugoi => new SugoiTranslatorOptions(settings.SugoiModelPath),
            TranslationMode.SugoiLlama => new SugoiLlamaTranslatorOptions(
                "llama-server",
                settings.SugoiModelPath
            ),
            _ => throw new NotSupportedException(
                $"Translation mode {settings.TranslationMode} is not supported."
            ),
        };
    }
}
