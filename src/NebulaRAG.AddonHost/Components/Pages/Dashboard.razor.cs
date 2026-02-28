using Microsoft.AspNetCore.Components;

namespace NebulaRAG.AddonHost.Components.Pages;

/// <summary>
/// Code-behind for dashboard navigation and periodic refresh orchestration.
/// </summary>
public partial class Dashboard : IAsyncDisposable
{
    /// <summary>
    /// Gets or sets the ingress path segment captured by the catch-all route.
    /// </summary>
    [Parameter]
    public string? IngressPath { get; set; }

    private static readonly DashboardTab[] Tabs = [DashboardTab.Overview, DashboardTab.Rag, DashboardTab.Memory, DashboardTab.Plans];
    private PeriodicTimer? _refreshTimer;
    private CancellationTokenSource? _refreshCancellation;
    private DashboardTab _activeTab = DashboardTab.Overview;
    private long _refreshNonce = 1;

    /// <summary>
    /// Exposes dashboard tab list for markup rendering.
    /// </summary>
    private IReadOnlyList<DashboardTab> _tabs => Tabs;

    /// <summary>
    /// Starts the periodic refresh loop used by tab child components.
    /// </summary>
    protected override void OnInitialized()
    {
        _refreshCancellation = new CancellationTokenSource();
        _refreshTimer = new PeriodicTimer(TimeSpan.FromSeconds(20));
        _ = RunRefreshLoopAsync(_refreshCancellation.Token);
        TriggerRefresh();
    }

    /// <summary>
    /// Advances refresh nonce and requests rerender so child tabs can sync data.
    /// </summary>
    private Task RefreshAsync()
    {
        TriggerRefresh();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Returns CSS classes for active/inactive tab buttons.
    /// </summary>
    /// <param name="tab">Tab candidate being rendered.</param>
    /// <returns>CSS class string for the tab button.</returns>
    private string GetTabClass(DashboardTab tab)
    {
        return _activeTab == tab
            ? "border border-nebula-accent bg-nebula-accent/20 text-nebula-text"
            : "border border-transparent text-nebula-muted hover:border-nebula-border hover:text-nebula-text";
    }

    /// <summary>
    /// Stops periodic refresh resources when the component is disposed.
    /// </summary>
    /// <returns>Completion task.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_refreshCancellation is not null)
        {
            await _refreshCancellation.CancelAsync();
            _refreshCancellation.Dispose();
        }

        _refreshTimer?.Dispose();
    }

    /// <summary>
    /// Background loop that emits refresh ticks on a fixed cadence.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for loop termination.</param>
    /// <returns>Completion task.</returns>
    private async Task RunRefreshLoopAsync(CancellationToken cancellationToken)
    {
        if (_refreshTimer is null)
        {
            return;
        }

        try
        {
            while (await _refreshTimer.WaitForNextTickAsync(cancellationToken))
            {
                await InvokeAsync(TriggerRefresh);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when dashboard is disposed.
        }
    }

    /// <summary>
    /// Updates refresh nonce used by child tabs to detect synchronization events.
    /// </summary>
    private void TriggerRefresh()
    {
        _refreshNonce++;
        StateHasChanged();
    }

    /// <summary>
    /// Dashboard tabs rendered by the main page shell.
    /// </summary>
    private enum DashboardTab
    {
        Overview,
        Rag,
        Memory,
        Plans
    }
}
