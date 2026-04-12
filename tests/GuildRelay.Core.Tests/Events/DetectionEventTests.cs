using System.Collections.Generic;
using FluentAssertions;
using GuildRelay.Core.Events;
using Xunit;

namespace GuildRelay.Core.Tests.Events;

public class DetectionEventTests
{
    [Fact]
    public void TwoEventsWithSameFieldsAreEqual()
    {
        // Share the Extras instance: record value-equality on a reference-typed
        // member uses reference equality, so two records constructed with two
        // different dictionaries would not compare equal even with identical
        // contents. Sharing the dictionary lets stock record semantics verify
        // the property we actually care about: that DetectionEvent is a record.
        var t = new System.DateTimeOffset(2026, 4, 11, 10, 0, 0, System.TimeSpan.Zero);
        var extras = new Dictionary<string, string> { ["source"] = "region" };
        var a = new DetectionEvent("chat", "Incoming", "inc north", t, "Tosh", extras, ImageAttachment: null);
        var b = new DetectionEvent("chat", "Incoming", "inc north", t, "Tosh", extras, ImageAttachment: null);

        a.Should().Be(b);
    }

    [Fact]
    public void EventCopiedViaWithExpressionIsEqual()
    {
        var t = new System.DateTimeOffset(2026, 4, 11, 10, 0, 0, System.TimeSpan.Zero);
        var original = new DetectionEvent("chat", "Incoming", "inc north", t, "Tosh",
            new Dictionary<string, string>(), ImageAttachment: null);

        var copy = original with { };

        copy.Should().Be(original);
    }

    [Fact]
    public void ImageAttachmentDefaultsToNull()
    {
        var evt = new DetectionEvent("audio", "Horse", "whinny", System.DateTimeOffset.UtcNow,
            "Tosh", new Dictionary<string, string>(), ImageAttachment: null);

        evt.ImageAttachment.Should().BeNull();
    }
}
