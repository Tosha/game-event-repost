using System.Collections.Generic;
using FluentAssertions;
using GuildRelay.Features.Chat;
using Xunit;

namespace GuildRelay.Features.Chat.Tests;

public class ChatMessageAssemblerTests
{
    private const double Threshold = 0.5;

    private static OcrLineInput L(string normalized, float confidence = 0.9f)
        => new(normalized, normalized, confidence);

    [Fact]
    public void EmptyInputYieldsEmptyResult()
    {
        var r = ChatMessageAssembler.Assemble(new List<OcrLineInput>(), Threshold);
        r.Completed.Should().BeEmpty();
        r.Trailing.Should().BeNull();
    }

    [Fact]
    public void SingleHeaderOnlyIsTrailing()
    {
        var r = ChatMessageAssembler.Assemble(new[] { L("[Nave] [Tosh] hello") }, Threshold);
        r.Completed.Should().BeEmpty();
        r.Trailing.Should().NotBeNull();
        r.Trailing!.Channel.Should().Be("Nave");
        r.Trailing.PlayerName.Should().Be("Tosh");
        r.Trailing.Body.Should().Be("hello");
    }

    [Fact]
    public void TwoHeadersYieldOneCompletedOneTrailing()
    {
        var r = ChatMessageAssembler.Assemble(new[]
        {
            L("[Nave] [Tosh] first"),
            L("[Nave] [Tosh] second"),
        }, Threshold);

        r.Completed.Should().HaveCount(1);
        r.Completed[0].Body.Should().Be("first");
        r.Trailing!.Body.Should().Be("second");
    }

    [Fact]
    public void WrappedMessageJoinedWithSpace()
    {
        var r = ChatMessageAssembler.Assemble(new[]
        {
            L("[21:02:15][Game] A large band of Profiteers has been"),
            L("seen pillaging the Plains of Meduli!"),
            L("[Nave] [Next] next message"),
        }, Threshold);

        r.Completed.Should().HaveCount(1);
        var c = r.Completed[0];
        c.Channel.Should().Be("Game");
        c.Timestamp.Should().Be("21:02:15");
        c.Body.Should().Be("A large band of Profiteers has been seen pillaging the Plains of Meduli!");
        c.StartRow.Should().Be(0);
        c.EndRow.Should().Be(1);

        r.Trailing!.Channel.Should().Be("Nave");
    }

    [Fact]
    public void ContinuationBeforeFirstHeaderIsIgnored()
    {
        var r = ChatMessageAssembler.Assemble(new[]
        {
            L("orphan text with no header above"),
            L("[Nave] [Tosh] real message"),
        }, Threshold);

        r.Completed.Should().BeEmpty();
        r.Trailing!.Body.Should().Be("real message");
        r.Trailing.StartRow.Should().Be(1);
    }

    [Fact]
    public void HeaderBelowThresholdDropsWholeMessage()
    {
        var r = ChatMessageAssembler.Assemble(new[]
        {
            L("[Nave] [Tosh] ok", confidence: 0.9f),
            L("[Game] something", confidence: 0.2f),          // bad header
            L("continuation of bad header", confidence: 0.9f), // orphan now
            L("[Nave] [Tosh] third", confidence: 0.9f),
        }, Threshold);

        r.Completed.Should().HaveCount(1);
        r.Completed[0].Body.Should().Be("ok");
        r.Trailing!.Body.Should().Be("third");
    }

    [Fact]
    public void ContinuationBelowThresholdIsSkippedMessageStillEmitted()
    {
        var r = ChatMessageAssembler.Assemble(new[]
        {
            L("[Game] A large band of Profiteers has been", confidence: 0.9f),
            L("noise", confidence: 0.1f),                       // skipped
            L("pillaging the Plains of Meduli!", confidence: 0.9f),
            L("[Nave] [Tosh] done", confidence: 0.9f),
        }, Threshold);

        r.Completed.Should().HaveCount(1);
        r.Completed[0].Body.Should().Be(
            "A large band of Profiteers has been pillaging the Plains of Meduli!");
        r.Completed[0].EndRow.Should().Be(2);
        r.Trailing!.Body.Should().Be("done");
    }

    [Fact]
    public void OriginalTextPreservesCase()
    {
        var r = ChatMessageAssembler.Assemble(new[]
        {
            new OcrLineInput("[nave] [tosh] lowered", "[Nave] [Tosh] Lowered", 0.9f),
            new OcrLineInput("[nave] [tosh] next", "[Nave] [Tosh] Next", 0.9f),
        }, Threshold);

        r.Completed.Should().HaveCount(1);
        r.Completed[0].OriginalText.Should().Be("[Nave] [Tosh] Lowered");
    }
}
