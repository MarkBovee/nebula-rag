using NebulaRAG.Core.Models;
using TaskLifecycleStatus = NebulaRAG.Core.Models.TaskStatus;

namespace NebulaRAG.Core.Services;

/// <summary>
/// Validates plan status transitions and business rules.
/// </summary>
public static class PlanValidator
{
    /// <summary>
    /// Validates if a plan can transition from one status to another.
    /// </summary>
    /// <param name="currentStatus">The current status of the plan.</param>
    /// <param name="newStatus">The desired new status.</param>
    /// <returns>True if the transition is valid, false otherwise.</returns>
    public static bool CanTransition(PlanStatus currentStatus, PlanStatus newStatus)
    {
        // Valid transitions:
        // Draft → Active
        // Active → Completed
        // Active → Archived
        // Completed → Archived

        // Invalid transitions:
        // Draft → Completed
        // Draft → Archived
        // Active → Draft
        // Completed → Draft
        // Completed → Active
        // Archived → any status

        switch (currentStatus)
        {
            case PlanStatus.Draft:
                return newStatus == PlanStatus.Active;

            case PlanStatus.Active:
                return newStatus == PlanStatus.Completed || newStatus == PlanStatus.Archived;

            case PlanStatus.Completed:
                return newStatus == PlanStatus.Archived;

            case PlanStatus.Archived:
                return false;

            default:
                return false;
        }
    }

    /// <summary>
    /// Validates if a task can transition from one status to another.
    /// </summary>
    /// <param name="currentStatus">The current status of the task.</param>
    /// <param name="newStatus">The desired new status.</param>
    /// <returns>True if the transition is valid, false otherwise.</returns>
    public static bool CanTransition(TaskLifecycleStatus currentStatus, TaskLifecycleStatus newStatus)
    {
        // Valid transitions:
        // Pending → InProgress
        // InProgress → Completed
        // InProgress → Failed
        // Completed → Failed (optional, depending on requirements)

        // Invalid transitions:
        // Pending → Completed
        // Pending → Failed
        // InProgress → Pending
        // Completed → Pending
        // Completed → InProgress
        // Failed → Pending
        // Failed → InProgress
        // Failed → Completed

        switch (currentStatus)
        {
            case TaskLifecycleStatus.Pending:
                return newStatus == TaskLifecycleStatus.InProgress;

            case TaskLifecycleStatus.InProgress:
                return newStatus == TaskLifecycleStatus.Completed || newStatus == TaskLifecycleStatus.Failed;

            case TaskLifecycleStatus.Completed:
                return newStatus == TaskLifecycleStatus.Failed; // Optional: allow failed transition

            case TaskLifecycleStatus.Failed:
                return false;

            default:
                return false;
        }
    }

    /// <summary>
    /// Validates that only one active plan can exist per session.
    /// </summary>
    /// <param name="plans">The list of plans for the session.</param>
    /// <returns>True if the session can create a new plan, false if an active plan already exists.</returns>
    public static bool CanCreatePlan(IEnumerable<PlanRecord> plans)
    {
        return !plans.Any(p => p.Status == PlanStatus.Active);
    }

    /// <summary>
    /// Validates that a plan can be archived.
    /// </summary>
    /// <param name="plan">The plan to validate.</param>
    /// <returns>True if the plan can be archived, false otherwise.</returns>
    public static bool CanArchivePlan(PlanRecord plan)
    {
        return plan.Status == PlanStatus.Active || plan.Status == PlanStatus.Completed;
    }

    /// <summary>
    /// Validates that a task can be completed.
    /// </summary>
    /// <param name="task">The task to validate.</param>
    /// <returns>True if the task can be completed, false otherwise.</returns>
    public static bool CanCompleteTask(PlanTaskRecord task)
    {
        return task.Status == TaskLifecycleStatus.Pending || task.Status == TaskLifecycleStatus.InProgress;
    }

    /// <summary>
    /// Validates that a plan name is unique within a project.
    /// </summary>
    /// <param name="plans">The list of existing plans.</param>
    /// <param name="name">The name to validate.</param>
    /// <returns>True if the name is unique, false otherwise.</returns>
    public static bool IsPlanNameUnique(IEnumerable<PlanRecord> plans, string name)
    {
        return !plans.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }
}