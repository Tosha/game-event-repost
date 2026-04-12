using System.IO;
using Serilog;
using Serilog.Formatting.Display;

namespace GuildRelay.Logging;

public static class LoggingSetup
{
    public static ILogger CreateAppLogger(string logsDirectory)
    {
        Directory.CreateDirectory(logsDirectory);
        var path = Path.Combine(logsDirectory, "app-.log");
        var inner = new MessageTemplateTextFormatter(
            "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}");
        var formatter = new RedactingTextFormatter(inner);

        return new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                formatter,
                path,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14)
            .CreateLogger();
    }
}
