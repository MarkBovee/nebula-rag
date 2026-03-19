using System.Globalization;
using Microsoft.AspNetCore.Components;
using NebulaRAG.AddonHost.Services;
using NebulaRAG.Core.Models;

namespace NebulaRAG.AddonHost.Components.Pages.Tabs;

/// <summary>
/// Code-behind for project-scoped indexed-document management in the dashboard.
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

    /// <summary>
    /// Gets or sets the callback used to refresh the parent shell after data mutations.
    /// </summary>
    [Parameter]
    public EventCallback OnDataChanged { get; set; }

    private readonly List<IndexedDocumentRecord> _documents = [];
    private long _lastRefreshNonce = -1;
    private string? _lastSelectedProjectId;
    private bool _statusIsError;
    private bool _showPurgeProjectConfirm;
    private bool _showDeleteDocumentConfirm;
    private string? _statusMessage;
    private string _projectFilter = string.Empty;
    private string _documentSearchText = string.Empty;
    private string _indexPath = "/share";
    private string _editSourcePath = string.Empty;
    private string _editProjectId = string.Empty;
    private string _editContent = string.Empty;
    private string _pendingDeleteSourcePath = string.Empty;

    /// <summary>
    /// Synchronizes document list whenever the dashboard refresh nonce changes.
    /// </summary>
    protected override async Task OnParametersSetAsync()
    {
        if (_lastRefreshNonce == RefreshNonce && string.Equals(_lastSelectedProjectId, SelectedProjectId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _lastRefreshNonce = RefreshNonce;
        _lastSelectedProjectId = SelectedProjectId;
        ApplyProjectSelectionDefaults();
        await RefreshDocumentsAsync();
    }

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
            SetStatus($"Index finished. Docs: {summary.DocumentsIndexed}, chunks: {summary.ChunksIndexed}.");
            await NotifyDataChangedAsync();
        }
        catch (Exception exception)
        {
            SetStatus($"Index failed: {exception.Message}", isError: true);
        }
    }

    private async Task RefreshDocumentsAsync()
    {
        try
        {
            var rows = await RagOperationsService.ListDocumentsAsync(GetActiveProjectFilter(), NormalizeOptional(_documentSearchText), 300);
            _documents.Clear();
            _documents.AddRange(rows);
            SetStatus($"Loaded {_documents.Count} indexed documents.");
        }
        catch (Exception exception)
        {
            SetStatus($"Document refresh failed: {exception.Message}", isError: true);
        }
    }

    private async Task LoadSelectedDocumentAsync()
    {
        if (string.IsNullOrWhiteSpace(_editSourcePath))
        {
            SetStatus("Source path is required to load a document.", isError: true);
            return;
        }

        await LoadDocumentIntoEditorAsync(_editSourcePath);
    }

    private async Task LoadDocumentIntoEditorAsync(string sourcePath)
    {
        try
        {
            var document = await RagOperationsService.GetDocumentAsync(sourcePath.Trim());
            if (document is null)
            {
                SetStatus($"Indexed document not found: {sourcePath}", isError: true);
                return;
            }

            _editSourcePath = document.SourcePath;
            _editProjectId = document.ProjectId ?? string.Empty;
            _editContent = document.Content;
            SetStatus($"Loaded indexed document {_editSourcePath}.");
        }
        catch (Exception exception)
        {
            SetStatus($"Load document failed: {exception.Message}", isError: true);
        }
    }

    private async Task SaveDocumentAsync()
    {
        if (string.IsNullOrWhiteSpace(_editSourcePath) || string.IsNullOrWhiteSpace(_editContent))
        {
            SetStatus("Source path and content are required to save a document.", isError: true);
            return;
        }

        try
        {
            var updated = await RagOperationsService.UpdateDocumentAsync(_editSourcePath.Trim(), NormalizeOptional(_editProjectId), _editContent.Trim());
            _editSourcePath = updated.SourcePath;
            _editProjectId = updated.ProjectId ?? string.Empty;
            _editContent = updated.Content;
            SetStatus($"Updated indexed document {updated.SourcePath}.");
            await NotifyDataChangedAsync();
        }
        catch (Exception exception)
        {
            SetStatus($"Save document failed: {exception.Message}", isError: true);
        }
    }

    private void OpenDeleteDocumentConfirm()
    {
        if (string.IsNullOrWhiteSpace(_editSourcePath))
        {
            SetStatus("Load a document before deleting it.", isError: true);
            return;
        }

        _pendingDeleteSourcePath = _editSourcePath;
        _showDeleteDocumentConfirm = true;
    }

    private void OpenDeleteDocumentConfirm(string sourcePath)
    {
        _pendingDeleteSourcePath = sourcePath;
        _showDeleteDocumentConfirm = true;
    }

    private void OpenPurgeProjectConfirm()
    {
        if (string.IsNullOrWhiteSpace(GetActiveProjectFilter()))
        {
            SetStatus("Select or enter a project id before purging documents.", isError: true);
            return;
        }

        _showPurgeProjectConfirm = true;
    }

    private async Task ConfirmDeleteDocumentAsync()
    {
        try
        {
            var deleted = await RagOperationsService.DeleteSourceAsync(_pendingDeleteSourcePath.Trim());
            if (string.Equals(_editSourcePath, _pendingDeleteSourcePath, StringComparison.OrdinalIgnoreCase))
            {
                ResetDocumentEditor();
            }

            SetStatus($"Deleted {deleted} indexed document row(s).");
            await NotifyDataChangedAsync();
        }
        catch (Exception exception)
        {
            SetStatus($"Delete document failed: {exception.Message}", isError: true);
        }
        finally
        {
            CloseAllConfirm();
        }
    }

    private async Task ConfirmPurgeProjectAsync()
    {
        try
        {
            var projectId = GetActiveProjectFilter();
            if (string.IsNullOrWhiteSpace(projectId))
            {
                SetStatus("Project id is required for purge.", isError: true);
                return;
            }

            var deleted = await RagOperationsService.DeleteProjectDocumentsAsync(projectId);
            ResetDocumentEditor();
            SetStatus($"Deleted {deleted} indexed documents from {projectId}.");
            await NotifyDataChangedAsync();
        }
        catch (Exception exception)
        {
            SetStatus($"Project document purge failed: {exception.Message}", isError: true);
        }
        finally
        {
            CloseAllConfirm();
        }
    }

    private void CloseAllConfirm()
    {
        _showPurgeProjectConfirm = false;
        _showDeleteDocumentConfirm = false;
        _pendingDeleteSourcePath = string.Empty;
    }

    private async Task NotifyDataChangedAsync()
    {
        await RefreshDocumentsAsync();
        if (OnDataChanged.HasDelegate)
        {
            await OnDataChanged.InvokeAsync();
        }
    }

    private void SetStatus(string message, bool isError = false)
    {
        _statusMessage = message;
        _statusIsError = isError;
    }

    private bool HasSelectedProject()
    {
        return !string.IsNullOrWhiteSpace(GetActiveProjectFilter());
    }

    private string GetScopeHeading()
    {
        return HasSelectedProject() ? $"RAG documents for {GetActiveProjectFilter()}" : "RAG documents for all projects";
    }

    private string GetScopeDescription()
    {
        return HasSelectedProject()
            ? "The selected project owns document search, view, edit, delete, and purge actions on this screen."
            : "Select a project from the shell to pin the document ledger, or browse the full index when you need a global view.";
    }

    private string GetLatestIndexTime()
    {
        if (_documents.Count == 0)
        {
            return "n/a";
        }

        return _documents.MaxBy(document => document.IndexedAt)?.IndexedAt.ToLocalTime().ToString("MMM dd HH:mm", CultureInfo.InvariantCulture) ?? "n/a";
    }

    private void ApplyProjectSelectionDefaults()
    {
        if (string.IsNullOrWhiteSpace(SelectedProjectId))
        {
            return;
        }

        _projectFilter = SelectedProjectId;
        if (string.IsNullOrWhiteSpace(_editProjectId))
        {
            _editProjectId = SelectedProjectId;
        }
    }

    private string? GetActiveProjectFilter()
    {
        return NormalizeOptional(_projectFilter) ?? NormalizeOptional(SelectedProjectId);
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private void ResetDocumentEditor()
    {
        _editSourcePath = string.Empty;
        _editProjectId = string.Empty;
        _editContent = string.Empty;
    }

    private static string TrimText(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length <= maxLength)
        {
            return value;
        }

        return $"{value[..maxLength]}...";
    }
}