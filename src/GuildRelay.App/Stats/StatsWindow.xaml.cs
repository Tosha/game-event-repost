using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace GuildRelay.App.Stats;

public partial class StatsWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly StatsViewModel _vm;
    private readonly DispatcherTimer _timer;

    public StatsWindow(StatsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) => { Refresh(); _timer.Start(); };
        Closed += (_, _) => _timer.Stop();
    }

    private void Refresh()
    {
        _vm.Refresh();
        Grid.ItemsSource = _vm.Rows;
        BadgeText.Text = _vm.BadgeState;
        EmptyHint.Visibility = _vm.HasNoRules ? Visibility.Visible : Visibility.Collapsed;
        Grid.Visibility       = _vm.HasNoRules ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnRowResetClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string label)
        {
            _vm.ResetCounter(label);
            Refresh();
        }
    }

    private void OnResetAllClick(object sender, RoutedEventArgs e)
    {
        _vm.ResetAll();
        Refresh();
    }

    private void OnAlwaysOnTopChanged(object sender, RoutedEventArgs e)
    {
        Topmost = AlwaysOnTopCheck.IsChecked == true;
    }
}
