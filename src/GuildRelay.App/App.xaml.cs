using System.Windows;
using GuildRelay.App.Exceptions;
using GuildRelay.App.Tray;

namespace GuildRelay.App;

public partial class App : Application
{
    private CoreHost? _host;
    private TrayView? _trayView;

    private async void OnStartup(object sender, StartupEventArgs e)
    {
        _host = await CoreHost.CreateAsync().ConfigureAwait(true);
        GlobalExceptionHandler.Hook(_host.Logger);

        _trayView = new TrayView();
        _trayView.DataContext = new TrayViewModel(_host, OpenConfig, Quit);
        _trayView.Show();

        if (!_host.Secrets.HasWebhookUrl)
            OpenConfig();
    }

    private void OpenConfig()
    {
        var window = new Config.ConfigWindow();
        window.DataContext = new Config.ConfigViewModel(_host!);
        window.Show();
        window.Activate();
    }

    private async void Quit()
    {
        if (_trayView is not null) _trayView.Hide();
        if (_host is not null) await _host.DisposeAsync();
        Shutdown();
    }
}
