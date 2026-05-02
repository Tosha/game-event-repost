using System;
using System.Collections.Generic;
using GuildRelay.Core.Config;

namespace GuildRelay.App.Stats;

public sealed class StatsWindowController
{
    private readonly CoreHost _host;
    private readonly Func<IReadOnlyList<CounterRule>> _rulesProvider;
    private readonly Func<bool> _statsEnabledProvider;
    private StatsWindow? _window;

    public StatsWindowController(
        CoreHost host,
        Func<IReadOnlyList<CounterRule>> rulesProvider,
        Func<bool> statsEnabledProvider)
    {
        _host = host;
        _rulesProvider = rulesProvider;
        _statsEnabledProvider = statsEnabledProvider;
    }

    public void OpenOrFocus()
    {
        if (_window is not null && _window.IsLoaded)
        {
            _window.Activate();
            return;
        }

        var vm = new StatsViewModel(
            _host.StatsAggregator,
            () => DateTimeOffset.UtcNow,
            _rulesProvider,
            _statsEnabledProvider);

        _window = new StatsWindow(vm);
        _window.Closed += (_, _) => _window = null;
        _window.Show();
    }
}
