using NebulaRAG.Core.Models;
using TaskLifecycleStatus = NebulaRAG.Core.Models.TaskStatus;

namespace NebulaRAG.Core.Storage;

/// <summary>
/// Converts plan and task lifecycle statuses between domain enums and database text values.
/// </summary>
internal static class PlanStatusMapper
{
    /// <summary>
    /// Converts a plan status enum to the database text representation.
    /// </summary>
    /// <param name="status">The plan status to convert.</param>
    /// <returns>The database value used by PostgreSQL constraints and history rows.</returns>
    public static string ToDatabaseValue(PlanStatus status)
    {
        return status switch
        {
            PlanStatus.Draft => "draft",
            PlanStatus.Active => "active",
            PlanStatus.Completed => "completed",
            PlanStatus.Archived => "archived",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported plan status.")
        };
    }

    /// <summary>
    /// Converts a task status enum to the database text representation.
    /// </summary>
    /// <param name="status">The task status to convert.</param>
    /// <returns>The database value used by PostgreSQL constraints and history rows.</returns>
    public static string ToDatabaseValue(TaskLifecycleStatus status)
    {
        return status switch
        {
            TaskLifecycleStatus.Pending => "pending",
            TaskLifecycleStatus.InProgress => "in_progress",
            TaskLifecycleStatus.Completed => "completed",
            TaskLifecycleStatus.Failed => "failed",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported task status.")
        };
    }

    /// <summary>
    /// Parses a plan status value stored in PostgreSQL into the domain enum.
    /// </summary>
    /// <param name="value">The raw database value.</param>
    /// <returns>The parsed plan status.</returns>
    public static PlanStatus ParsePlanStatus(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "draft" => PlanStatus.Draft,
            "active" => PlanStatus.Active,
            "completed" => PlanStatus.Completed,
            "archived" => PlanStatus.Archived,
            _ => throw new FormatException($"Unsupported plan status value '{value}'.")
        };
    }

    /// <summary>
    /// Parses a task status value stored in PostgreSQL into the domain enum.
    /// </summary>
    /// <param name="value">The raw database value.</param>
    /// <returns>The parsed task status.</returns>
    public static TaskLifecycleStatus ParseTaskStatus(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "pending" => TaskLifecycleStatus.Pending,
            "in_progress" => TaskLifecycleStatus.InProgress,
            "inprogress" => TaskLifecycleStatus.InProgress,
            "completed" => TaskLifecycleStatus.Completed,
            "failed" => TaskLifecycleStatus.Failed,
            _ => throw new FormatException($"Unsupported task status value '{value}'.")
        };
    }
}