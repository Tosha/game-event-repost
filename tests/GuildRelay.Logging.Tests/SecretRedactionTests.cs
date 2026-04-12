using System.IO;
using FluentAssertions;
using GuildRelay.Logging;
using Serilog;
using Serilog.Formatting.Display;
using Xunit;

namespace GuildRelay.Logging.Tests;

public class SecretRedactionTests
{
    [Fact]
    public void WebhookUrlIsRedactedFromRenderedOutput()
    {
        var path = Path.Combine(Path.GetTempPath(), "guildrelay-tests", Path.GetRandomFileName() + ".log");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var inner = new MessageTemplateTextFormatter("{Message:lj}{NewLine}");
        var formatter = new RedactingTextFormatter(inner);

        var log = new LoggerConfiguration()
            .WriteTo.File(formatter, path)
            .CreateLogger();

        log.Information("POSTing to {Url} failed", "https://discord.com/api/webhooks/123/abc");
        log.Dispose();

        var contents = File.ReadAllText(path);
        contents.Should().NotContain("123/abc");
        contents.Should().Contain("https://discord.com/api/webhooks/***");
    }

    [Fact]
    public void NonWebhookMessagesAreUnchanged()
    {
        var path = Path.Combine(Path.GetTempPath(), "guildrelay-tests", Path.GetRandomFileName() + ".log");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var inner = new MessageTemplateTextFormatter("{Message:lj}{NewLine}");
        var formatter = new RedactingTextFormatter(inner);

        var log = new LoggerConfiguration()
            .WriteTo.File(formatter, path)
            .CreateLogger();

        log.Information("Normal message with no secrets");
        log.Dispose();

        var contents = File.ReadAllText(path);
        contents.Should().Contain("Normal message with no secrets");
    }
}
