using System.Collections.Generic;
using FluentAssertions;
using GuildRelay.Features.Chat;
using Xunit;

namespace GuildRelay.Features.Chat.Tests;

public class DeferredTrailingTests
{
    private static AssembledMessage Msg(string body, string? channel = "Nave",
        string? player = "Tosh", string? ts = null, int start = 0, int end = 0)
        => new(ts, channel, player, body, body, start, end);

    [Fact]
    public void NoPreviousTrailingEmitsCompletedBuffersTrailing()
    {
        var current = new AssemblyResult(new[] { Msg("c0"), Msg("c1") }, Msg("tcur"));
        var (toEmit, buffer) = DeferredTrailing.Resolve(previousTrailing: null, current);
        toEmit.Should().HaveCount(2);
        toEmit[0].Body.Should().Be("c0");
        toEmit[1].Body.Should().Be("c1");
        buffer!.Body.Should().Be("tcur");
    }

    [Fact]
    public void PreviousNotFoundEmitsPreviousThenCompleted()
    {
        var prev = Msg("old trailing", channel: "Guild", player: "A");
        var current = new AssemblyResult(
            new[] { Msg("new1", channel: "Nave", player: "B") },
            Msg("new2", channel: "Nave", player: "B"));
        var (toEmit, buffer) = DeferredTrailing.Resolve(prev, current);
        toEmit.Should().HaveCount(2);
        toEmit[0].Body.Should().Be("old trailing");
        toEmit[1].Body.Should().Be("new1");
        buffer!.Body.Should().Be("new2");
    }

    [Fact]
    public void PreviousFoundGrownInCompletedSkipsPrevious()
    {
        var prev = Msg("A large band of Prof", channel: "Game", player: null, ts: "21:02:15");
        var grown = Msg("A large band of Prof pillaging the Plains of Meduli!",
            channel: "Game", player: null, ts: "21:02:15");
        var current = new AssemblyResult(new[] { grown }, Trailing: null);
        var (toEmit, buffer) = DeferredTrailing.Resolve(prev, current);
        toEmit.Should().ContainSingle().Which.Body.Should().Be(grown.Body);
        buffer.Should().BeNull();
    }

    [Fact]
    public void PreviousStillTrailingEmitsPreviousAndBuffersCurrentTrailing()
    {
        // Chat idle for one tick: the same trailing appears again.
        var prev = Msg("hello world", channel: "Nave", player: "Tosh");
        var current = new AssemblyResult(
            new List<AssembledMessage>(),
            Msg("hello world", channel: "Nave", player: "Tosh"));
        var (toEmit, buffer) = DeferredTrailing.Resolve(prev, current);
        toEmit.Should().ContainSingle().Which.Body.Should().Be("hello world");
        buffer!.Body.Should().Be("hello world");
    }

    [Fact]
    public void PreviousScrolledOffEmitsPreviousAndBuffersNewTrailing()
    {
        var prev = Msg("scrolled off", channel: "Nave", player: "Tosh");
        var current = new AssemblyResult(
            new List<AssembledMessage>(),
            Msg("new trailing", channel: "Nave", player: "Tosh"));
        var (toEmit, buffer) = DeferredTrailing.Resolve(prev, current);
        toEmit.Should().ContainSingle().Which.Body.Should().Be("scrolled off");
        buffer!.Body.Should().Be("new trailing");
    }

    [Fact]
    public void EmptyCurrentWithPreviousEmitsPreviousAndClearsBuffer()
    {
        var prev = Msg("final", channel: "Nave", player: "Tosh");
        var current = new AssemblyResult(new List<AssembledMessage>(), Trailing: null);
        var (toEmit, buffer) = DeferredTrailing.Resolve(prev, current);
        toEmit.Should().ContainSingle().Which.Body.Should().Be("final");
        buffer.Should().BeNull();
    }
}
