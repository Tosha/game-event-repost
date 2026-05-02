using System;

namespace GuildRelay.App.Tray;

public sealed class TrayViewModel
{
    private readonly Action _openConfig;
    private readonly Action _openStats;
    private readonly Action _quit;

    public TrayViewModel(CoreHost host, Action openConfig, Action openStats, Action quit)
    {
        Host = host;
        _openConfig = openConfig;
        _openStats = openStats;
        _quit = quit;
    }

    public CoreHost Host { get; }

    public void OpenConfig() => _openConfig();
    public void OpenStats()  => _openStats();
    public void Quit()       => _quit();
}
