using FluentAssertions;
using GuildRelay.Features.Chat;
using Xunit;

namespace GuildRelay.Features.Chat.Tests;

public class ChatDedupTests
{
    [Fact]
    public void FirstOccurrenceIsNotDuplicate()
    {
        var dedup = new ChatDedup(capacity: 256);
        dedup.IsDuplicate("hello world").Should().BeFalse();
    }

    [Fact]
    public void SecondOccurrenceIsDuplicate()
    {
        var dedup = new ChatDedup(capacity: 256);
        dedup.IsDuplicate("hello world");
        dedup.IsDuplicate("hello world").Should().BeTrue();
    }

    [Fact]
    public void DifferentLinesAreNotDuplicates()
    {
        var dedup = new ChatDedup(capacity: 256);
        dedup.IsDuplicate("line one");
        dedup.IsDuplicate("line two").Should().BeFalse();
    }

    [Fact]
    public void OldestEntryIsEvictedWhenCapacityExceeded()
    {
        var dedup = new ChatDedup(capacity: 2);
        dedup.IsDuplicate("a");
        dedup.IsDuplicate("b");
        dedup.IsDuplicate("c"); // evicts "a"

        // Check "b" BEFORE "a": IsDuplicate("a") would re-add "a" to the
        // LRU (it's not a dup), which evicts "b" from a capacity-2 set.
        dedup.IsDuplicate("b").Should().BeTrue("'b' is still cached");
        dedup.IsDuplicate("a").Should().BeFalse("'a' was evicted");
    }

    [Fact]
    public void ClearResetsAllEntries()
    {
        var dedup = new ChatDedup(capacity: 256);
        dedup.IsDuplicate("hello");
        dedup.Clear();
        dedup.IsDuplicate("hello").Should().BeFalse();
    }
}
