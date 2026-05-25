using System.CommandLine;
using System.Runtime.InteropServices;
using ASTranscriber.Core.Extensions;
using ASTranscriber.Transcription.Whisper;
using ASTranscriber.Translation;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;
using PathLib;
using Spectre.Console;
using Spectre.Console.Rendering;
using ILogger = Microsoft.Extensions.Logging.ILogger;
using MLogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace ASTranscriber.Cli;

public static class Program
{
    private static readonly ILogger Logger = LoggerFactory
        .Create(builder => builder.SetMinimumLevel(MLogLevel.Debug).AddNLog())
        .CreateLogger("Program");

    public static int Main(string[] args)
    {
        using var termSignalRegistration = PosixSignalRegistration.Create(
            PosixSignal.SIGINT,
            _ => Environment.Exit(0)
        );

        var inputOption = new Option<string>("--input", "-i")
        {
            Description = "Path to directory containing files to transcribe",
            DefaultValueFactory = _ => "inputs/",
        };

        var outputOption = new Option<string>("--output", "-o")
        {
            Description = "Path to output directory for processed files",
            DefaultValueFactory = _ => "outputs/",
        };

        var verboseOption = new Option<bool>("--verbose", "-v")
        {
            Description = "Send debug level logging statements to console",
        };

        var transcriptionOption = new Option<TranscriptionMode>("--transcription-mode", "-tc")
        {
            Description = "Which transcription mode to use",
            DefaultValueFactory = _ => TranscriptionMode.Whisper,
        };

        var translationOption = new Option<TranslationMode>("--translation-mode", "-tr")
        {
            Description = "Which translation mode to use",
            DefaultValueFactory = _ => TranslationMode.Sugoi,
        };

        var whisperModelOption = new Option<string>("--whisper-model")
        {
            Description = "The GGML whisper model to use",
            DefaultValueFactory = _ => "models/large-v3-q5_0.bin",
        };

        var whisperVadOption = new Option<bool>("--whisper-vad", "-w")
        {
            Description = "Use the Silero VAD for whisper",
            DefaultValueFactory = _ => true,
        };

        var whisperVadModelOption = new Option<string>("--whisper-vad-model")
        {
            Description = "The Silero VAD model to use",
            DefaultValueFactory = _ => "models/silero_vad.onnx",
        };

        var sugoiModelOption = new Option<string>("--sugoi-model")
        {
            Description = "The GGUF Sugoi model to use",
            DefaultValueFactory = _ => "models/Sugoi-14B-Ultra-Q2_K.gguf",
        };

        var rootCommand = new RootCommand("Transcription and translation of audio files")
        {
            inputOption,
            outputOption,
            transcriptionOption,
            translationOption,
            whisperModelOption,
            whisperVadOption,
            whisperVadModelOption,
            sugoiModelOption,
            verboseOption,
        };

        rootCommand.SetAction(parseResult =>
        {
            var inputDir = parseResult.GetRequiredValue(inputOption);
            var outputDir = parseResult.GetRequiredValue(outputOption);
            var transcriptionChoice = parseResult.GetRequiredValue(transcriptionOption);
            var translationChoice = parseResult.GetRequiredValue(translationOption);
            var verbose = parseResult.GetRequiredValue(verboseOption);
            var whisperModel = parseResult.GetRequiredValue(whisperModelOption);
            var vadChoice = parseResult.GetRequiredValue(whisperVadOption);
            var sileroModel = parseResult.GetRequiredValue(whisperVadModelOption);
            var sugoiModel = parseResult.GetRequiredValue(sugoiModelOption);

            var factory = new PathFactory();
            var inputDirPath = factory.Create(inputDir);
            var outputDirPath = factory.Create(outputDir);
            var whisperModelPath = factory.Create(whisperModel);
            var sileroModelPath = factory.Create(sileroModel);
            var sugoiModelPath = factory.Create(sugoiModel);

            if (!inputDirPath.IsDir())
                inputDirPath.Mkdir();
            if (!outputDirPath.IsDir())
                outputDirPath.Mkdir();

            ConfigureLogger.ConfigureLogging(verbose);

            Logger.LogDebug($"Input dir: {inputDir}");
            Logger.LogDebug($"Output dir: {outputDir}");

            var transcriber = transcriptionChoice switch
            {
                // TODO: Pass an enum instead of an IPath to WhisperTranscriber constructor
                TranscriptionMode.Whisper => new WhisperTranscriber(
                    whisperModelPath,
                    vadChoice ? sileroModelPath : null
                ),
                _ => throw new ArgumentOutOfRangeException(),
            };
            var translator = translationChoice switch
            {
                TranslationMode.Sugoi => new SugoiTranslator(sugoiModelPath),
                TranslationMode.Whisper => throw new NotImplementedException(),
                _ => throw new ArgumentOutOfRangeException(),
            };

            AnsiConsole
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
                    var entries = inputDirPath.ListDir().OrderBy(x => x.Basename).ToArray();
                    if (entries.Length == 0)
                    {
                        Logger.LogWarning(
                            "No files found in input directory: {InputDir}",
                            inputDir
                        );
                        return;
                    }

                    var primaryTask = ctx.AddTask($"...", maxValue: entries.Length);
                    // ProgressTask primaryTask = null;
                    foreach (var entry in entries)
                    {
                        if (!entry.IsFile())
                            continue;

                        var supportedExtensions = new List<string>
                        {
                            ".wav",
                            ".mp3",
                            ".opus",
                            ".m4a",
                        };
                        if (!supportedExtensions.Contains(entry.Extension))
                        {
                            Logger.LogWarning($"Ignoring unsupported file: {entry.Filename}");
                            continue;
                        }

                        primaryTask?.Description =
                            $"[bold]{Markup.Escape(entry.BasenameWithoutExtensions.Ellipsize(16))}[/]";

                        var jobWorkingDir = (
                            outputDirPath.Resolve() / entry.BasenameWithoutExtensions
                        );

                        var srtResult = transcriber.Start(entry, jobWorkingDir, ctx);
                        if (srtResult.IsFailed)
                        {
                            Logger.LogError(
                                "Failed to transcribe {EntryFilename}: {Join}",
                                entry.Filename,
                                string.Join(", ", srtResult.Errors.Select(e => e.Message))
                            );
                            primaryTask?.Increment(1);
                            continue;
                        }

                        var srt = srtResult.Value.Resolve();
                        srtResult = translator.Start(srt, jobWorkingDir, ctx);
                        // var srt = new PosixPath(
                        //     "/home/erwin/Projects/csharp/transcribe-cs/outputs/W⧸X⧸Y ⧸ 常闇トワ(cover)-fmDHfS0fXm4"
                        // );
                        // var srtResult = translator.Start(srt, jobWorkingDir, ctx);

                        primaryTask?.Increment(1);
                    }
                });

            return 0;
        });

        var parseResult = rootCommand.Parse(args);

        if (parseResult.Errors.Count > 0)
        {
            foreach (var error in parseResult.Errors)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine(error);
                Console.ResetColor();
            }

            rootCommand.Parse("-h").Invoke();
            return 1;
        }

        return parseResult.Invoke();
    }

    private enum TranscriptionMode
    {
        Whisper,
    }

    private enum TranslationMode
    {
        Whisper,
        Sugoi,
    }
}

public sealed class CountColumn : ProgressColumn
{
    public string? Unit { get; set; }

    /// <summary>
    /// Gets or sets the style of the remaining text.
    /// </summary>
    public Style Style { get; set; } = Color.Blue;

    public override IRenderable Render(RenderOptions options, ProgressTask task, TimeSpan deltaTime)
    {
        var current = (long)task.Value;
        var total = (long)task.MaxValue;

        var text = total > 0 ? $"{current}/{total}" : $"{current}/?";

        if (Unit is not null)
            text += $" {Unit}";

        return new Markup($"{text}", Style);
    }
}

public sealed class SquareBracedRemainingTimeColumn : ProgressColumn
{
    /// <inheritdoc/>
    protected override bool NoWrap => true;

    /// <summary>
    /// Gets or sets the style of the remaining time text.
    /// </summary>
    public Style Style { get; set; } = Color.Blue;

    /// <inheritdoc/>
    public override IRenderable Render(RenderOptions options, ProgressTask task, TimeSpan deltaTime)
    {
        var remaining = task.RemainingTime;
        if (remaining == null)
        {
            return new Markup("[[--:--:--]]");
        }

        if (remaining.Value.TotalHours > 99 || task.IsIndeterminate)
        {
            return new Markup("[[**:**:**]]");
        }

        return new Text($"[{remaining.Value:hh\\:mm\\:ss}]", Style);
    }

    /// <inheritdoc/>
    public override int? GetColumnWidth(RenderOptions options)
    {
        return 10;
    }
}
