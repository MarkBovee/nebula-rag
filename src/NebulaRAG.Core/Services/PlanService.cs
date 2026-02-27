using NebulaRAG.Core.Exceptions;
using NebulaRAG.Core.Models;
using NebulaRAG.Core.Storage;

namespace NebulaRAG.Core.Services;

/// <summary>
/// Service for plan-level operations with business logic enforcement.
/// </summary>
public sealed class PlanService
{
    private readonly PostgresPlanStore _planStore;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlanService"/> class.
    /// </summary>
    /// <param name="planStore">The plan storage backend.</param>
    public PlanService(PostgresPlanStore planStore)
    {
        _planStore = planStore ?? throw new ArgumentNullException(nameof(planStore));
    }

    /// <summary>
    /// Creates a new plan with the specified details.
    /// Enforces business rules including active plan constraint.
    /// </summary>
    /// <param name="request">The plan creation request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A tuple containing the created plan ID and list of task IDs.</returns>
    /// <exception cref="PlanException">Thrown when business rules are violated.</exception>
    public async Task<(long planId, IReadOnlyList<long> taskIds)> CreatePlanAsync(CreatePlanRequest request, CancellationToken cancellationToken = default)
    {
        // Check for existing active plan in the session
        var existingPlans = await _planStore.ListPlansBySessionAsync(request.SessionId, cancellationToken);
        if (!PlanValidator.CanCreatePlan(existingPlans))
        {
            throw new PlanException(
                violationType: "ActivePlanExists",
                message: $"An active plan already exists for session {request.SessionId}. Only one active plan is allowed per session.",
                context: new { SessionId = request.SessionId });
        }

        // Validate plan name uniqueness within project
        var projectPlans = await _planStore.ListPlansBySessionAsync(request.SessionId, cancellationToken);
        if (!PlanValidator.IsPlanNameUnique(projectPlans, request.Name))
        {
            throw new PlanException(
                violationType: "PlanNameNotUnique",
                message: $"A plan with name '{request.Name}' already exists in project {request.ProjectId}.",
                context: new { ProjectId = request.ProjectId, PlanName = request.Name });
        }

        // Create the plan (storage layer handles transaction)
        return await _planStore.CreatePlanAsync(request, cancellationToken);
    }

    /// <summary>
    /// Archives a plan, transitioning it to Archived status.
    /// </summary>
    /// <param name="planId">The plan identifier.</param>
    /// <param name="changedBy">Identifier of who made the change.</param>
    /// <param name="reason">Optional reason for the change.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="PlanException">Thrown when business rules are violated.</exception>
    /// <exception cref="PlanNotFoundException">Thrown when the plan is not found.</exception>
    public async Task ArchivePlanAsync(long planId, string changedBy, string? reason, CancellationToken cancellationToken = default)
    {
        await UpdatePlanStatusAsync(planId, PlanStatus.Archived, changedBy, reason, cancellationToken);
    }

    /// <summary>
    /// Updates the plan lifecycle status after validating transition rules.
    /// </summary>
    /// <param name="planId">The plan identifier.</param>
    /// <param name="newStatus">The new status to apply.</param>
    /// <param name="changedBy">Identifier of who made the change.</param>
    /// <param name="reason">Optional reason for the status transition.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="PlanException">Thrown when transition rules are violated.</exception>
    /// <exception cref="PlanNotFoundException">Thrown when the plan is not found.</exception>
    public async Task UpdatePlanStatusAsync(long planId, PlanStatus newStatus, string changedBy, string? reason, CancellationToken cancellationToken = default)
    {
        var plan = await _planStore.GetPlanByIdAsync(planId, cancellationToken);
        if (!PlanValidator.CanTransition(plan.Status, newStatus))
        {
            throw new PlanException(
                violationType: "InvalidPlanStatusTransition",
                message: $"Plan {planId} cannot transition from {plan.Status} to {newStatus}.",
                context: new { PlanId = planId, CurrentStatus = plan.Status, NewStatus = newStatus });
        }

        await _planStore.UpdatePlanStatusAsync(planId, newStatus, changedBy, reason, cancellationToken);
    }

    /// <summary>
    /// Updates plan details.
    /// </summary>
    /// <param name="planId">The plan identifier.</param>
    /// <param name="request">The plan update request.</param>
    /// <param name="changedBy">Identifier of who made the change.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="PlanNotFoundException">Thrown when the plan is not found.</exception>
    public async Task UpdatePlanAsync(long planId, UpdatePlanRequest request, string changedBy, CancellationToken cancellationToken = default)
    {
        await _planStore.UpdatePlanAsync(planId, request, cancellationToken);
    }

    /// <summary>
    /// Retrieves a plan by its unique identifier.
    /// </summary>
    /// <param name="planId">The plan identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The plan record.</returns>
    /// <exception cref="PlanNotFoundException">Thrown when the plan is not found.</exception>
    public async Task<PlanRecord> GetPlanByIdAsync(long planId, CancellationToken cancellationToken = default)
    {
        return await _planStore.GetPlanByIdAsync(planId, cancellationToken);
    }

    /// <summary>
    /// Retrieves a plan by project identifier and name.
    /// </summary>
    /// <param name="projectId">The project identifier.</param>
    /// <param name="name">The plan name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The plan record.</returns>
    /// <exception cref="PlanNotFoundException">Thrown when the plan is not found.</exception>
    public async Task<PlanRecord> GetPlanByProjectAndNameAsync(string projectId, string name, CancellationToken cancellationToken = default)
    {
        return await _planStore.GetPlanByProjectAndNameAsync(projectId, name, cancellationToken);
    }

    /// <summary>
    /// Lists all plans for a given session identifier.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A read-only list of plan records ordered by creation date descending.</returns>
    public async Task<IReadOnlyList<PlanRecord>> ListPlansBySessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        return await _planStore.ListPlansBySessionAsync(sessionId, cancellationToken);
    }

    /// <summary>
    /// Retrieves a plan with all its tasks by plan identifier.
    /// </summary>
    /// <param name="planId">The plan identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A tuple containing the plan record and its tasks.</returns>
    /// <exception cref="PlanNotFoundException">Thrown when the plan is not found.</exception>
    public async Task<(PlanRecord plan, IReadOnlyList<PlanTaskRecord> tasks)> GetPlanWithTasksByIdAsync(long planId, CancellationToken cancellationToken = default)
    {
        return await _planStore.GetPlanWithTasksByIdAsync(planId, cancellationToken);
    }

    /// <summary>
    /// Retrieves a plan with all its tasks by project identifier and name.
    /// </summary>
    /// <param name="projectId">The project identifier.</param>
    /// <param name="name">The plan name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A tuple containing the plan record and its tasks.</returns>
    /// <exception cref="PlanNotFoundException">Thrown when the plan is not found.</exception>
    public async Task<(PlanRecord plan, IReadOnlyList<PlanTaskRecord> tasks)> GetPlanWithTasksByProjectAndNameAsync(string projectId, string name, CancellationToken cancellationToken = default)
    {
        return await _planStore.GetPlanWithTasksByProjectAndNameAsync(projectId, name, cancellationToken);
    }
}