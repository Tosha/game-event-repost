using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GuildRelay.Core.Events;
using GuildRelay.Core.Publishing;
using GuildRelay.Core.Security;

namespace GuildRelay.Publisher;

public sealed class DiscordPublisher : IDiscordPublisher
{
    private readonly HttpClient _http;
    private readonly SecretStore _secrets;
    private readonly TemplateEngine _templates;
    private readonly IReadOnlyDictionary<string, string> _templateByFeatureId;
    private readonly int _maxRetries;
    private readonly TimeSpan _initialBackoff;

    public DiscordPublisher(
        HttpClient http,
        SecretStore secrets,
        TemplateEngine templates,
        IReadOnlyDictionary<string, string> templateByFeatureId,
        int maxRetries = 4,
        TimeSpan? initialBackoff = null)
    {
        _http = http;
        _secrets = secrets;
        _templates = templates;
        _templateByFeatureId = templateByFeatureId;
        _maxRetries = maxRetries;
        _initialBackoff = initialBackoff ?? TimeSpan.FromSeconds(1);
    }

    public async ValueTask PublishAsync(DetectionEvent evt, CancellationToken ct)
    {
        if (!_secrets.HasWebhookUrl) return;

        var template = _templateByFeatureId.TryGetValue(evt.FeatureId, out var t)
            ? t
            : "{player} — {rule_label}";
        var body = _templates.Render(template, evt);

        for (var attempt = 0; attempt <= _maxRetries; attempt++)
        {
            using var request = BuildRequest(evt, body);
            HttpResponseMessage? response = null;
            try
            {
                response = await _http.SendAsync(request, ct).ConfigureAwait(false);

                if ((int)response.StatusCode >= 200 && (int)response.StatusCode < 300)
                    return; // success

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    var delay = response.Headers.RetryAfter?.Delta ?? _initialBackoff;
                    await Task.Delay(delay, ct).ConfigureAwait(false);
                    continue;
                }

                if ((int)response.StatusCode >= 500)
                {
                    if (attempt == _maxRetries) return; // exhausted
                    await Task.Delay(BackoffFor(attempt), ct).ConfigureAwait(false);
                    continue;
                }

                return; // 4xx other than 429: do not retry
            }
            catch (HttpRequestException) when (attempt < _maxRetries)
            {
                await Task.Delay(BackoffFor(attempt), ct).ConfigureAwait(false);
            }
            finally
            {
                response?.Dispose();
            }
        }
    }

    private HttpRequestMessage BuildRequest(DetectionEvent evt, string body)
    {
        using var access = _secrets.BorrowWebhookUrl();
        var request = new HttpRequestMessage(HttpMethod.Post, access.Value);

        if (evt.ImageAttachment is null)
        {
            var payload = JsonSerializer.Serialize(new { content = body });
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        }
        else
        {
            var multipart = new MultipartFormDataContent("boundary-" + Guid.NewGuid().ToString("N"));
            var payloadJson = JsonSerializer.Serialize(new { content = body });
            multipart.Add(new StringContent(payloadJson, Encoding.UTF8, "application/json"), "payload_json");
            var file = new ByteArrayContent(evt.ImageAttachment);
            file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            multipart.Add(file, "files[0]", "attachment.png");
            request.Content = multipart;
        }

        return request;
    }

    private TimeSpan BackoffFor(int attempt)
    {
        var ms = _initialBackoff.TotalMilliseconds * Math.Pow(2, attempt);
        return TimeSpan.FromMilliseconds(ms);
    }
}
