using System;
using System.Windows;
using GuildRelay.App.Exceptions;
using GuildRelay.App.Stats;
using GuildRelay.App.Tray;

namespace GuildRelay.App;

public partial class App : Application
{
    private CoreHost? _host;
    private TrayView? _trayView;
    private StatsWindowController? _statsController;
    private Config.ConfigViewModel? _configVm;
    private Config.ConfigWindow? _configWindow;

    private async void OnStartup(object sender, StartupEventArgs e)
    {
        try
        {
            _host = await CoreHost.CreateAsync().ConfigureAwait(true);
            GlobalExceptionHandler.Hook(_host.Logger);

            _statsController = new StatsWindowController(
                _host,
                rulesProvider: () => CurrentChat().CounterRules,
                statsEnabledProvider: () => CurrentChat().StatsEnabled);

            _trayView = new TrayView();
            _trayView.DataContext = new TrayViewModel(_host, OpenConfig, OpenStats, Quit);
            _trayView.Show();

            // Always open the config window on startup. Users hide it via minimize
            // (handled by the StateChanged hook in OpenConfig) — the tray icon stays.
            OpenConfig();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"GuildRelay failed to start:\n\n{ex.Message}\n\n{ex.GetType().Name}",
                "GuildRelay — Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private Core.Config.ChatConfig CurrentChat()
    {
        // The pending VM (if open) reflects unsaved edits; otherwise fall back
        // to the saved host config. The stats window should reflect the user's
        // current view of which rules are configured.
        return _configVm?.PendingConfig.Chat ?? _host!.Config.Chat;
    }

    private void OpenConfig()
    {
        if (_configWindow is { IsLoaded: true })
        {
            // Restore from hidden (minimize-to-tray) or minimized state.
            if (!_configWindow.IsVisible) _configWindow.Show();
            if (_configWindow.WindowState == WindowState.Minimized)
                _configWindow.WindowState = WindowState.Normal;
            _configWindow.Activate();
            return;
        }
        _configVm = new Config.ConfigViewModel(_host!);
        _configWindow = new Config.ConfigWindow();
        _configWindow.DataContext = _configVm;
        // Minimize button hides the window so it disappears from the taskbar
        // and lives only in the tray. To restore: tray menu → Open Config.
        _configWindow.StateChanged += (_, _) =>
        {
            if (_configWindow is not null && _configWindow.WindowState == WindowState.Minimized)
                _configWindow.Hide();
        };
        _configWindow.Closed += (_, _) =>
        {
            _configVm = null;
            _configWindow = null;
        };
        _configWindow.Show();
        _configWindow.Activate();
    }

    private void OpenStats() => _statsController?.OpenOrFocus();

    internal void OpenStatsFromConfig() => OpenStats();

    private async void Quit()
    {
        if (_trayView is not null) _trayView.Hide();
        if (_host is not null) await _host.DisposeAsync();
        Shutdown();
    }
}
