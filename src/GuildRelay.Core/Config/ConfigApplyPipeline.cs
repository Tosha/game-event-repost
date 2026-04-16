using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GuildRelay.Core.Features;

namespace GuildRelay.Core.Config;

/// <summary>
/// Diffs two <see cref="AppConfig"/> snapshots and dispatches the correct
/// sequence of ApplyConfig / Start / Stop calls for each feature so that
/// running watchers pick up changes with minimal downtime.
/// </summary>
public static class ConfigApplyPipeline
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    public static async Task DispatchAsync(
        AppConfig oldConfig,
        AppConfig newConfig,
        IFeatureRegistry registry,
        CancellationToken ct)
    {
        await DispatchFeatureAsync(
            name: "chat",
            oldEnabled: oldConfig.Chat.Enabled, newEnabled: newConfig.Chat.Enabled,
            oldCfg: oldConfig.Chat, newCfg: newConfig.Chat,
            equal: ConfigEquality.Equal,
            needsRestart: ChatNeedsRestart,
            registry: registry,
            ct: ct).ConfigureAwait(false);

        await DispatchFeatureAsync(
            name: "audio",
            oldEnabled: oldConfig.Audio.Enabled, newEnabled: newConfig.Audio.Enabled,
            oldCfg: oldConfig.Audio, newCfg: newConfig.Audio,
            equal: ConfigEquality.Equal,
            needsRestart: static (_, _) => false,
            registry: registry,
            ct: ct).ConfigureAwait(false);

        await DispatchFeatureAsync(
            name: "status",
            oldEnabled: oldConfig.Status.Enabled, newEnabled: newConfig.Status.Enabled,
            oldCfg: oldConfig.Status, newCfg: newConfig.Status,
            equal: ConfigEquality.Equal,
            needsRestart: StatusNeedsRestart,
            registry: registry,
            ct: ct).ConfigureAwait(false);
    }

    private static async Task DispatchFeatureAsync<T>(
        string name,
        bool oldEnabled, bool newEnabled,
        T oldCfg, T newCfg,
        System.Func<T, T, bool> equal,
        System.Func<T, T, bool> needsRestart,
        IFeatureRegistry registry,
        CancellationToken ct)
        where T : notnull
    {
        // Case 1: stays disabled — nothing runtime.
        if (!oldEnabled && !newEnabled) return;

        // Case 2: enabled -> disabled. Stop, no config push needed.
        if (oldEnabled && !newEnabled)
        {
            await registry.StopAsync(name).ConfigureAwait(false);
            return;
        }

        // Case 3: disabled -> enabled. Seed new config into the feature instance, then start.
        if (!oldEnabled && newEnabled)
        {
            await registry.ApplyConfigAsync(name, Serialize(newCfg)).ConfigureAwait(false);
            await registry.StartAsync(name, ct).ConfigureAwait(false);
            return;
        }

        // Case 4: both enabled. If config didn't change, no-op. Use structural
        // equality — record Equals would compare list/dict members by reference
        // and spuriously report "changed" every time after a ViewModel clone.
        if (equal(oldCfg, newCfg)) return;

        // Otherwise push the new config, then restart if a baked-in field changed.
        await registry.ApplyConfigAsync(name, Serialize(newCfg)).ConfigureAwait(false);
        if (needsRestart(oldCfg, newCfg))
        {
            await registry.StopAsync(name).ConfigureAwait(false);
            await registry.StartAsync(name, ct).ConfigureAwait(false);
        }
    }

    private static JsonElement Serialize<T>(T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOpts);
        using var doc = JsonDocument.Parse(bytes);
        return doc.RootElement.Clone();
    }

    // Restart classifiers per the spec's restart-trigger table.
    private static bool ChatNeedsRestart(ChatConfig old, ChatConfig cur)
        => old.CaptureIntervalSec != cur.CaptureIntervalSec;

    private static bool StatusNeedsRestart(StatusConfig old, StatusConfig cur)
        => old.CaptureIntervalSec != cur.CaptureIntervalSec
        || old.DebounceSamples   != cur.DebounceSamples;
}
