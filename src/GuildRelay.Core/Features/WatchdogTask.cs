using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GuildRelay.Core.Features;

public enum WatchdogState { Idle, Running, Stopped, Error }

/// <summary>
/// Runs a user-supplied task body with retry + backoff. When the body
/// throws, <see cref="WatchdogTask"/> waits the next backoff interval and
/// restarts. When the supplied backoff list is exhausted, the watchdog
/// gives up and transitions to <see cref="WatchdogState.Error"/>.
/// </summary>
public sealed class WatchdogTask
{
    private readonly string _name;
    private readonly Func<CancellationToken, Task> _body;
    private readonly IReadOnlyList<TimeSpan> _backoffs;
    private readonly TaskCompletionSource _terminal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CancellationTokenSource? _cts;

    public WatchdogTask(string name, Func<CancellationToken, Task> body, IReadOnlyList<TimeSpan> backoffs)
    {
        _name = name;
        _body = body;
        _backoffs = backoffs;
    }

    public WatchdogState State { get; private set; } = WatchdogState.Idle;
    public string Name => _name;
    public Exception? LastError { get; private set; }

    public Task StartAsync(CancellationToken outer)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(outer);
        _ = Task.Run(() => RunLoopAsync(_cts.Token));
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _cts?.Cancel();
        return Task.CompletedTask;
    }

    public Task WaitForTerminalAsync(TimeSpan timeout)
        => _terminal.Task.WaitAsync(timeout);

    private async Task RunLoopAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < _backoffs.Count; attempt++)
        {
            try
            {
                State = WatchdogState.Running;
                await _body(ct).ConfigureAwait(false);
                // Body completed normally — we're done.
                State = WatchdogState.Stopped;
                _terminal.TrySetResult();
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                State = WatchdogState.Stopped;
                _terminal.TrySetResult();
                return;
            }
            catch (Exception ex)
            {
                LastError = ex;

                // Was this the last allowed attempt?
                if (attempt >= _backoffs.Count - 1)
                {
                    State = WatchdogState.Error;
                    _terminal.TrySetResult();
                    return;
                }

                // Wait the backoff interval before retrying.
                try
                {
                    await Task.Delay(_backoffs[attempt], ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    State = WatchdogState.Stopped;
                    _terminal.TrySetResult();
                    return;
                }
            }
        }
    }
}
