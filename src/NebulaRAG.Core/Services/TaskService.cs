using NebulaRAG.Core.Exceptions;
using NebulaRAG.Core.Models;
using NebulaRAG.Core.Storage;

namespace NebulaRAG.Core.Services;

/// <summary>
/// Service for task-level operations with business logic enforcement.
/// </summary>
public sealed class TaskService
{
    private readonly PostgresPlanStore _planStore;

    /// <summary>
    /// Initializes a new instance of the <see cref="TaskService"/> class.
    /// </summary>
    /// <param name="planStore">The plan storage backend.</param>
    public TaskService(PostgresPlanStore planStore)
    {
        _planStore = planStore ?? throw new ArgumentNullException(nameof(planStore));
    }

    /// <summary>
    /// Creates a new task for the specified plan.
    /// </summary>
    /// <param name="planId">The parent plan identifier.</param>
    /// <param name="request">The task creation request.</param>
    /// <param name="changedBy">Identifier of who made the change.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created task identifier.</returns>
    /// <exception cref="PlanNotFoundException">Thrown when the plan is not found.</exception>
    public async Task<long> CreateTaskAsync(long planId, CreateTaskRequest request, string changedBy, CancellationToken cancellationToken = default)
    {
        await _planStore.GetPlanByIdAsync(planId, cancellationToken);
        return await _planStore.CreateTaskAsync(planId, request, cancellationToken);
    }

    /// <summary>
    /// Marks a task as completed.
    /// </summary>
    /// <param name="taskId">The task identifier.</param>
    /// <param name="changedBy">Identifier of who made the change.</param>
    /// <param name="reason">Optional reason for the change.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="PlanException">Thrown when business rules are violated.</exception>
    /// <exception cref="PlanNotFoundException">Thrown when the task is not found.</exception>
    public async Task CompleteTaskAsync(long taskId, string changedBy, string? reason, CancellationToken cancellationToken = default)
    {
        var task = await _planStore.GetTaskByIdAsync(taskId, cancellationToken);

        if (!PlanValidator.CanCompleteTask(task))
        {
            throw new PlanException(
                violationType: "InvalidTaskStatusForComplete",
                message: $"Task {taskId} cannot be completed in its current status {task.Status}.",
                context: new { TaskId = taskId, CurrentStatus = task.Status });
        }

        await _planStore.CompleteTaskAsync(taskId, changedBy, reason, cancellationToken);
    }

    /// <summary>
    /// Updates task details.
    /// </summary>
    /// <param name="taskId">The task identifier.</param>
    /// <param name="request">The task update request.</param>
    /// <param name="changedBy">Identifier of who made the change.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="PlanNotFoundException">Thrown when the task is not found.</exception>
    public async Task UpdateTaskAsync(long taskId, UpdateTaskRequest request, string changedBy, CancellationToken cancellationToken = default)
    {
        await _planStore.UpdateTaskAsync(taskId, request, cancellationToken);
    }

    /// <summary>
    /// Retrieves a task by its unique identifier.
    /// </summary>
    /// <param name="taskId">The task identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The task record.</returns>
    /// <exception cref="PlanNotFoundException">Thrown when the task is not found.</exception>
    public async Task<PlanTaskRecord> GetTaskByIdAsync(long taskId, CancellationToken cancellationToken = default)
    {
        return await _planStore.GetTaskByIdAsync(taskId, cancellationToken);
    }

    /// <summary>
    /// Retrieves all tasks for a given plan identifier.
    /// </summary>
    /// <param name="planId">The plan identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A read-only list of task records ordered by creation date.</returns>
    public async Task<IReadOnlyList<PlanTaskRecord>> GetTasksByPlanIdAsync(long planId, CancellationToken cancellationToken = default)
    {
        return await _planStore.GetTasksByPlanIdAsync(planId, cancellationToken);
    }
}