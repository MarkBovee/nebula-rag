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
    private readonly ILogger<RagApiController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RagApiController"/> class.
    /// </summary>
    /// <param name="queryService">Semantic query service.</param>
    /// <param name="managementService">Management service for stats and source operations.</param>
    /// <param name="indexer">Indexer service for source ingestion.</param>
    /// <param name="settings">Runtime settings.</param>
    /// <param name="dashboardSnapshotService">Dashboard snapshot orchestration service.</param>
    /// <param name="logger">Controller logger.</param>
    public RagApiController(RagQueryService queryService, RagManagementService managementService, RagIndexer indexer, RagSettings settings, DashboardSnapshotService dashboardSnapshotService, ILogger<RagApiController> logger)
    {
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
        _managementService = managementService ?? throw new ArgumentNullException(nameof(managementService));
        _indexer = indexer ?? throw new ArgumentNullException(nameof(indexer));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _dashboardSnapshotService = dashboardSnapshotService ?? throw new ArgumentNullException(nameof(dashboardSnapshotService));
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
    /// <param name="limit">Maximum number of sources included in snapshot.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Combined dashboard snapshot.</returns>
    [HttpGet("dashboard")]
    public async Task<ActionResult<DashboardSnapshotResponse>> GetDashboardAsync([FromQuery] int? limit, CancellationToken cancellationToken)
    {
        var snapshot = await _dashboardSnapshotService.GetDashboardAsync(limit ?? 50, cancellationToken);
        return Ok(snapshot);
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
        _dashboardSnapshotService.RecordQueryLatency(stopwatch.Elapsed.TotalMilliseconds);
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

        var summary = await _indexer.IndexDirectoryAsync(request.SourcePath, cancellationToken);
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

/// <summary>
/// Query API request model.
/// </summary>
/// <param name="Text">The semantic query text.</param>
/// <param name="Limit">Optional top-k result limit.</param>
public sealed record ApiQueryRequest(string Text, int? Limit);

/// <summary>
/// Query API response model.
/// </summary>
/// <param name="Query">Original query text.</param>
/// <param name="Limit">Effective top-k limit.</param>
/// <param name="Matches">Ranked result list.</param>
public sealed record QueryResponse(string Query, int Limit, IReadOnlyList<RagSearchResult> Matches);

/// <summary>
/// Index API request model.
/// </summary>
/// <param name="SourcePath">Directory path to index.</param>
public sealed record ApiIndexRequest(string SourcePath);

/// <summary>
/// Delete source API request model.
/// </summary>
/// <param name="SourcePath">Indexed source path to delete.</param>
public sealed record ApiDeleteRequest(string SourcePath);

/// <summary>
/// Purge API request model.
/// </summary>
/// <param name="ConfirmPhrase">Safety confirmation phrase.</param>
public sealed record ApiPurgeRequest(string ConfirmPhrase);

/// <summary>
/// Client error telemetry payload model.
/// </summary>
/// <param name="Message">Client error message.</param>
/// <param name="Stack">Optional stack trace.</param>
/// <param name="Source">Event source name.</param>
/// <param name="Url">Page URL where error occurred.</param>
/// <param name="UserAgent">Browser user agent string.</param>
/// <param name="Severity">Client severity hint.</param>
/// <param name="Timestamp">Client timestamp.</param>
public sealed record ApiClientErrorRequest(string Message, string? Stack, string? Source, string? Url, string? UserAgent, string? Severity, string? Timestamp);
