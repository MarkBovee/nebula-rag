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

    private readonly List<RagSearchResult> _queryResults = [];
    private readonly List<SourceInfo> _sources = [];
    private long _lastRefreshNonce = -1;
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
        if (_lastRefreshNonce == RefreshNonce)
        {
            return;
        }

        _lastRefreshNonce = RefreshNonce;
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
            _queryResults.AddRange(matches);
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
        _sources.AddRange(sources);
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
