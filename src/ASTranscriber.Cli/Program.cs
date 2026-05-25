using System.Runtime.InteropServices;
using ASTranscriber.Cli.Commands;
using ASTranscriber.Transcription;
using ASTranscriber.Transcription.Whisper;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Rendering;

namespace ASTranscriber.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var cts = new CancellationTokenSource();

        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cts.Cancel();
            AnsiConsole.MarkupLine("[yellow]Cancellation requested.[/]");
        };

        var services = new ServiceCollection();
        services.AddSingleton<IConcreteTranscriberFactory, WhisperTranscriberFactory>();
        services.AddSingleton<ITranscriberFactory, TranscriberFactory>();

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
