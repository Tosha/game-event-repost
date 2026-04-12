using System;
using System.Threading.Tasks;
using Serilog;

namespace GuildRelay.App.Exceptions;

public static class GlobalExceptionHandler
{
    public static void Hook(ILogger logger)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                logger.Fatal(ex, "Unhandled AppDomain exception");
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            logger.Error(e.Exception, "Unobserved task exception");
            e.SetObserved();
        };
    }
}
