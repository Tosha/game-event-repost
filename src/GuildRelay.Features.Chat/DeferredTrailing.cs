using System.Collections.Generic;

namespace GuildRelay.Features.Chat;

/// <summary>
/// Resolves a previous tick's trailing message against the current tick's
/// assembly. Implements the one-tick deferral described in the ADR: the last
/// message on screen is buffered until the next tick, then emitted as either
/// its grown form (if the next tick confirmed it extended) or its buffered
/// form (if it stayed the same or scrolled off).
/// </summary>
public static class DeferredTrailing
{
    public static (IReadOnlyList<AssembledMessage> ToEmit, AssembledMessage? NewBuffer)
        Resolve(AssembledMessage? previousTrailing, AssemblyResult current)
    {
        var toEmit = new List<AssembledMessage>();

        if (previousTrailing is not null)
        {
            var resolvedByCompleted = false;
            foreach (var msg in current.Completed)
            {
                if (IsGrownVersion(msg, previousTrailing))
                {
                    resolvedByCompleted = true;
                    break;
                }
            }
            if (!resolvedByCompleted)
                toEmit.Add(previousTrailing);
        }

        toEmit.AddRange(current.Completed);
        return (toEmit, current.Trailing);
    }

    /// <summary>
    /// Candidate is the grown version of previous iff header identity matches
    /// and candidate's body starts with previous's body.
    /// </summary>
    private static bool IsGrownVersion(AssembledMessage candidate, AssembledMessage previous)
    {
        if (candidate.Channel != previous.Channel) return false;
        if (candidate.PlayerName != previous.PlayerName) return false;
        if (candidate.Timestamp != previous.Timestamp) return false;
        return candidate.Body.StartsWith(previous.Body);
    }
}
