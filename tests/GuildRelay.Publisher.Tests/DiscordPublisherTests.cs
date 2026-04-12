using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GuildRelay.Core.Events;
using GuildRelay.Core.Security;
using GuildRelay.Publisher;
using Xunit;

namespace GuildRelay.Publisher.Tests;

public class DiscordPublisherTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;
        public List<HttpRequestMessage> Requests { get; } = new();

        public StubHandler(params HttpResponseMessage[] responses)
        {
            _responses = new Queue<HttpResponseMessage>(responses);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            Requests.Add(req);
            return Task.FromResult(_responses.Count > 0
                ? _responses.Dequeue()
                : new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }
    }

    private static DetectionEvent Make(byte[]? image = null) => new(
        FeatureId: "chat", RuleLabel: "Incoming", MatchedContent: "inc north",
        TimestampUtc: DateTimeOffset.UtcNow, PlayerName: "Tosh",
        Extras: new Dictionary<string, string>(), ImageAttachment: image);

    private static SecretStore StoreWithUrl()
    {
        var store = new SecretStore();
        store.SetWebhookUrl("https://discord.com/api/webhooks/1/token");
        return store;
    }

    private static DiscordPublisher CreatePublisher(StubHandler handler,
        int maxRetries = 0, TimeSpan? backoff = null)
    {
        return new DiscordPublisher(
            new HttpClient(handler),
            StoreWithUrl(),
            new TemplateEngine(),
            new Dictionary<string, string> { ["chat"] = "{player}: {matched_text}" },
            maxRetries: maxRetries,
            initialBackoff: backoff ?? TimeSpan.Zero);
    }

    [Fact]
    public async Task TextEventPostsJsonPayload()
    {
        var handler = new StubHandler(new HttpResponseMessage(HttpStatusCode.NoContent));
        var publisher = CreatePublisher(handler);

        await publisher.PublishAsync(Make(), CancellationToken.None);

        handler.Requests.Should().HaveCount(1);
        handler.Requests[0].Content!.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task ImageEventPostsMultipart()
    {
        var handler = new StubHandler(new HttpResponseMessage(HttpStatusCode.NoContent));
        var publisher = CreatePublisher(handler);

        await publisher.PublishAsync(Make(image: new byte[] { 1, 2, 3 }), CancellationToken.None);

        handler.Requests.Should().HaveCount(1);
        handler.Requests[0].Content!.Headers.ContentType!.MediaType.Should().StartWith("multipart/form-data");
    }

    [Fact]
    public async Task ServerErrorIsRetriedUpToMaxRetries()
    {
        var handler = new StubHandler(
            new HttpResponseMessage(HttpStatusCode.InternalServerError),
            new HttpResponseMessage(HttpStatusCode.InternalServerError),
            new HttpResponseMessage(HttpStatusCode.NoContent));
        var publisher = CreatePublisher(handler, maxRetries: 3);

        await publisher.PublishAsync(Make(), CancellationToken.None);

        handler.Requests.Should().HaveCount(3);
    }

    [Fact]
    public async Task NoWebhookUrlConfiguredIsNoOp()
    {
        var handler = new StubHandler(new HttpResponseMessage(HttpStatusCode.NoContent));
        var emptyStore = new SecretStore(); // no URL set
        var publisher = new DiscordPublisher(
            new HttpClient(handler),
            emptyStore,
            new TemplateEngine(),
            new Dictionary<string, string> { ["chat"] = "{player}" },
            maxRetries: 0,
            initialBackoff: TimeSpan.Zero);

        await publisher.PublishAsync(Make(), CancellationToken.None);

        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ClientErrorIsNotRetried()
    {
        var handler = new StubHandler(
            new HttpResponseMessage(HttpStatusCode.BadRequest),
            new HttpResponseMessage(HttpStatusCode.NoContent));
        var publisher = CreatePublisher(handler, maxRetries: 3);

        await publisher.PublishAsync(Make(), CancellationToken.None);

        handler.Requests.Should().HaveCount(1);
    }
}
