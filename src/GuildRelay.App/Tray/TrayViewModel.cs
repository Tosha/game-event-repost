using System;

namespace GuildRelay.App.Tray;

public sealed class TrayViewModel
{
    private readonly Action _openConfig;
    private readonly Action _quit;

    public TrayViewModel(CoreHost host, Action openConfig, Action quit)
    {
        Host = host;
        _openConfig = openConfig;
        _quit = quit;
    }

    public CoreHost Host { get; }

    public void OpenConfig() => _openConfig();
    public void Quit() => _quit();
}
