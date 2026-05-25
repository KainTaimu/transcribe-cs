using System.ComponentModel;
using ASTranscriber.Core;
using ASTranscriber.Transcription;
using ASTranscriber.Transcription.Whisper;
using ASTranscriber.Translation;
using EchoSharp.Onnx.SileroVad;
using Spectre.Console;
using Spectre.Console.Cli;

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

        var transcriber = settings.TranscriptionMode switch
        {
            TranscriptionMode.Whisper => transcriberFactory.Create(
                new WhisperTranscriberOptions(
                    settings.WhisperModelPath,
                    new SileroVadOptions(settings.WhisperVadModelPath)
                    {
                        Threshold = settings.WhisperVadThreshold,
                    }
                )
            ),
            _ => throw new ArgumentOutOfRangeException(),
        };
        RunTranscriber(data, transcriber, cancellationToken).Wait(cancellationToken);
        return 0;
    }

    private static Task RunTranscriber(
        IEnumerable<string> inputs,
        ITranscriber transcriber,
        CancellationToken cancellationToken = default
    )
    {
        foreach (var input in inputs)
        {
            transcriber.Transcribe(input, cancellationToken);
        }

        return Task.CompletedTask;
    }

    private static Task RunTranslator(
        IEnumerable<string> inputs,
        ITranslator translator,
        CancellationToken cancellationToken = default
    )
    {
        return Task.CompletedTask;
    }
}
