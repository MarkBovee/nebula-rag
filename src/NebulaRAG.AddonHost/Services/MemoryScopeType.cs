namespace NebulaRAG.AddonHost.Services;

/// <summary>
/// Supported memory scope values for API filtering.
/// </summary>
public static class MemoryScopeType
{
    /// <summary>Global unfiltered scope.</summary>
    public const string Global = "global";

    /// <summary>Project-specific scope.</summary>
    public const string Project = "project";

    /// <summary>Session-specific scope.</summary>
    public const string Session = "session";
}
