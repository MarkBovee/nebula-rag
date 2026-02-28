namespace NebulaRAG.AddonHost.Services;

/// <summary>
/// Resolves API memory scope values into normalized project/session filters.
/// </summary>
public sealed class MemoryScopeResolver
{
    /// <summary>
    /// Resolves memory scope input into a normalized filter.
    /// </summary>
    /// <param name="scope">Optional scope value.</param>
    /// <param name="sessionId">Optional session-id input.</param>
    /// <param name="projectId">Optional project-id input.</param>
    /// <returns>Resolved filter with validation result and normalized values.</returns>
    public MemoryScopeResolution Resolve(string? scope, string? sessionId, string? projectId)
    {
        var resolvedSessionId = string.IsNullOrWhiteSpace(sessionId) ? null : sessionId.Trim();
        var resolvedProjectId = string.IsNullOrWhiteSpace(projectId) ? null : projectId.Trim();

        var normalizedScope = string.IsNullOrWhiteSpace(scope)
            ? MemoryScopeType.Global
            : scope.Trim().ToLowerInvariant();

        return normalizedScope switch
        {
            MemoryScopeType.Global => MemoryScopeResolution.Success(resolvedSessionId, resolvedProjectId),
            MemoryScopeType.Project => ResolveProjectScope(resolvedSessionId, resolvedProjectId),
            MemoryScopeType.Session => ResolveSessionScope(resolvedSessionId, resolvedProjectId),
            _ => MemoryScopeResolution.Failure("scope must be one of: global, project, session.")
        };
    }

    /// <summary>
    /// Validates and resolves project scope input.
    /// </summary>
    /// <param name="resolvedSessionId">Normalized session-id value.</param>
    /// <param name="resolvedProjectId">Normalized project-id value.</param>
    /// <returns>Validated resolution result.</returns>
    private static MemoryScopeResolution ResolveProjectScope(string? resolvedSessionId, string? resolvedProjectId)
    {
        if (string.IsNullOrWhiteSpace(resolvedProjectId))
        {
            return MemoryScopeResolution.Failure("projectId is required when scope=project.");
        }

        return MemoryScopeResolution.Success(sessionId: null, projectId: resolvedProjectId);
    }

    /// <summary>
    /// Validates and resolves session scope input.
    /// </summary>
    /// <param name="resolvedSessionId">Normalized session-id value.</param>
    /// <param name="resolvedProjectId">Normalized project-id value.</param>
    /// <returns>Validated resolution result.</returns>
    private static MemoryScopeResolution ResolveSessionScope(string? resolvedSessionId, string? resolvedProjectId)
    {
        if (string.IsNullOrWhiteSpace(resolvedSessionId))
        {
            return MemoryScopeResolution.Failure("sessionId is required when scope=session.");
        }

        return MemoryScopeResolution.Success(sessionId: resolvedSessionId, projectId: null);
    }
}

/// <summary>
/// Represents the output of memory scope resolution.
/// </summary>
/// <param name="IsSuccess">Whether input was valid.</param>
/// <param name="SessionId">Resolved session-id.</param>
/// <param name="ProjectId">Resolved project-id.</param>
/// <param name="Error">Validation error when resolution fails.</param>
public sealed record MemoryScopeResolution(bool IsSuccess, string? SessionId, string? ProjectId, string? Error)
{
    /// <summary>
    /// Creates a successful resolution.
    /// </summary>
    /// <param name="sessionId">Resolved session-id value.</param>
    /// <param name="projectId">Resolved project-id value.</param>
    /// <returns>Successful scope resolution result.</returns>
    public static MemoryScopeResolution Success(string? sessionId, string? projectId)
    {
        return new MemoryScopeResolution(true, sessionId, projectId, null);
    }

    /// <summary>
    /// Creates a failed resolution.
    /// </summary>
    /// <param name="error">Validation error text.</param>
    /// <returns>Failed scope resolution result.</returns>
    public static MemoryScopeResolution Failure(string error)
    {
        return new MemoryScopeResolution(false, null, null, error);
    }
}
