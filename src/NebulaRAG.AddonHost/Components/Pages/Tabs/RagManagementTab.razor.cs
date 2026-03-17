using System.Globalization;
using Microsoft.AspNetCore.Components;
using NebulaRAG.AddonHost.Services;
using NebulaRAG.Core.Models;

namespace NebulaRAG.AddonHost.Components.Pages.Tabs;

/// <summary>
/// Code-behind for RAG operations and source list interactions in the dashboard.
/// </summary>
public partial class RagManagementTab
{
    [Inject]
    private RagOperationsService RagOperationsService { get; set; } = default!;

    /// <summary>
    /// Gets or sets refresh nonce propagated by the parent dashboard.
    /// </summary>
    [Parameter]
    public long RefreshNonce { get; set; }

    /// <summary>
    /// Gets or sets the project filter inherited from the dashboard shell.
    /// </summary>
    [Parameter]
    public string? SelectedProjectId { get; set; }

    private readonly List<RagSearchResult> _queryResults = [];
    private readonly List<SourceInfo> _sources = [];
    private long _lastRefreshNonce = -1;
    private string? _lastSelectedProjectId;
    private bool _statusIsError;
    private bool _showPurgeConfirm;
    private bool _showDeleteSourceConfirm;
    private string? _statusMessage;
    private string _queryText = string.Empty;
    private int _queryLimit = 5;
    private string _indexPath = "/share";
    private string _deleteSourcePath = string.Empty;

    /// <summary>
    /// Synchronizes source list whenever the dashboard refresh nonce changes.
    /// </summary>
    /// <returns>Completion task.</returns>
    protected override async Task OnParametersSetAsync()
    {
        if (_lastRefreshNonce == RefreshNonce && string.Equals(_lastSelectedProjectId, SelectedProjectId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _lastRefreshNonce = RefreshNonce;
        _lastSelectedProjectId = SelectedProjectId;
        await RefreshSourcesAsync();
    }

    /// <summary>
    /// Executes semantic query from UI input.
    /// </summary>
    /// <returns>Completion task.</returns>
    private async Task RunQueryAsync()
    {
        if (string.IsNullOrWhiteSpace(_queryText))
        {
            SetStatus("Query text is required.", isError: true);
            return;
        }

        try
        {
            var matches = await RagOperationsService.QueryAsync(_queryText.Trim(), _queryLimit);
            _queryResults.Clear();
            _queryResults.AddRange(ApplyQueryProjectFilter(matches));
            SetStatus($"Query completed with {_queryResults.Count} matches.");
        }
        catch (Exception exception)
        {
            SetStatus($"Query failed: {exception.Message}", isError: true);
        }
    }

    /// <summary>
    /// Starts indexing for the user-provided source path.
    /// </summary>
    /// <returns>Completion task.</returns>
    private async Task IndexSourceAsync()
    {
        if (string.IsNullOrWhiteSpace(_indexPath))
        {
            SetStatus("Source path is required.", isError: true);
            return;
        }

        try
        {
            var summary = await RagOperationsService.IndexPathAsync(_indexPath.Trim());
            SetStatus($"Index finished. Docs: {summary.DocumentsIndexed}, Chunks: {summary.ChunksIndexed}.");
            await RefreshSourcesAsync();
        }
        catch (Exception exception)
        {
            SetStatus($"Index failed: {exception.Message}", isError: true);
        }
    }

    /// <summary>
    /// Opens source delete confirmation after validating source input.
    /// </summary>
    private void OpenDeleteSourceConfirm()
    {
        if (string.IsNullOrWhiteSpace(_deleteSourcePath))
        {
            SetStatus("Source path to delete is required.", isError: true);
            return;
        }

        _showDeleteSourceConfirm = true;
    }

    /// <summary>
    /// Opens full-purge confirmation modal.
    /// </summary>
    private void OpenPurgeConfirm()
    {
        _showPurgeConfirm = true;
    }

    /// <summary>
    /// Confirms source delete and refreshes source view.
    /// </summary>
    /// <returns>Completion task.</returns>
    private async Task ConfirmDeleteSourceAsync()
    {
        try
        {
            var deleted = await RagOperationsService.DeleteSourceAsync(_deleteSourcePath.Trim());
            SetStatus($"Deleted {deleted} source rows.");
            await RefreshSourcesAsync();
        }
        catch (Exception exception)
        {
            SetStatus($"Delete source failed: {exception.Message}", isError: true);
        }
        finally
        {
            CloseAllConfirm();
        }
    }

    /// <summary>
    /// Confirms full purge and refreshes source view.
    /// </summary>
    /// <returns>Completion task.</returns>
    private async Task ConfirmPurgeAsync()
    {
        try
        {
            await RagOperationsService.PurgeAllAsync();
            SetStatus("Entire index purged.");
            await RefreshSourcesAsync();
        }
        catch (Exception exception)
        {
            SetStatus($"Purge failed: {exception.Message}", isError: true);
        }
        finally
        {
            CloseAllConfirm();
        }
    }

    /// <summary>
    /// Closes all active confirmation dialogs.
    /// </summary>
    private void CloseAllConfirm()
    {
        _showPurgeConfirm = false;
        _showDeleteSourceConfirm = false;
    }

    /// <summary>
    /// Updates user-facing status banner content.
    /// </summary>
    /// <param name="message">Status text to display.</param>
    /// <param name="isError">Whether status represents an error state.</param>
    private void SetStatus(string message, bool isError = false)
    {
        _statusMessage = message;
        _statusIsError = isError;
    }

    /// <summary>
    /// Refreshes source table from backend dashboard data.
    /// </summary>
    /// <returns>Completion task.</returns>
    private async Task RefreshSourcesAsync()
    {
        var sources = await RagOperationsService.ListSourcesAsync(limit: 300);
        _sources.Clear();
        _sources.AddRange(FilterSourcesForProject(sources));
    }

    /// <summary>
    /// Returns whether the tab is scoped to one selected project.
    /// </summary>
    /// <returns>True when a project filter is active.</returns>
    private bool HasSelectedProject()
    {
        return !string.IsNullOrWhiteSpace(SelectedProjectId);
    }

    /// <summary>
    /// Gets the scope heading shown above the RAG controls.
    /// </summary>
    /// <returns>Scope heading text.</returns>
    private string GetScopeHeading()
    {
        return HasSelectedProject() ? $"RAG ledger for {SelectedProjectId}" : "RAG ledger for all projects";
    }

    /// <summary>
    /// Gets the scope description shown above the RAG controls.
    /// </summary>
    /// <returns>Scope description text.</returns>
    private string GetScopeDescription()
    {
        return HasSelectedProject()
            ? "Query results and source CRUD stay aligned to the selected project wherever the underlying model supports it."
            : "Use the shell project switcher to narrow source CRUD to one project without changing the RAG toolset.";
    }

    /// <summary>
    /// Filters visible sources for the selected project.
    /// </summary>
    /// <param name="sources">Unfiltered source list.</param>
    /// <returns>Filtered source list.</returns>
    private IReadOnlyList<SourceInfo> FilterSourcesForProject(IReadOnlyList<SourceInfo> sources)
    {
        return HasSelectedProject()
            ? sources.Where(source => string.Equals(source.ProjectId, SelectedProjectId, StringComparison.OrdinalIgnoreCase)).ToList()
            : sources;
    }

    /// <summary>
    /// Filters semantic matches to the selected project by source membership.
    /// </summary>
    /// <param name="matches">Unfiltered semantic matches.</param>
    /// <returns>Filtered semantic matches.</returns>
    private IReadOnlyList<RagSearchResult> ApplyQueryProjectFilter(IReadOnlyList<RagSearchResult> matches)
    {
        if (!HasSelectedProject())
        {
            return matches;
        }

        var allowedSources = _sources.Select(source => source.SourcePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return matches.Where(match => allowedSources.Contains(match.SourcePath)).ToList();
    }

    /// <summary>
    /// Uses a source path as the next query text for targeted searches.
    /// </summary>
    /// <param name="sourcePath">Source path selected from the source ledger.</param>
    private void UseSourceForQuery(string sourcePath)
    {
        _queryText = sourcePath;
        SetStatus($"Loaded source path into query box: {sourcePath}");
    }

    /// <summary>
    /// Starts delete confirmation using a source selected from the table.
    /// </summary>
    /// <param name="sourcePath">Source path chosen for deletion.</param>
    private void ConfirmDeleteFromRow(string sourcePath)
    {
        _deleteSourcePath = sourcePath;
        _showDeleteSourceConfirm = true;
    }

    /// <summary>
    /// Gets the latest indexed timestamp across visible sources.
    /// </summary>
    /// <returns>Formatted latest index timestamp.</returns>
    private string GetLatestIndexTime()
    {
        if (_sources.Count == 0)
        {
            return "n/a";
        }

        return _sources.MaxBy(source => source.IndexedAt)?.IndexedAt.ToLocalTime().ToString("MMM dd HH:mm", CultureInfo.InvariantCulture) ?? "n/a";
    }

    /// <summary>
    /// Trims text previews for table display.
    /// </summary>
    /// <param name="value">Source text.</param>
    /// <param name="maxLength">Max output length.</param>
    /// <returns>Trimmed or original text.</returns>
    private static string TrimText(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length <= maxLength)
        {
            return value;
        }

        return $"{value[..maxLength]}...";
    }
}
