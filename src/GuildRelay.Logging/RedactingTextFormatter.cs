using System.IO;
using System.Text.RegularExpressions;
using Serilog.Events;
using Serilog.Formatting;

namespace GuildRelay.Logging;

/// <summary>
/// Wraps an inner <see cref="ITextFormatter"/>, rendering through it and
/// then scrubbing any Discord webhook URLs from the output before writing.
/// This is the only redaction layer — it operates on the fully-rendered
/// string so nothing upstream needs to be aware of secrets.
/// </summary>
public sealed class RedactingTextFormatter : ITextFormatter
{
    private static readonly Regex WebhookPattern = new(
        @"https://discord(?:app)?\.com/api/webhooks/[^\s""']+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly ITextFormatter _inner;

    public RedactingTextFormatter(ITextFormatter inner) { _inner = inner; }

    public void Format(LogEvent logEvent, TextWriter output)
    {
        using var captured = new StringWriter();
        _inner.Format(logEvent, captured);
        var redacted = WebhookPattern.Replace(captured.ToString(), "https://discord.com/api/webhooks/***");
        output.Write(redacted);
    }
}
