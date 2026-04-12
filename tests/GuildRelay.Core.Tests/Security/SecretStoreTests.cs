using System;
using FluentAssertions;
using GuildRelay.Core.Security;
using Xunit;

namespace GuildRelay.Core.Tests.Security;

public class SecretStoreTests
{
    [Fact]
    public void ReadReturnsStoredValue()
    {
        var store = new SecretStore();
        store.SetWebhookUrl("https://discord.com/api/webhooks/123/abc");

        using var access = store.BorrowWebhookUrl();
        access.Value.Should().Be("https://discord.com/api/webhooks/123/abc");
    }

    [Fact]
    public void ToStringDoesNotLeakSecret()
    {
        var store = new SecretStore();
        store.SetWebhookUrl("https://discord.com/api/webhooks/123/abc");

        store.ToString().Should().NotContain("abc");
        store.ToString().Should().NotContain("webhooks/123");
    }

    [Fact]
    public void EmptyStoreThrowsOnBorrow()
    {
        var store = new SecretStore();
        var act = () => store.BorrowWebhookUrl();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void HasWebhookUrlIsFalseWhenEmpty()
    {
        var store = new SecretStore();
        store.HasWebhookUrl.Should().BeFalse();
    }

    [Fact]
    public void HasWebhookUrlIsTrueWhenSet()
    {
        var store = new SecretStore();
        store.SetWebhookUrl("https://discord.com/api/webhooks/1/x");
        store.HasWebhookUrl.Should().BeTrue();
    }

    [Fact]
    public void SetWebhookUrlToNullClearsIt()
    {
        var store = new SecretStore();
        store.SetWebhookUrl("https://discord.com/api/webhooks/1/x");
        store.SetWebhookUrl(null);
        store.HasWebhookUrl.Should().BeFalse();
    }
}
