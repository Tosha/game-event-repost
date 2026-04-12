using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using GuildRelay.Core.Events;

namespace GuildRelay.App.Config;

public partial class ConfigWindow : Window
{
    public ConfigWindow() { InitializeComponent(); Loaded += OnLoaded; }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ConfigViewModel vm) return;
        WebhookBox.Password = vm.WebhookUrl;
        PlayerBox.Text = vm.PlayerName;
        ChatTab.DataContext = vm;
        AudioTab.DataContext = vm;
        StatusTab.DataContext = vm;
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
        await vm.SaveAsync();
        StatusText.Text = "Saved.";
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
