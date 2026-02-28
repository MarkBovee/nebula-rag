using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using NebulaRAG.AddonHost.Services;
using NebulaRAG.Core.Configuration;
using NebulaRAG.Core.Models;
using NebulaRAG.Core.Services;

namespace NebulaRAG.AddonHost.Controllers;

/// <summary>
/// Dedicated REST API controller for dashboard and management endpoints.
/// </summary>
[ApiController]
[Route("api")]
public sealed class RagApiController : ControllerBase
{
    private readonly RagQueryService _queryService;
    private readonly RagManagementService _managementService;
    private readonly RagIndexer _indexer;
    private readonly RagSettings _settings;
    private readonly DashboardSnapshotService _dashboardSnapshotService;
    private readonly MemoryScopeResolver _memoryScopeResolver;
    private readonly IRuntimeTelemetrySink _telemetrySink;
    private readonly ILogger<RagApiController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RagApiController"/> class.
    /// </summary>
    /// <param name="queryService">Semantic query service.</param>
    /// <param name="managementService">Management service for stats and source operations.</param>
    /// <param name="indexer">Indexer service for source ingestion.</param>
    /// <param name="settings">Runtime settings.</param>
    /// <param name="dashboardSnapshotService">Dashboard snapshot orchestration service.</param>
    /// <param name="memoryScopeResolver">Scope resolver for memory APIs.</param>
    /// <param name="logger">Controller logger.</param>
    public RagApiController(RagQueryService queryService, RagManagementService managementService, RagIndexer indexer, RagSettings settings, DashboardSnapshotService dashboardSnapshotService, MemoryScopeResolver memoryScopeResolver, IRuntimeTelemetrySink telemetrySink, ILogger<RagApiController> logger)
    {
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
        _managementService = managementService ?? throw new ArgumentNullException(nameof(managementService));
        _indexer = indexer ?? throw new ArgumentNullException(nameof(indexer));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _dashboardSnapshotService = dashboardSnapshotService ?? throw new ArgumentNullException(nameof(dashboardSnapshotService));
        _memoryScopeResolver = memoryScopeResolver ?? throw new ArgumentNullException(nameof(memoryScopeResolver));
        _telemetrySink = telemetrySink ?? throw new ArgumentNullException(nameof(telemetrySink));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Returns backend health status.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Health status payload.</returns>
    [HttpGet("health")]
    public async Task<ActionResult<HealthCheckResult>> GetHealthAsync(CancellationToken cancellationToken)
    {
        var health = await _dashboardSnapshotService.GetHealthAsync(cancellationToken);
        return Ok(health);
    }

    /// <summary>
    /// Returns index statistics.
    /// </summary>
    /// <param name="includeSize">When true, includes index-size computation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Index stats payload.</returns>
    [HttpGet("stats")]
    public async Task<ActionResult<IndexStats>> GetStatsAsync([FromQuery] bool? includeSize, CancellationToken cancellationToken)
    {
        var stats = await _dashboardSnapshotService.GetStatsAsync(includeSize == true, cancellationToken);
        return Ok(stats);
    }

    /// <summary>
    /// Returns aggregated memory analytics.
    /// </summary>
    /// <param name="scope">Optional memory scope: global, project, or session.</param>
    /// <param name="projectId">Optional project-id filter.</param>
    /// <param name="sessionId">Optional session-id filter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Memory stats payload.</returns>
    [HttpGet("memory/stats")]
    public async Task<ActionResult<MemoryDashboardStats>> GetMemoryStatsAsync([FromQuery] string? scope, [FromQuery] string? projectId, [FromQuery] string? sessionId, CancellationToken cancellationToken)
    {
        var resolvedScope = _memoryScopeResolver.Resolve(scope, sessionId, projectId);
        if (!resolvedScope.IsSuccess)
        {
            return BadRequest(new { error = resolvedScope.Error });
        }

        // Keep cache behavior for the common global view while allowing explicit scoped views.
        var memoryStats = string.IsNullOrWhiteSpace(resolvedScope.SessionId) && string.IsNullOrWhiteSpace(resolvedScope.ProjectId)
            ? await _dashboardSnapshotService.GetMemoryStatsAsync(cancellationToken)
            : await _managementService.GetMemoryStatsAsync(sessionId: resolvedScope.SessionId, projectId: resolvedScope.ProjectId, cancellationToken: cancellationToken);

        return Ok(memoryStats);
    }

    /// <summary>
    /// Lists memories with optional filters and scope.
    /// </summary>
    /// <param name="scope">Optional memory scope: global, project, or session.</param>
    /// <param name="projectId">Optional project-id filter.</param>
    /// <param name="sessionId">Optional session-id filter.</param>
    /// <param name="type">Optional memory type filter.</param>
    /// <param name="tag">Optional tag filter.</param>
    /// <param name="limit">Maximum number of rows to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Memory rows ordered by most recent first.</returns>
    [HttpGet("memory/list")]
    public async Task<ActionResult<IReadOnlyList<MemoryRecord>>> ListMemoriesAsync([FromQuery] string? scope, [FromQuery] string? projectId, [FromQuery] string? sessionId, [FromQuery] string? type, [FromQuery] string? tag, [FromQuery] int? limit, CancellationToken cancellationToken)
    {
        var resolvedScope = _memoryScopeResolver.Resolve(scope, sessionId, projectId);
        if (!resolvedScope.IsSuccess)
        {
            return BadRequest(new { error = resolvedScope.Error });
        }

        var memories = await _managementService.ListMemoriesAsync(
            limit: Math.Clamp(limit ?? 100, 1, 500),
            type: type,
            tag: tag,
            sessionId: resolvedScope.SessionId,
            projectId: resolvedScope.ProjectId,
            cancellationToken: cancellationToken);

        return Ok(memories);
    }

    /// <summary>
    /// Performs semantic search over stored memories with optional filters and scope.
    /// </summary>
    /// <param name="request">Memory search request payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Semantically ranked memory matches.</returns>
    [HttpPost("memory/search")]
    public async Task<ActionResult<IReadOnlyList<MemorySearchResult>>> SearchMemoriesAsync([FromBody] ApiMemorySearchRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return BadRequest(new { error = "text is required" });
        }

        var resolvedScope = _memoryScopeResolver.Resolve(request.Scope, request.SessionId, request.ProjectId);
        if (!resolvedScope.IsSuccess)
        {
            return BadRequest(new { error = resolvedScope.Error });
        }

        var matches = await _managementService.SearchMemoriesAsync(
            text: request.Text,
            limit: Math.Clamp(request.Limit ?? 20, 1, 100),
            type: request.Type,
            tag: request.Tag,
            sessionId: resolvedScope.SessionId,
            projectId: resolvedScope.ProjectId,
            cancellationToken: cancellationToken);

        _telemetrySink.RecordActivity("query", "API memory search", new Dictionary<string, string?>
        {
            ["matches"] = matches.Count.ToString(),
            ["scope"] = request.Scope ?? "global"
        });

        return Ok(matches);
    }

    /// <summary>
    /// Lists indexed sources.
    /// </summary>
    /// <param name="limit">Maximum number of sources.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Source list ordered by most recently indexed.</returns>
    [HttpGet("sources")]
    public async Task<ActionResult<IReadOnlyList<SourceInfo>>> GetSourcesAsync([FromQuery] int? limit, CancellationToken cancellationToken)
    {
        var sources = await _managementService.ListSourcesAsync(Math.Clamp(limit ?? 100, 1, 500), cancellationToken);
        return Ok(sources);
    }

    /// <summary>
    /// Returns an aggregated dashboard payload in one request.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Combined dashboard snapshot.</returns>
    [HttpGet("dashboard")]
    public async Task<ActionResult<DashboardSnapshotResponse>> GetDashboardAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _dashboardSnapshotService.GetDashboardAsync(cancellationToken);
        return Ok(snapshot);
    }

    /// <summary>
    /// Returns project-first dashboard hierarchy: projects -> plans/rag/memory.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Project hierarchy payload.</returns>
    [HttpGet("dashboard/projects")]
    public async Task<ActionResult<IReadOnlyList<ProjectDashboardNode>>> GetDashboardProjectsAsync(CancellationToken cancellationToken)
    {
        var hierarchy = await _dashboardSnapshotService.GetProjectHierarchyAsync(cancellationToken);
        return Ok(hierarchy);
    }

    /// <summary>
    /// Executes semantic search.
    /// </summary>
    /// <param name="request">Query request payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Query results.</returns>
    [HttpPost("query")]
    public async Task<ActionResult<QueryResponse>> QueryAsync([FromBody] ApiQueryRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return BadRequest(new { error = "text is required" });
        }

        var stopwatch = Stopwatch.StartNew();
        var topK = Math.Clamp(request.Limit ?? _settings.Retrieval.DefaultTopK, 1, 20);
        var results = await _queryService.QueryAsync(request.Text, topK, cancellationToken);
        stopwatch.Stop();
        _telemetrySink.RecordQueryLatency(stopwatch.Elapsed.TotalMilliseconds);
        _telemetrySink.RecordActivity("query", $"API query: '{request.Text}' ({results.Count} matches)", new Dictionary<string, string?>
        {
            ["topK"] = topK.ToString(),
            ["durationMs"] = stopwatch.Elapsed.TotalMilliseconds.ToString("F1")
        });
        return Ok(new QueryResponse(request.Text, topK, results));
    }

    /// <summary>
    /// Starts indexing for a source path.
    /// </summary>
    /// <param name="request">Index request payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Indexing summary.</returns>
    [HttpPost("index")]
    public async Task<ActionResult<IndexSummary>> IndexAsync([FromBody] ApiIndexRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.SourcePath))
        {
            return BadRequest(new { error = "sourcePath is required" });
        }

        var stopwatch = Stopwatch.StartNew();
        var summary = await _indexer.IndexDirectoryAsync(request.SourcePath, cancellationToken: cancellationToken);
        stopwatch.Stop();

        if (summary.DocumentsIndexed > 0 && stopwatch.Elapsed.TotalSeconds > 0)
        {
            _telemetrySink.RecordIndexingRate(summary.DocumentsIndexed / stopwatch.Elapsed.TotalSeconds);
        }

        _telemetrySink.RecordActivity("index", $"API index: {request.SourcePath}", new Dictionary<string, string?>
        {
            ["documentsIndexed"] = summary.DocumentsIndexed.ToString(),
            ["chunksIndexed"] = summary.ChunksIndexed.ToString(),
            ["durationMs"] = stopwatch.Elapsed.TotalMilliseconds.ToString("F1")
        });

        return Ok(summary);
    }

    /// <summary>
    /// Deletes one indexed source.
    /// </summary>
    /// <param name="request">Delete request payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Deleted row count wrapper.</returns>
    [HttpPost("source/delete")]
    public async Task<ActionResult<object>> DeleteSourceAsync([FromBody] ApiDeleteRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.SourcePath))
        {
            return BadRequest(new { error = "sourcePath is required" });
        }

        var deleted = await _managementService.DeleteSourceAsync(request.SourcePath, cancellationToken);
        _telemetrySink.RecordActivity("delete", $"API delete: {request.SourcePath}", new Dictionary<string, string?>
        {
            ["deleted"] = deleted.ToString()
        });
        return Ok(new { deleted });
    }

    /// <summary>
    /// Purges all indexed content.
    /// </summary>
    /// <param name="request">Purge request payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Purge status response.</returns>
    [HttpPost("purge")]
    public async Task<ActionResult<object>> PurgeAsync([FromBody] ApiPurgeRequest request, CancellationToken cancellationToken)
    {
        if (!string.Equals(request.ConfirmPhrase, "PURGE ALL", StringComparison.Ordinal))
        {
            return BadRequest(new { error = "confirmPhrase must be PURGE ALL" });
        }

        await _managementService.PurgeAllAsync(cancellationToken);
        _telemetrySink.RecordActivity("delete", "API purge all indexed content");
        return Ok(new { purged = true });
    }

    /// <summary>
    /// Receives browser-side runtime error reports.
    /// </summary>
    /// <param name="request">Client error payload.</param>
    /// <returns>Accepted response when payload is valid.</returns>
    [HttpPost("client-errors")]
    public ActionResult ReportClientError([FromBody] ApiClientErrorRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return BadRequest(new { error = "message is required" });
        }

        var severity = string.IsNullOrWhiteSpace(request.Severity) ? "error" : request.Severity;
        var message = TruncateForLogs(request.Message, 400);
        var source = TruncateForLogs(request.Source, 200);
        var url = TruncateForLogs(request.Url, 400);
        var stack = TruncateForLogs(request.Stack, 1500);

        _logger.LogWarning("Client-side nebula flare [{Severity}] {Message} | source={Source} | url={Url} | ts={ClientTimestamp}", severity, message, source, url, request.Timestamp);
        if (!string.IsNullOrWhiteSpace(stack))
        {
            _logger.LogWarning("Client stack trace: {ClientStack}", stack);
        }

        return Accepted();
    }

    /// <summary>
    /// Truncates log payloads to keep event lines compact.
    /// </summary>
    /// <param name="value">Input text value.</param>
    /// <param name="maxLength">Maximum length.</param>
    /// <returns>Truncated or original text.</returns>
    private static string TruncateForLogs(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Length <= maxLength ? value : $"{value[..maxLength]}...";
    }

}
