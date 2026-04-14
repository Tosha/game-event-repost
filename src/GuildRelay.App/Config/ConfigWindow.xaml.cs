using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using GuildRelay.Core.Events;

namespace GuildRelay.App.Config;

public partial class ConfigWindow : Wpf.Ui.Controls.FluentWindow
{
    public ConfigWindow() { InitializeComponent(); Loaded += OnLoaded; }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ConfigViewModel vm) return;
        WebhookBox.Password = vm.WebhookUrl;
        PlayerBox.Text = vm.PlayerName;

        // Load Chat Watcher advanced settings
        var chat = vm.Host.Config.Chat;
        IntervalBox.Text = chat.CaptureIntervalMs.ToString();
        ConfidenceBox.Text = chat.OcrConfidenceThreshold.ToString("F2");
        CooldownBox.Text = chat.DefaultCooldownSec.ToString();

        ChatTab.DataContext = vm;
        AudioTab.DataContext = vm;
        StatusTab.DataContext = vm;

        UpdateIndicators(vm);
    }

    public void UpdateIndicators(ConfigViewModel vm)
    {
        ChatDot.Text = vm.Host.Config.Chat.Enabled ? "\u25CF" : "";
        AudioDot.Text = vm.Host.Config.Audio.Enabled ? "\u25CF" : "";
        StatusDot.Text = vm.Host.Config.Status.Enabled ? "\u25CF" : "";
    }

    private async void OnTestWebhookClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ConfigViewModel vm) return;
        StatusText.Text = "Testing...";
        try
        {
            vm.WebhookUrl = WebhookBox.Password;
            vm.PlayerName = PlayerBox.Text;
            vm.Apply();

            await vm.Host.Publisher.PublishAsync(new DetectionEvent(
                FeatureId: "test",
                RuleLabel: "Connection test",
                MatchedContent: $"GuildRelay connected - hello from {vm.PlayerName}",
                TimestampUtc: DateTimeOffset.UtcNow,
                PlayerName: vm.PlayerName,
                Extras: new Dictionary<string, string>(),
                ImageAttachment: null), CancellationToken.None);

            StatusText.Text = "Test message sent.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Test failed: " + ex.Message;
            vm.Host.Logger.Error(ex, "Test webhook post failed");
        }
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ConfigViewModel vm) return;
        vm.WebhookUrl = WebhookBox.Password;
        vm.PlayerName = PlayerBox.Text;

        // Save Chat Watcher advanced settings
        var newChat = vm.Host.Config.Chat with
        {
            CaptureIntervalMs = int.TryParse(IntervalBox.Text, out var iv) ? iv : 1000,
            OcrConfidenceThreshold = double.TryParse(ConfidenceBox.Text, out var ct) ? ct : 0.65,
            DefaultCooldownSec = int.TryParse(CooldownBox.Text, out var cd) ? cd : 600
        };
        var newConfig = vm.Host.Config with { Chat = newChat };
        vm.Host.UpdateConfig(newConfig);

        await vm.SaveAsync();
        StatusText.Text = "Saved.";
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
