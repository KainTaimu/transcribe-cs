using Spectre.Console;
using Spectre.Console.Rendering;

namespace ASTranscriber.Cli;

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
