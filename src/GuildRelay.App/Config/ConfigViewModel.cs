using System;
using System.ComponentModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GuildRelay.Core.Config;

namespace GuildRelay.App.Config;

public sealed class ConfigViewModel : INotifyPropertyChanged
{
    private AppConfig _savedConfig;
    private AppConfig _pendingConfig;

    public ConfigViewModel(CoreHost host)
    {
        Host = host;
        _savedConfig  = host.Config;
        _pendingConfig = DeepClone(host.Config);
    }

    public CoreHost Host { get; }

    public AppConfig SavedConfig => _savedConfig;
    public AppConfig PendingConfig => _pendingConfig;

    // --- Convenience accessors used by the Settings tab (webhook + player) ---

    public string WebhookUrl
    {
        get => _pendingConfig.General.WebhookUrl;
        set
        {
            if (string.Equals(_pendingConfig.General.WebhookUrl, value, StringComparison.Ordinal)) return;
            SetPendingGeneral(_pendingConfig.General with { WebhookUrl = value });
        }
    }

    public string PlayerName
    {
        get => _pendingConfig.General.PlayerName;
        set
        {
            if (string.Equals(_pendingConfig.General.PlayerName, value, StringComparison.Ordinal)) return;
            SetPendingGeneral(_pendingConfig.General with { PlayerName = value });
        }
    }

    // --- Section setters raise IsDirty* events ---

    public void SetPendingChat(ChatConfig value)
    {
        if (Equals(_pendingConfig.Chat, value)) return;
        _pendingConfig = _pendingConfig with { Chat = value };
        RaiseAllDirtyFlags();
    }

    public void SetPendingAudio(AudioConfig value)
    {
        if (Equals(_pendingConfig.Audio, value)) return;
        _pendingConfig = _pendingConfig with { Audio = value };
        RaiseAllDirtyFlags();
    }

    public void SetPendingStatus(StatusConfig value)
    {
        if (Equals(_pendingConfig.Status, value)) return;
        _pendingConfig = _pendingConfig with { Status = value };
        RaiseAllDirtyFlags();
    }

    public void SetPendingGeneral(GeneralConfig value)
    {
        if (Equals(_pendingConfig.General, value)) return;
        _pendingConfig = _pendingConfig with { General = value };
        RaiseAllDirtyFlags();
    }

    // --- Dirty flags (delegate to ConfigDirty in Core for testability) ---

    public bool IsDirty            => ConfigDirty.AnyDirty(_pendingConfig, _savedConfig);
    public bool IsDirtyChatTab     => ConfigDirty.IsDirtyChatTab(_pendingConfig, _savedConfig);
    public bool IsDirtyAudioTab    => ConfigDirty.IsDirtyAudioTab(_pendingConfig, _savedConfig);
    public bool IsDirtyStatusTab   => ConfigDirty.IsDirtyStatusTab(_pendingConfig, _savedConfig);
    public bool IsDirtySettingsTab => ConfigDirty.IsDirtySettingsTab(_pendingConfig, _savedConfig);

    // --- Save / Revert ---

    public async Task SaveAsync(CancellationToken ct = default)
    {
        // This VM is UI-bound: PropertyChanged subscribers touch WPF DependencyObjects,
        // so awaits must resume on the captured UI SynchronizationContext. Do NOT add
        // ConfigureAwait(false) here — RaiseAllDirtyFlags() below must run on the UI thread.
        var oldConfig = _savedConfig;
        var newConfig = _pendingConfig;

        await Host.ConfigStore.SaveAsync(newConfig);
        Host.UpdateConfig(newConfig);

        await ConfigApplyPipeline.DispatchAsync(oldConfig, newConfig, Host.Registry, ct);

        _savedConfig  = newConfig;
        _pendingConfig = DeepClone(newConfig);
        RaiseAllDirtyFlags();
    }

    public void Revert()
    {
        _pendingConfig = DeepClone(_savedConfig);
        RaiseAllDirtyFlags();
    }

    // --- Plumbing ---

    public event PropertyChangedEventHandler? PropertyChanged;

    private void RaiseAllDirtyFlags()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PendingConfig)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDirty)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDirtyChatTab)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDirtyAudioTab)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDirtyStatusTab)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDirtySettingsTab)));
    }

    private static readonly JsonSerializerOptions CloneOpts = new() { WriteIndented = false };

    private static AppConfig DeepClone(AppConfig source)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(source, CloneOpts);
        return JsonSerializer.Deserialize<AppConfig>(bytes, CloneOpts)
            ?? throw new InvalidOperationException("AppConfig round-trip failed.");
    }
}
