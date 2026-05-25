using System.ComponentModel;
using ASTranscriber.Core;
using ASTranscriber.Transcription;
using ASTranscriber.Transcription.Whisper;
using ASTranscriber.Translation;
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
    [DefaultValue(TranscriptionMode.Whisper)]
    public TranscriptionMode TranscriptionMode { get; init; } = TranscriptionMode.Whisper;

    [CommandOption("--whisper-model")]
    [DefaultValue("models/ggml-large-v3-q5_0.bin")]
    public string WhisperModelPath { get; init; } = "models/ggml-large-v3-q5_0.bin";

    [CommandOption("--whisper-vad-model")]
    [DefaultValue("models/silero_vad.onnx")]
    public string WhisperVadModelPath { get; init; } = "models/silero_vad.onnx";

    [CommandOption("--whisper-vad-threshold")]
    [DefaultValue(0.005f)]
    public float WhisperVadThreshold { get; init; } = 0.005f;
}

public class RunCommand(ITranscriberFactory transcriberFactory, IAnsiConsole console)
    : Command<RunSettings>
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

        RunTranscriber(data, transcriber, cancellationToken).Wait(cancellationToken);
        return 0;
    }

    private Task RunTranscriber(
        IEnumerable<string> inputs,
        ITranscriber transcriber,
        CancellationToken cancellationToken = default
    )
    {
        console
            .Progress()
            .Columns(
                new TaskDescriptionColumn() { Alignment = Justify.Center },
                new ProgressBarColumn() { CompletedStyle = new Style(Color.Blue) },
                new CountColumn() { Style = Color.Blue },
                new ElapsedTimeColumn() { Style = Style.Plain },
                new SquareBracedRemainingTimeColumn() { Style = Style.Plain }
            )
            .HideCompleted(true)
            .AutoClear(true)
            .Start(ctx =>
            {
                var inputFiles = inputs.ToList();
                var transcriptionTask = ctx.AddTask("...", maxValue: inputFiles.Count);

                foreach (var input in inputFiles)
                {
                    transcriptionTask.Description = $"{Path.GetFileName(input)}";

                    Transcribe(input, transcriber, ctx, cancellationToken);

                    transcriptionTask.Increment(1);
                }
            });

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
        transcriber.ProgressChanged += (_, e) =>
        {
            transcribeProgress.Value = (e.Current / e.Total) * 100;
        };

        var result = transcriber.Transcribe(input, cancellationToken);

        transcribeProgress.StopTask();
        return result;
    }

    private ITranscriberOptions GetTranscriberOptions(RunSettings settings)
    {
        return settings.TranscriptionMode switch
        {
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
}
