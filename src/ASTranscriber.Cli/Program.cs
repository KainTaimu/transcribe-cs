using System.CommandLine;
using System.Runtime.InteropServices;
using ASTranscriber.Transcription.Whisper;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;
using PathLib;
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
            DefaultValueFactory = _ => TranslationMode.Whisper,
        };

        var whisperModelOption = new Option<string>("--whisper-model")
        {
            Description = "The GGML whisper model to use",
            DefaultValueFactory = _ => "models/large-v3-q5_0.bin",
        };

        var rootCommand = new RootCommand("Transcription and translation of audio files")
        {
            inputOption,
            outputOption,
            transcriptionOption,
            translationOption,
            whisperModelOption,
            verboseOption,
        };

        rootCommand.SetAction(parseResult =>
        {
            var inputDir = parseResult.GetRequiredValue(inputOption);
            var outputDir = parseResult.GetRequiredValue(outputOption);
            var verbose = parseResult.GetRequiredValue(verboseOption);
            var whisperModel = parseResult.GetRequiredValue(whisperModelOption);

            var factory = new PathFactory();
            var inputDirPath = factory.Create(inputDir);
            var outputDirPath = factory.Create(outputDir);

            if (!inputDirPath.IsDir())
                inputDirPath.Mkdir();
            if (!outputDirPath.IsDir())
                outputDirPath.Mkdir();

            ConfigureLogger.ConfigureLogging(verbose);

            Logger.LogDebug($"Input dir: {inputDir}");
            Logger.LogDebug($"Output dir: {outputDir}");

            var transcriber = new WhisperTranscriber();
            var modelPath = factory.Create(whisperModel);

            // Process all audio files in the input directory
            foreach (var entry in inputDirPath.ListDir().OrderBy(x => x.Basename))
            {
                if (!entry.IsFile())
                    continue;

                var supportedExtensions = new List<string> { ".wav", ".mp3", ".opus", ".m4a" };
                if (!supportedExtensions.Contains(entry.Extension))
                {
                    Logger.LogWarning($"Ignoring unsupported file: {entry.Filename}");
                    continue;
                }

                var result = transcriber.Start(entry, outputDirPath, modelPath);
                if (result.IsFailed)
                {
                    Logger.LogError(
                        $"Failed to transcribe {entry.Filename}: {string.Join(", ", result.Errors.Select(e => e.Message))}"
                    );
                    return 1;
                }
            }

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

    public enum TranscriptionMode
    {
        Whisper,
    }

    public enum TranslationMode
    {
        Whisper,
        Sugoi,
    }
}
