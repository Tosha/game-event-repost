using System;

namespace GuildRelay.Core.Security;

/// <summary>
/// Single in-memory holder for the Discord webhook URL. The URL is only
/// accessed via <see cref="BorrowWebhookUrl"/>, and the store's
/// <see cref="ToString"/> is locked to a safe constant so accidental
/// string interpolation or logger format calls cannot leak the secret.
/// </summary>
public sealed class SecretStore
{
    private string? _webhookUrl;

    public void SetWebhookUrl(string? value) => _webhookUrl = value;

    public bool HasWebhookUrl => !string.IsNullOrWhiteSpace(_webhookUrl);

    public WebhookUrlAccess BorrowWebhookUrl()
    {
        if (string.IsNullOrWhiteSpace(_webhookUrl))
            throw new InvalidOperationException("Webhook URL is not configured.");
        return new WebhookUrlAccess(_webhookUrl);
    }

    public override string ToString() => "SecretStore(***)";

    public readonly struct WebhookUrlAccess : IDisposable
    {
        public WebhookUrlAccess(string value) { Value = value; }
        public string Value { get; }
        public void Dispose() { /* no-op; struct present for future hardening */ }
    }
}
