using System;
using System.ComponentModel;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GuildRelay.Core.Events;

namespace GuildRelay.App.Config;

public partial class ConfigWindow : Wpf.Ui.Controls.FluentWindow
{
    private const string UnsavedText = "● Unsaved changes";
    private bool _loading;
    private bool _closing;

    public ConfigWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ConfigViewModel vm) return;

        _loading = true;
        WebhookBox.Password = vm.PendingConfig.General.WebhookUrl;
        PlayerBox.Text      = vm.PendingConfig.General.PlayerName;

        IntervalBox.Text   = vm.PendingConfig.Chat.CaptureIntervalSec.ToString();
        ConfidenceBox.Text = vm.PendingConfig.Chat.OcrConfidenceThreshold.ToString("F2");
        CooldownBox.Text   = vm.PendingConfig.Chat.DefaultCooldownSec.ToString();
        _loading = false;

        ChatTab.DataContext   = vm;
        AudioTab.DataContext  = vm;
        StatusTab.DataContext = vm;

        vm.PropertyChanged += OnViewModelPropertyChanged;
        UpdateDirtyUi(vm);
        UpdateActiveDots(vm);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (DataContext is not ConfigViewModel vm) return;
        UpdateDirtyUi(vm);
        UpdateActiveDots(vm);
    }

    private void UpdateDirtyUi(ConfigViewModel vm)
    {
        SaveButton.IsEnabled   = vm.IsDirty;
        RevertButton.IsEnabled = vm.IsDirty;

        if (vm.IsDirty)
            FooterStatusText.Text = UnsavedText;
        else if (FooterStatusText.Text == UnsavedText)
            FooterStatusText.Text = "";

        ChatDot.Visibility     = vm.IsDirtyChatTab     ? Visibility.Visible : Visibility.Collapsed;
        AudioDot.Visibility    = vm.IsDirtyAudioTab    ? Visibility.Visible : Visibility.Collapsed;
        StatusDot.Visibility   = vm.IsDirtyStatusTab   ? Visibility.Visible : Visibility.Collapsed;
        SettingsDot.Visibility = vm.IsDirtySettingsTab ? Visibility.Visible : Visibility.Collapsed;
    }

    // Green dot on a feature tab = that feature is currently running. Tracks
    // SavedConfig (not PendingConfig) because the feature watchdog only starts
    // or stops when a save goes through the apply pipeline — toggling the
    // switch without saving just dirties the config.
    private void UpdateActiveDots(ConfigViewModel vm)
    {
        ChatActiveDot.Visibility   = vm.SavedConfig.Chat.Enabled   ? Visibility.Visible : Visibility.Collapsed;
        AudioActiveDot.Visibility  = vm.SavedConfig.Audio.Enabled  ? Visibility.Visible : Visibility.Collapsed;
        StatusActiveDot.Visibility = vm.SavedConfig.Status.Enabled ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.S
            && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control
            && DataContext is ConfigViewModel vm
            && vm.IsDirty)
        {
            e.Handled = true;
            await DoSaveAsync(vm);
        }
    }

    // --- Settings tab edit handlers ---

    private void OnWebhookChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || DataContext is not ConfigViewModel vm) return;
        vm.WebhookUrl = WebhookBox.Password;
    }

    private void OnPlayerNameChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || DataContext is not ConfigViewModel vm) return;
        vm.PlayerName = PlayerBox.Text;
    }

    private void OnChatAdvancedChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || DataContext is not ConfigViewModel vm) return;

        var iv = int.TryParse(IntervalBox.Text, out var i) ? i : vm.PendingConfig.Chat.CaptureIntervalSec;
        var ct = double.TryParse(ConfidenceBox.Text, System.Globalization.NumberStyles.Float,
                                  System.Globalization.CultureInfo.InvariantCulture, out var c)
                 ? c : vm.PendingConfig.Chat.OcrConfidenceThreshold;
        var cd = int.TryParse(CooldownBox.Text, out var d) ? d : vm.PendingConfig.Chat.DefaultCooldownSec;

        vm.SetPendingChat(vm.PendingConfig.Chat with
        {
            CaptureIntervalSec    = iv,
            OcrConfidenceThreshold = ct,
            DefaultCooldownSec    = cd
        });
    }

    // --- Footer actions ---

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ConfigViewModel vm) return;
        await DoSaveAsync(vm);
    }

    private void OnRevertClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ConfigViewModel vm) return;
        vm.Revert();
        ReloadLocalUiFields(vm);
        FooterStatusText.Text = "Reverted to saved.";
    }

    private async System.Threading.Tasks.Task DoSaveAsync(ConfigViewModel vm)
    {
        try
        {
            await vm.SaveAsync();
            FooterStatusText.Text = "Saved.";
        }
        catch (Exception ex)
        {
            FooterStatusText.Text = "Save failed — see logs";
            vm.Host.Logger.Error(ex, "Saving config failed");
        }
    }

    private void ReloadLocalUiFields(ConfigViewModel vm)
    {
        _loading = true;
        WebhookBox.Password = vm.PendingConfig.General.WebhookUrl;
        PlayerBox.Text      = vm.PendingConfig.General.PlayerName;
        IntervalBox.Text    = vm.PendingConfig.Chat.CaptureIntervalSec.ToString();
        ConfidenceBox.Text  = vm.PendingConfig.Chat.OcrConfidenceThreshold.ToString("F2");
        CooldownBox.Text    = vm.PendingConfig.Chat.DefaultCooldownSec.ToString();
        _loading = false;
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closing) return;
        if (DataContext is not ConfigViewModel vm) return;
        if (!vm.IsDirty) return;

        e.Cancel = true;
        _closing = true;
        await DoSaveAsync(vm);
        Close();
    }

    // --- Test webhook (operates on PendingConfig, does not persist) ---

    private async void OnTestWebhookClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ConfigViewModel vm) return;
        FooterStatusText.Text = "Testing webhook...";
        try
        {
            vm.Host.Secrets.SetWebhookUrl(WebhookBox.Password);

            await vm.Host.Publisher.PublishAsync(new DetectionEvent(
                FeatureId: "test",
                RuleLabel: "Connection test",
                MatchedContent: $"GuildRelay connected - hello from {PlayerBox.Text}",
                TimestampUtc: DateTimeOffset.UtcNow,
                PlayerName: PlayerBox.Text,
                Extras: new System.Collections.Generic.Dictionary<string, string>(),
                ImageAttachment: null), CancellationToken.None);

            FooterStatusText.Text = "Test message sent.";
        }
        catch (Exception ex)
        {
            FooterStatusText.Text = "Test failed: " + ex.Message;
            vm.Host.Logger.Error(ex, "Test webhook post failed");
        }
        finally
        {
            // Restore the currently-saved webhook URL so an un-saved test doesn't
            // linger as the active secret for running features.
            vm.Host.Secrets.SetWebhookUrl(vm.SavedConfig.General.WebhookUrl);
        }
    }
}
