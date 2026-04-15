namespace GuildRelay.Core.Config;

/// <summary>
/// Pure per-tab / whole-config dirty predicates used by the Settings UI's
/// ViewModel. Kept in Core so it can be unit tested without WPF.
/// </summary>
public static class ConfigDirty
{
    public static bool AnyDirty(AppConfig pending, AppConfig saved)
        => !Equals(pending.Chat,    saved.Chat)
        || !Equals(pending.Audio,   saved.Audio)
        || !Equals(pending.Status,  saved.Status)
        || !Equals(pending.General, saved.General);

    // Chat tab edits: Enabled, Region, Rules. (CaptureIntervalSec /
    // OcrConfidenceThreshold / DefaultCooldownSec are edited on the Settings
    // tab even though they live in ChatConfig.)
    public static bool IsDirtyChatTab(AppConfig pending, AppConfig saved)
        => pending.Chat.Enabled != saved.Chat.Enabled
        || !Equals(pending.Chat.Region, saved.Chat.Region)
        || !Equals(pending.Chat.Rules,  saved.Chat.Rules);

    public static bool IsDirtyAudioTab(AppConfig pending, AppConfig saved)
        => !Equals(pending.Audio, saved.Audio);

    public static bool IsDirtyStatusTab(AppConfig pending, AppConfig saved)
        => !Equals(pending.Status, saved.Status);

    // Settings tab edits: the whole General section plus the Chat advanced subset.
    public static bool IsDirtySettingsTab(AppConfig pending, AppConfig saved)
        => !Equals(pending.General, saved.General)
        || pending.Chat.CaptureIntervalSec     != saved.Chat.CaptureIntervalSec
        || pending.Chat.OcrConfidenceThreshold != saved.Chat.OcrConfidenceThreshold
        || pending.Chat.DefaultCooldownSec     != saved.Chat.DefaultCooldownSec;
}
