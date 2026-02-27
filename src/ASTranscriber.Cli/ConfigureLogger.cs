using NLog;
using NLog.Targets;

namespace ASTranscriber.Cli;

public static class ConfigureLogger
{
    public static void ConfigureLogging(bool verbose = false)
    {
        const string layout =
            "[${level:uppercase=true} : ${logger:shortName=true}.${callsite:className=false:methodName=true}] "
            + "${message}${exception:format=tostring}";

        LogManager
            .Setup()
            .LoadConfiguration(c =>
            {
                var consoleTarget = new ColoredConsoleTarget("logconsole")
                {
                    Layout = layout,
                    RowHighlightingRules =
                    {
                        new ConsoleRowHighlightingRule(
                            "level == LogLevel.Trace",
                            ConsoleOutputColor.DarkGray,
                            ConsoleOutputColor.NoChange
                        ),
                        new ConsoleRowHighlightingRule(
                            "level == LogLevel.Debug",
                            ConsoleOutputColor.DarkGray,
                            ConsoleOutputColor.NoChange
                        ),
                        new ConsoleRowHighlightingRule(
                            "level == LogLevel.Info",
                            ConsoleOutputColor.White,
                            ConsoleOutputColor.NoChange
                        ),
                        new ConsoleRowHighlightingRule(
                            "level == LogLevel.Warn",
                            ConsoleOutputColor.Yellow,
                            ConsoleOutputColor.NoChange
                        ),
                        new ConsoleRowHighlightingRule(
                            "level == LogLevel.Error",
                            ConsoleOutputColor.Red,
                            ConsoleOutputColor.NoChange
                        ),
                        new ConsoleRowHighlightingRule(
                            "level == LogLevel.Fatal",
                            ConsoleOutputColor.Red,
                            ConsoleOutputColor.NoChange
                        ),
                    },
                };
                var fileTarget = new FileTarget("logfile")
                {
                    FileName = "logs/app.log",
                    Layout = layout,
                };

                c.Configuration.AddRule(
                    verbose ? LogLevel.Debug : LogLevel.Info,
                    LogLevel.Fatal,
                    consoleTarget
                );
                c.Configuration.AddRule(LogLevel.Debug, LogLevel.Fatal, fileTarget);
            });
    }
}
