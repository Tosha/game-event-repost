namespace GuildRelay.Core.Config;

/// <summary>
/// Pure per-tab / whole-config dirty predicates used by the Settings UI's
/// ViewModel. Kept in Core so it can be unit tested without WPF.
///
/// Section comparisons go through <see cref="ConfigEquality"/> because record
/// <c>Equals</c> would compare their <c>List&lt;&gt;</c> / <c>Dictionary&lt;,&gt;</c>
/// members by reference — which is always false after the ViewModel's JSON
/// clone, even when content is identical.
/// </summary>
public static class ConfigDirty
{
    public static bool AnyDirty(AppConfig pending, AppConfig saved)
        => !ConfigEquality.Equal(pending.Chat,   saved.Chat)
        || !ConfigEquality.Equal(pending.Audio,  saved.Audio)
        || !ConfigEquality.Equal(pending.Status, saved.Status)
        || !Equals(pending.General, saved.General);

    // Chat tab edits: EventRepostEnabled, StatsEnabled, Region, Rules, CounterRules.
    // (CaptureIntervalSec / OcrConfidenceThreshold / DefaultCooldownSec are edited
    // on the Settings tab even though they live in ChatConfig.)
    public static bool IsDirtyChatTab(AppConfig pending, AppConfig saved)
    {
        if (pending.Chat.EventRepostEnabled != saved.Chat.EventRepostEnabled) return true;
        if (pending.Chat.StatsEnabled != saved.Chat.StatsEnabled) return true;
        if (!Equals(pending.Chat.Region, saved.Chat.Region)) return true;
        if (pending.Chat.Rules.Count != saved.Chat.Rules.Count) return true;
        for (int i = 0; i < pending.Chat.Rules.Count; i++)
            if (!ConfigEquality.Equal(pending.Chat.Rules[i], saved.Chat.Rules[i])) return true;
        if (pending.Chat.CounterRules.Count != saved.Chat.CounterRules.Count) return true;
        for (int i = 0; i < pending.Chat.CounterRules.Count; i++)
            if (!ConfigEquality.Equal(pending.Chat.CounterRules[i], saved.Chat.CounterRules[i])) return true;
        return false;
    }

    public static bool IsDirtyAudioTab(AppConfig pending, AppConfig saved)
        => !ConfigEquality.Equal(pending.Audio, saved.Audio);

    public static bool IsDirtyStatusTab(AppConfig pending, AppConfig saved)
        => !ConfigEquality.Equal(pending.Status, saved.Status);

    // Settings tab edits: the whole General section plus the Chat advanced subset.
    public static bool IsDirtySettingsTab(AppConfig pending, AppConfig saved)
        => !Equals(pending.General, saved.General)
        || pending.Chat.CaptureIntervalSec     != saved.Chat.CaptureIntervalSec
        || pending.Chat.OcrConfidenceThreshold != saved.Chat.OcrConfidenceThreshold
        || pending.Chat.DefaultCooldownSec     != saved.Chat.DefaultCooldownSec;
}
