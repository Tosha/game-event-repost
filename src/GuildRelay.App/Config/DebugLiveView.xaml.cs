using System;
using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GuildRelay.Features.Chat;

namespace GuildRelay.App.Config;

public partial class DebugLiveView : Wpf.Ui.Controls.FluentWindow
{
    private ChatWatcher? _watcher;

    public DebugLiveView() { InitializeComponent(); }

    public void Attach(ChatWatcher watcher)
    {
        Detach();
        _watcher = watcher;
        _watcher.DebugTick += OnDebugTick;
    }

    public void Detach()
    {
        if (_watcher is not null)
        {
            _watcher.DebugTick -= OnDebugTick;
            _watcher = null;
        }
    }

    private void OnDebugTick(ChatTickDebugInfo info)
    {
        // Marshal to UI thread
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                UpdateImage(info);
                UpdateOcrOutput(info);
                UpdateMatches(info);
            }
            catch
            {
                // Don't let UI errors kill the debug view
            }
        });
    }

    private void UpdateImage(ChatTickDebugInfo info)
    {
        if (info.ImageWidth <= 0 || info.ImageHeight <= 0) return;

        var bitmap = BitmapSource.Create(
            info.ImageWidth, info.ImageHeight,
            96, 96,
            PixelFormats.Bgra32,
            null,
            info.CapturedImageBgra,
            info.ImageStride);
        bitmap.Freeze();
        CaptureImage.Source = bitmap;
    }

    private void UpdateOcrOutput(ChatTickDebugInfo info)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < info.OcrLines.Count; i++)
        {
            var role = i < info.LineRoles.Count ? info.LineRoles[i] : "?";
            var channel = i < info.ParsedChannels.Count ? info.ParsedChannels[i] : "?";
            var normalized = i < info.NormalizedLines.Count ? info.NormalizedLines[i] : "";

            string prefix = role switch
            {
                "HEADER" => $"[{channel}]  ",
                "CONT"   => "   \u21B3    ",   // ↳
                "SKIP"   => "(skip)  ",
                _        => "        "
            };

            sb.AppendLine($"{prefix}OCR: \"{info.OcrLines[i]}\"");
            sb.AppendLine($"         Norm: \"{normalized}\"");
        }
        if (info.OcrLines.Count == 0)
            sb.AppendLine("(no text detected)");

        OcrOutputBox.Text = sb.ToString();
    }

    private void UpdateMatches(ChatTickDebugInfo info)
    {
        if (info.MatchResults.Count == 0)
        {
            MatchBox.Text = $"[{info.Timestamp:HH:mm:ss}] No matches this tick";
            return;
        }

        var sb = new StringBuilder();
        foreach (var result in info.MatchResults)
            sb.AppendLine($"[{info.Timestamp:HH:mm:ss}] {result}");
        MatchBox.Text = sb.ToString();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        Detach();
    }
}
