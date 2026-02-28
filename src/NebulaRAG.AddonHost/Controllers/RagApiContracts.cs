using System.Text.Json.Serialization;
using NebulaRAG.Core.Models;

namespace NebulaRAG.AddonHost.Controllers;

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
/// Memory semantic search API request model.
/// </summary>
/// <param name="Text">Memory search text.</param>
/// <param name="Limit">Optional max number of matches to return.</param>
/// <param name="Scope">Optional scope value: global, project, or session.</param>
/// <param name="SessionId">Optional session-id filter.</param>
/// <param name="ProjectId">Optional project-id filter.</param>
/// <param name="Type">Optional memory type filter.</param>
/// <param name="Tag">Optional memory tag filter.</param>
public sealed record ApiMemorySearchRequest(
    string Text,
    int? Limit,
    [property: JsonPropertyName("scope")] string? Scope,
    [property: JsonPropertyName("sessionId")] string? SessionId,
    [property: JsonPropertyName("projectId")] string? ProjectId,
    string? Type,
    string? Tag);

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
