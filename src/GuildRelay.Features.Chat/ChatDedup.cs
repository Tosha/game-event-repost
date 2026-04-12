using System.Collections.Generic;
using System.IO.Hashing;
using System.Text;

namespace GuildRelay.Features.Chat;

/// <summary>
/// LRU set of hashes of normalized chat lines. Returns true if a
/// line was already seen within the last <c>capacity</c> lines.
/// </summary>
public sealed class ChatDedup
{
    private readonly int _capacity;
    private readonly LinkedList<ulong> _order = new();
    private readonly HashSet<ulong> _seen = new();

    public ChatDedup(int capacity) { _capacity = capacity; }

    public bool IsDuplicate(string normalizedLine)
    {
        var hash = Hash(normalizedLine);
        if (_seen.Contains(hash))
            return true;

        if (_seen.Count >= _capacity)
        {
            var oldest = _order.First!.Value;
            _order.RemoveFirst();
            _seen.Remove(oldest);
        }

        _seen.Add(hash);
        _order.AddLast(hash);
        return false;
    }

    public void Clear()
    {
        _order.Clear();
        _seen.Clear();
    }

    private static ulong Hash(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        return XxHash64.HashToUInt64(bytes);
    }
}
