using System.Diagnostics;
using System.IO;
using System.Windows;

namespace GuildRelay.App.Tray;

public partial class TrayView : Window
{
    public TrayView() { InitializeComponent(); }

    private void OnOpenConfig(object sender, RoutedEventArgs e)
        => (DataContext as TrayViewModel)?.OpenConfig();

    private void OnOpenLogs(object sender, RoutedEventArgs e)
    {
        var vm = DataContext as TrayViewModel;
        if (vm is null) return;
        var logsDir = Path.Combine(vm.Host.AppDataDirectory, "logs");
        Process.Start(new ProcessStartInfo("explorer.exe", logsDir) { UseShellExecute = true });
    }

    private void OnQuit(object sender, RoutedEventArgs e)
        => (DataContext as TrayViewModel)?.Quit();
}
