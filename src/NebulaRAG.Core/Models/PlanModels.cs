using System.Text.Json;

namespace NebulaRAG.Core.Models;

/// <summary>
/// Represents the lifecycle status of a plan.
/// </summary>
public enum PlanStatus
{
    /// <summary>Plan is being created and not yet active.</summary>
    Draft,

    /// <summary>Plan is currently being executed.</summary>
    Active,

    /// <summary>Plan has been successfully completed.</summary>
    Completed,

    /// <summary>Plan has been archived for historical reference.</summary>
    Archived
}

/// <summary>
/// Represents the execution status of a task within a plan.
/// </summary>
public enum TaskStatus
{
    /// <summary>Task is created but not yet started.</summary>
    Pending,

    /// <summary>Task is currently being worked on.</summary>
    InProgress,

    /// <summary>Task has been successfully completed.</summary>
    Completed,

    /// <summary>Task failed to complete.</summary>
    Failed
}

/// <summary>
/// Represents a stored plan record with metadata.
/// Plans are organized by project and session identifiers for multi-tenant support.
/// </summary>
/// <param name="Id">Database identifier of the plan.</param>
/// <param name="ProjectId">Project identifier associated with the plan.</param>
/// <param name="SessionId">Session identifier for session-scoped plan isolation.</param>
/// <param name="Name">Human-readable plan name.</param>
/// <param name="Description">Optional detailed description of the plan.</param>
/// <param name="Status">Current lifecycle status of the plan.</param>
/// <param name="CreatedAt">UTC timestamp when the plan was created.</param>
/// <param name="UpdatedAt">UTC timestamp when the plan was last updated.</param>
/// <param name="Metadata">Flexible JSONB metadata for plan-specific data.</param>
public sealed record PlanRecord(
    long Id,
    string ProjectId,
    string SessionId,
    string Name,
    string? Description,
    PlanStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    JsonDocument Metadata);

/// <summary>
/// Represents a task within a plan.
/// Tasks are executed sequentially or in parallel as defined by the plan.
/// </summary>
/// <param name="Id">Database identifier of the task.</param>
/// <param name="PlanId">Parent plan identifier this task belongs to.</param>
/// <param name="Title">Human-readable task title.</param>
/// <param name="Description">Optional detailed description of the task.</param>
/// <param name="Priority">Priority level for task execution ordering.</param>
/// <param name="Status">Current execution status of the task.</param>
/// <param name="CreatedAt">UTC timestamp when the task was created.</param>
/// <param name="UpdatedAt">UTC timestamp when the task was last updated.</param>
/// <param name="Metadata">Flexible JSONB metadata for task-specific data.</param>
public sealed record PlanTaskRecord(
    long Id,
    long PlanId,
    string Title,
    string? Description,
    string Priority,
    TaskStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    JsonDocument Metadata);

/// <summary>
/// Represents an audit trail entry for plan status changes.
/// History is automatically created on every status transition.
/// </summary>
/// <param name="Id">Database identifier of the history entry.</param>
/// <param name="PlanId">Parent plan identifier this history belongs to.</param>
/// <param name="OldStatus">Previous status value, null for initial status.</param>
/// <param name="NewStatus">New status value after the transition.</param>
/// <param name="ChangedBy">Identifier of who or what made the change.</param>
/// <param name="ChangedAt">UTC timestamp when the status change occurred.</param>
/// <param name="Reason">Optional description explaining the status transition.</param>
public sealed record PlanHistoryRecord(
    long Id,
    long PlanId,
    string? OldStatus,
    string NewStatus,
    string ChangedBy,
    DateTimeOffset ChangedAt,
    string? Reason);

/// <summary>
/// Represents an audit trail entry for task status changes.
/// History is automatically created on every status transition.
/// </summary>
/// <param name="Id">Database identifier of the history entry.</param>
/// <param name="TaskId">Parent task identifier this history belongs to.</param>
/// <param name="OldStatus">Previous status value, null for initial status.</param>
/// <param name="NewStatus">New status value after the transition.</param>
/// <param name="ChangedBy">Identifier of who or what made the change.</param>
/// <param name="ChangedAt">UTC timestamp when the status change occurred.</param>
/// <param name="Reason">Optional description explaining the status transition.</param>
public sealed record TaskHistoryRecord(
    long Id,
    long TaskId,
    string? OldStatus,
    string NewStatus,
    string ChangedBy,
    DateTimeOffset ChangedAt,
    string? Reason);
