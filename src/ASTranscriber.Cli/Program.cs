using ASTranscriber.Cli.Commands;
using ASTranscriber.Transcription;
using ASTranscriber.Translation;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ASTranscriber.Cli;

public static class Program
{
    private static int _cancelKeyPressCount;

    public static async Task<int> Main(string[] args)
    {
        var cts = new CancellationTokenSource();

        Console.CancelKeyPress += (_, eventArgs) =>
        {
            cts.Cancel();
            _cancelKeyPressCount++;
            if (_cancelKeyPressCount >= 3)
            {
                AnsiConsole.MarkupLine("[red]Force quitting.[/]");
                eventArgs.Cancel = false;
            }
            else
            {
                AnsiConsole.MarkupLine("[yellow]Cancellation requested.[/]");
                eventArgs.Cancel = true;
            }
        };

        var services = new ServiceCollection();
        services.AddSingleton<ITranscriberFactory, TranscriberFactory>();
        services.AddSingleton<ITranslatorFactory, TranslatorFactory>();

        var registrar = new TypeRegistrar(services);
        var app = new CommandApp<RunCommand>(registrar);
        app.Configure(config =>
        {
            config.SetApplicationName("as-transcriber");
        });

        return await app.WithData(GetInputs()).RunAsync(args, cts.Token);
    }

    private static List<string> GetInputs()
    {
        if (!Directory.Exists("inputs"))
            Directory.CreateDirectory("inputs");
        var list = Directory.EnumerateFiles("inputs").ToList();
        return list;
    }
}
