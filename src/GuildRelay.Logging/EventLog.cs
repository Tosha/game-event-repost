using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GuildRelay.Core.Events;

namespace GuildRelay.Logging;

public enum EventPostStatus { Pending, Success, Failed, Dropped }

public sealed class EventLog
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _directory;

    public EventLog(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(directory);
    }

    public Task AppendAsync(DetectionEvent evt, EventPostStatus status)
        => WriteLineAsync(BuildEntry(evt, status, updateOnly: false));

    public Task UpdateStatusAsync(DetectionEvent evt, EventPostStatus status)
        => WriteLineAsync(BuildEntry(evt, status, updateOnly: true));

    private object BuildEntry(DetectionEvent evt, EventPostStatus status, bool updateOnly)
        => updateOnly
            ? (object)new
            {
                kind = "status",
                featureId = evt.FeatureId,
                ruleLabel = evt.RuleLabel,
                timestampUtc = evt.TimestampUtc,
                status = status.ToString()
            }
            : new
            {
                kind = "event",
                featureId = evt.FeatureId,
                ruleLabel = evt.RuleLabel,
                matchedContent = evt.MatchedContent,
                timestampUtc = evt.TimestampUtc,
                playerName = evt.PlayerName,
                extras = evt.Extras,
                status = status.ToString()
            };

    private async Task WriteLineAsync(object entry)
    {
        var json = JsonSerializer.Serialize(entry, Json);
        var name = $"events-{DateTime.UtcNow:yyyyMMdd}.jsonl";
        var path = Path.Combine(_directory, name);

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await File.AppendAllTextAsync(path, json + Environment.NewLine).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }
}
