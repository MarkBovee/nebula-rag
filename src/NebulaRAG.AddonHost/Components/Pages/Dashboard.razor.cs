using Microsoft.AspNetCore.Components;
using NebulaRAG.AddonHost.Services;
using NebulaRAG.Core.Models;

namespace NebulaRAG.AddonHost.Components.Pages;

/// <summary>
/// Code-behind for dashboard navigation and periodic refresh orchestration.
/// </summary>
public partial class Dashboard : IAsyncDisposable
{
    [Inject]
    private DashboardSnapshotService SnapshotService { get; set; } = default!;

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
    private readonly List<ProjectDashboardNode> _projects = [];
    private DashboardSnapshotResponse? _shellSnapshot;
    private bool _refreshing;
    private string? _selectedProjectId;
    private string? _shellError;

    /// <summary>
    /// Exposes dashboard tab list for markup rendering.
    /// </summary>
    private IReadOnlyList<DashboardTab> _tabs => Tabs;

    /// <summary>
    /// Starts the periodic refresh loop used by tab child components.
    /// </summary>
    protected override async Task OnInitializedAsync()
    {
        _refreshCancellation = new CancellationTokenSource();
        _refreshTimer = new PeriodicTimer(TimeSpan.FromSeconds(20));
        await RefreshAsync();
        _ = RunRefreshLoopAsync(_refreshCancellation.Token);
    }

    /// <summary>
    /// Reloads shell data and advances refresh nonce so child tabs can sync data.
    /// </summary>
    private async Task RefreshAsync()
    {
        _refreshing = true;

        try
        {
            var snapshotTask = SnapshotService.GetDashboardAsync();
            var projectsTask = SnapshotService.GetProjectHierarchyAsync();
            await Task.WhenAll(snapshotTask, projectsTask);

            _shellSnapshot = await snapshotTask;
            _projects.Clear();
            _projects.AddRange(await projectsTask);
            ReconcileSelectedProject();
            _shellError = null;
        }
        catch (Exception exception)
        {
            _shellError = $"Shell refresh failed: {exception.Message}";
        }
        finally
        {
            _refreshing = false;
        }

        TriggerRefresh();
    }

    /// <summary>
    /// Returns CSS classes for active/inactive nav buttons.
    /// </summary>
    /// <param name="tab">Tab candidate being rendered.</param>
    /// <returns>CSS class string for the tab button.</returns>
    private string GetTabClass(DashboardTab tab)
    {
        return _activeTab == tab
            ? "nav-button-active"
            : string.Empty;
    }

    /// <summary>
    /// Sets the active tab in the dashboard shell.
    /// </summary>
    /// <param name="tab">Selected dashboard tab.</param>
    private void SetActiveTab(DashboardTab tab)
    {
        _activeTab = tab;
    }

    /// <summary>
    /// Sets the active project context for the dashboard shell.
    /// </summary>
    /// <param name="projectId">Selected project identifier, or null for all projects.</param>
    private void SetSelectedProject(string? projectId)
    {
        _selectedProjectId = string.IsNullOrWhiteSpace(projectId) ? null : projectId;
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
                await InvokeAsync(RefreshAsync);
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
    /// Gets the short description shown under the section name.
    /// </summary>
    /// <param name="tab">Current tab.</param>
    /// <returns>Tab subtitle text.</returns>
    private static string GetTabSubtitle(DashboardTab tab)
    {
        return tab switch
        {
            DashboardTab.Overview => "Pulse, projects, activity",
            DashboardTab.Rag => "Docs, indexing, purge",
            DashboardTab.Memory => "Ledger, search, purge",
            DashboardTab.Plans => "Plans, tasks, purge",
            _ => "Operations"
        };
    }

    /// <summary>
    /// Gets the hero heading for the active tab.
    /// </summary>
    /// <param name="tab">Current tab.</param>
    /// <returns>Large heading text.</returns>
    private static string GetTabHeading(DashboardTab tab)
    {
        return tab switch
        {
            DashboardTab.Overview => "Overview",
            DashboardTab.Rag => "RAG",
            DashboardTab.Memory => "Memory",
            DashboardTab.Plans => "Plans",
            _ => "Nebula operations"
        };
    }

    /// <summary>
    /// Gets the detailed description for the hero panel.
    /// </summary>
    /// <param name="tab">Current tab.</param>
    /// <returns>Descriptive body text.</returns>
    private static string GetTabDescription(DashboardTab tab)
    {
        return tab switch
        {
            DashboardTab.Overview => "Watch health, throughput, memory growth, and project balance in one pass. This is the system-wide control room for NebulaRAG.",
            DashboardTab.Rag => "List, search, view, edit, delete, and purge indexed documents without dropping into MCP tooling.",
            DashboardTab.Memory => "Manage project-owned memory records directly: create, search, edit, delete, and purge from one ledger.",
            DashboardTab.Plans => "Manage project-owned plans and tasks directly, without using session filters as the primary dashboard axis.",
            _ => "Operational dashboard"
        };
    }

    /// <summary>
    /// Gets the numeric metric displayed alongside each tab button.
    /// </summary>
    /// <param name="tab">Current tab.</param>
    /// <returns>Short metric string.</returns>
    private string GetTabMetric(DashboardTab tab)
    {
        var selectedProject = GetSelectedProject();
        return tab switch
        {
            DashboardTab.Overview => (selectedProject is null ? _shellSnapshot?.Stats.ProjectCount ?? 0 : 1).ToString(),
            DashboardTab.Rag => (selectedProject?.Rag.DocumentCount ?? _shellSnapshot?.Stats.DocumentCount ?? 0).ToString(),
            DashboardTab.Memory => (selectedProject?.Memory.MemoryCount ?? _shellSnapshot?.MemoryStats.TotalMemories ?? 0).ToString(),
            DashboardTab.Plans => (selectedProject?.Plans.PlanCount ?? _projects.Sum(project => project.Plans.PlanCount)).ToString(),
            _ => "0"
        };
    }

    /// <summary>
    /// Gets the CSS class for one project selector chip.
    /// </summary>
    /// <param name="projectId">Project represented by the chip.</param>
    /// <returns>CSS class string.</returns>
    private string GetProjectChipClass(string? projectId)
    {
        return string.Equals(_selectedProjectId, projectId, StringComparison.OrdinalIgnoreCase)
            || (string.IsNullOrWhiteSpace(projectId) && string.IsNullOrWhiteSpace(_selectedProjectId))
            ? "project-pill-active"
            : string.Empty;
    }

    /// <summary>
    /// Gets the compact context eyebrow text.
    /// </summary>
    /// <returns>Eyebrow text.</returns>
    private string GetProjectEyebrow()
    {
        return string.IsNullOrWhiteSpace(_selectedProjectId) ? "Cross-project operations" : "Project operations";
    }

    /// <summary>
    /// Gets the compact context heading.
    /// </summary>
    /// <returns>Heading text.</returns>
    private string GetContextHeading()
    {
        return GetSelectedProject()?.ProjectId ?? "All Nebula data";
    }

    /// <summary>
    /// Gets the compact context description.
    /// </summary>
    /// <returns>Description text.</returns>
    private string GetContextDescription()
    {
        return _activeTab switch
        {
            DashboardTab.Overview when GetSelectedProject() is null => "A compact control view across all indexed projects, memory, plans, and recent operator activity.",
            DashboardTab.Overview => "Project-centered telemetry, footprint, and execution posture without an oversized landing page.",
            DashboardTab.Rag when GetSelectedProject() is null => "Document-level RAG management across the full Nebula corpus, including view, edit, delete, and purge flows.",
            DashboardTab.Rag => "Document-level RAG CRUD pinned to the selected project ledger.",
            DashboardTab.Memory when GetSelectedProject() is null => "Create, search, update, delete, and purge memory records across the shared store.",
            DashboardTab.Memory => "Memory CRUD pinned to the selected project without making session filtering the primary dashboard flow.",
            DashboardTab.Plans when GetSelectedProject() is null => "Plan creation, editing, task completion, delete, and purge flows across the workspace.",
            DashboardTab.Plans => "Project-first plan CRUD and purge flows for the active project.",
            _ => "Nebula operations"
        };
    }

    /// <summary>
    /// Gets the currently selected project node, if any.
    /// </summary>
    /// <returns>Selected project node or null.</returns>
    private ProjectDashboardNode? GetSelectedProject()
    {
        return string.IsNullOrWhiteSpace(_selectedProjectId)
            ? null
            : _projects.FirstOrDefault(project => string.Equals(project.ProjectId, _selectedProjectId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets the source count for the active context.
    /// </summary>
    /// <returns>Source count.</returns>
    private int GetFocusedSourceCount()
    {
        return GetSelectedProject()?.Rag.DocumentCount ?? _shellSnapshot?.Stats.DocumentCount ?? 0;
    }

    /// <summary>
    /// Gets the memory count for the active context.
    /// </summary>
    /// <returns>Memory count.</returns>
    private long GetFocusedMemoryCount()
    {
        return GetSelectedProject()?.Memory.MemoryCount ?? _shellSnapshot?.MemoryStats.TotalMemories ?? 0;
    }

    /// <summary>
    /// Gets the plan count for the active context.
    /// </summary>
    /// <returns>Plan count.</returns>
    private int GetFocusedPlanCount()
    {
        return GetSelectedProject()?.Plans.PlanCount ?? _projects.Sum(project => project.Plans.PlanCount);
    }

    /// <summary>
    /// Gets the task count for the active context.
    /// </summary>
    /// <returns>Task count.</returns>
    private int GetFocusedTaskCount()
    {
        return GetSelectedProject()?.Plans.TaskCount ?? _projects.Sum(project => project.Plans.TaskCount);
    }

    /// <summary>
    /// Gets the chunk count for the active context.
    /// </summary>
    /// <returns>Chunk count.</returns>
    private int GetFocusedChunkCount()
    {
        return GetSelectedProject()?.Rag.ChunkCount ?? _shellSnapshot?.Stats.ChunkCount ?? 0;
    }

    /// <summary>
    /// Clears the selected project if it no longer exists in refreshed data.
    /// </summary>
    private void ReconcileSelectedProject()
    {
        if (string.IsNullOrWhiteSpace(_selectedProjectId))
        {
            return;
        }

        var exists = _projects.Any(project => string.Equals(project.ProjectId, _selectedProjectId, StringComparison.OrdinalIgnoreCase));
        if (!exists)
        {
            _selectedProjectId = null;
        }
    }

    /// <summary>
    /// Returns the shell health label.
    /// </summary>
    /// <returns>Health label.</returns>
    private string GetHealthLabel()
    {
        if (_shellSnapshot is null)
        {
            return _refreshing ? "Refreshing" : "Unavailable";
        }

        return _shellSnapshot.Health.IsHealthy ? "Healthy" : "Degraded";
    }

    /// <summary>
    /// Returns the health CSS class variant.
    /// </summary>
    /// <returns>CSS class for the health chip.</returns>
    private string GetHealthChipClass()
    {
        return _shellSnapshot?.Health.IsHealthy == true ? "ok" : "warn";
    }

    /// <summary>
    /// Formats the latest shell generation time for display.
    /// </summary>
    /// <returns>Human-readable timestamp.</returns>
    private string FormatGeneratedAt()
    {
        if (_shellSnapshot is null)
        {
            return _refreshing ? "in progress" : "not loaded";
        }

        return _shellSnapshot.GeneratedAtUtc.ToLocalTime().ToString("MMM dd HH:mm:ss");
    }

    /// <summary>
    /// Formats byte counts into compact units.
    /// </summary>
    /// <param name="value">Byte count.</param>
    /// <returns>Formatted size text.</returns>
    private static string FormatBytes(long value)
    {
        if (value <= 0)
        {
            return "0 B";
        }

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var size = (double)value;
        var unitIndex = 0;
        while (size >= 1024d && unitIndex < units.Length - 1)
        {
            size /= 1024d;
            unitIndex++;
        }

        return $"{size:0.#} {units[unitIndex]}";
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
