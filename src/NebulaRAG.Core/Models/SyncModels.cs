namespace NebulaRAG.Core.Models;

/// <summary>Result of a memory(action: "sync") call.</summary>
public sealed record SyncSummary(
    int FilesIngested,
    int MemoriesPruned,
    int SourcesReindexed,
    IReadOnlyList<string> Errors,
    long DurationMs);

/// <summary>Result of a nebula_setup install-hooks or uninstall-hooks call.</summary>
public sealed record HookOperationResult(
    bool Success,
    string Client,
    string? Diff,
    string Message);

/// <summary>Per-client status returned by nebula_setup status.</summary>
public sealed record HookStatusResult(
    string Client,
    bool SettingsFileExists,
    bool HookInstalled,
    bool EndpointReachable,
    string? EndpointWarning);

/// <summary>One row from nebula_sync_state.</summary>
public sealed record SyncStateEntry(
    string FilePath,
    string LastHash,
    DateTimeOffset SyncedAt);
