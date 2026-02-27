namespace NebulaRAG.Core.Exceptions;

/// <summary>
/// Thrown when a plan or task cannot be found in the storage system.
/// </summary>
public sealed class PlanNotFoundException : Exception
{
    /// <summary>
    /// The plan identifier that was not found.
    /// </summary>
    public long PlanId { get; }

    /// <summary>
    /// The optional task identifier that was not found.
    /// </summary>
    public long? TaskId { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlanNotFoundException"/> class.
    /// </summary>
    public PlanNotFoundException()
        : base("Plan or task not found.")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlanNotFoundException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public PlanNotFoundException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlanNotFoundException"/> class with a specified error message and a reference to the inner exception that is the cause of this exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public PlanNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlanNotFoundException"/> class with plan and optional task identifiers.
    /// </summary>
    /// <param name="planId">The plan identifier that was not found.</param>
    /// <param name="taskId">The optional task identifier that was not found.</param>
    public PlanNotFoundException(long planId, long? taskId = null)
        : base(taskId.HasValue
            ? $"Task {taskId.Value} in plan {planId} not found."
            : $"Plan {planId} not found.")
    {
        PlanId = planId;
        TaskId = taskId;
    }
}
