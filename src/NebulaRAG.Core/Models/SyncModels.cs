namespace NebulaRAG.Core.Models;

/// <summary>
/// Result of a memory(action: "sync") call.
/// </summary>
/// <param name="FilesIngested">Number of auto-memory files ingested or re-ingested in this sync run.</param>
/// <param name="MemoriesPruned">Number of stale auto-memory entries removed during pruning.</param>
/// <param name="SourcesReindexed">Number of RAG sources reindexed due to content hash change.</param>
/// <param name="Errors">Non-fatal error messages accumulated during sync phases.</param>
/// <param name="DurationMs">Total elapsed time of the sync operation in milliseconds.</param>
public sealed record SyncSummary(
    int FilesIngested,
    int MemoriesPruned,
    int SourcesReindexed,
    IReadOnlyList<string> Errors,
    long DurationMs);

/// <summary>
/// Result of a nebula_setup install-hooks or uninstall-hooks call.
/// </summary>
/// <param name="Success">Whether the hook operation completed successfully.</param>
/// <param name="Client">Client identifier this operation was performed for (e.g. <c>claude</c>, <c>copilot</c>).</param>
/// <param name="Diff">JSON diff string showing what would change; populated only when <c>dry_run</c> is <c>true</c>.</param>
/// <param name="Message">Human-readable outcome description.</param>
public sealed record HookOperationResult(
    bool Success,
    string Client,
    string? Diff,
    string Message);

/// <summary>
/// Per-client status returned by nebula_setup status.
/// </summary>
/// <param name="Client">Client identifier (e.g. <c>claude</c>, <c>copilot</c>).</param>
/// <param name="SettingsFileExists">Whether the client settings file was found on disk.</param>
/// <param name="HookInstalled">Whether the Nebula Stop hook entry is present in the settings file.</param>
/// <param name="EndpointReachable">Whether the Nebula MCP HTTP endpoint responded to a health check.</param>
/// <param name="EndpointWarning">Warning message if the endpoint health check failed; <c>null</c> when reachable.</param>
public sealed record HookStatusResult(
    string Client,
    bool SettingsFileExists,
    bool HookInstalled,
    bool EndpointReachable,
    string? EndpointWarning);

/// <summary>
/// One row from nebula_sync_state.
/// </summary>
/// <param name="FilePath">Absolute path to the tracked file on disk.</param>
/// <param name="LastHash">SHA-256 hash of the file content at the time of the last sync.</param>
/// <param name="SyncedAtUtc">UTC timestamp of the last successful sync for this file.</param>
public sealed record SyncStateEntry(
    string FilePath,
    string LastHash,
    DateTimeOffset SyncedAtUtc);
