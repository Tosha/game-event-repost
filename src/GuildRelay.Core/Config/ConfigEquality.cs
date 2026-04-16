using System.Collections.Generic;

namespace GuildRelay.Core.Config;

/// <summary>
/// Structural equality for the config record graph.
///
/// Records like <see cref="ChatConfig"/>, <see cref="AudioConfig"/>, and
/// <see cref="StatusConfig"/> hold <c>List&lt;&gt;</c> / <c>Dictionary&lt;,&gt;</c>
/// members. The compiler-synthesized <c>record.Equals</c> compares those by
/// reference, so a JSON-cloned copy of a config — produced by
/// <c>ConfigViewModel</c> every time the Settings window opens or a Save
/// completes — looks "different" from its saved original even when every byte
/// of content is identical. That drives the dirty-dot UI and the apply
/// pipeline into false positives. These helpers compare the collections
/// element-wise instead.
/// </summary>
public static class ConfigEquality
{
    public static bool Equal(ChatConfig a, ChatConfig b)
        => a.Enabled == b.Enabled
        && a.CaptureIntervalSec == b.CaptureIntervalSec
        && a.OcrConfidenceThreshold == b.OcrConfidenceThreshold
        && a.DefaultCooldownSec == b.DefaultCooldownSec
        && Equals(a.Region, b.Region)
        && ListEqual(a.PreprocessPipeline, b.PreprocessPipeline, Equal)
        && ListEqual(a.Rules, b.Rules, Equal)
        && DictEqual(a.Templates, b.Templates);

    public static bool Equal(AudioConfig a, AudioConfig b)
        => a.Enabled == b.Enabled
        // AudioRuleConfig is a scalar-only record — record.Equals is correct.
        && ListEqual(a.Rules, b.Rules, static (x, y) => Equals(x, y))
        && DictEqual(a.Templates, b.Templates);

    public static bool Equal(StatusConfig a, StatusConfig b)
        => a.Enabled == b.Enabled
        && a.CaptureIntervalSec == b.CaptureIntervalSec
        && a.OcrConfidenceThreshold == b.OcrConfidenceThreshold
        && a.DebounceSamples == b.DebounceSamples
        && Equals(a.Region, b.Region)
        && ListEqual(a.PreprocessPipeline, b.PreprocessPipeline, Equal)
        // DisconnectPatternConfig is scalar-only.
        && ListEqual(a.DisconnectPatterns, b.DisconnectPatterns, static (x, y) => Equals(x, y))
        && DictEqual(a.Templates, b.Templates);

    public static bool Equal(StructuredChatRule a, StructuredChatRule b)
        => a.Id == b.Id
        && a.Label == b.Label
        && a.MatchMode == b.MatchMode
        && a.CooldownSec == b.CooldownSec
        && ListEqual(a.Channels, b.Channels, static (x, y) => x == y)
        && ListEqual(a.Keywords, b.Keywords, static (x, y) => x == y);

    public static bool Equal(PreprocessStageConfig a, PreprocessStageConfig b)
        => a.Stage == b.Stage
        && DictEqual(a.Parameters, b.Parameters);

    private static bool ListEqual<T>(List<T> a, List<T> b, System.Func<T, T, bool> elementEqual)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (!elementEqual(a[i], b[i])) return false;
        return true;
    }

    private static bool DictEqual<TKey, TValue>(Dictionary<TKey, TValue>? a, Dictionary<TKey, TValue>? b)
        where TKey : notnull
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        if (a.Count != b.Count) return false;
        var valueCmp = EqualityComparer<TValue>.Default;
        foreach (var kv in a)
        {
            if (!b.TryGetValue(kv.Key, out var bv)) return false;
            if (!valueCmp.Equals(kv.Value, bv)) return false;
        }
        return true;
    }
}
