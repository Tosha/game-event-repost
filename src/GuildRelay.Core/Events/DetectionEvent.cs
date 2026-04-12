using System;
using System.Collections.Generic;

namespace GuildRelay.Core.Events;

/// <summary>
/// A single detection produced by an <c>IFeature</c>. Flows through the
/// event bus to the Discord publisher and the event log.
/// </summary>
public sealed record DetectionEvent(
    string FeatureId,
    string RuleLabel,
    string MatchedContent,
    DateTimeOffset TimestampUtc,
    string PlayerName,
    IReadOnlyDictionary<string, string> Extras,
    byte[]? ImageAttachment
);
