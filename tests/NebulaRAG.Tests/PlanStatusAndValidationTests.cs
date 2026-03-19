using System.Text.Json;
using NebulaRAG.Core.Models;
using NebulaRAG.Core.Storage;
using NebulaRAG.Core.Services;
using TaskLifecycleStatus = NebulaRAG.Core.Models.TaskStatus;

namespace NebulaRAG.Tests;

/// <summary>
/// Covers status mapping and plan completion validation rules used by MCP plan operations.
/// </summary>
public sealed class PlanStatusAndValidationTests
{
    /// <summary>
    /// Ensures task in-progress status is serialized with the PostgreSQL underscore form.
    /// </summary>
    [Fact]
    public void ToDatabaseValue_TaskInProgress_UsesUnderscoreForm()
    {
        var databaseValue = PlanStatusMapper.ToDatabaseValue(TaskLifecycleStatus.InProgress);

        Assert.Equal("in_progress", databaseValue);
    }

    /// <summary>
    /// Ensures task status parsing accepts both canonical and legacy in-progress spellings.
    /// </summary>
    [Theory]
    [InlineData("in_progress")]
    [InlineData("inprogress")]
    public void ParseTaskStatus_InProgressVariants_ReturnInProgress(string databaseValue)
    {
        var parsedStatus = PlanStatusMapper.ParseTaskStatus(databaseValue);

        Assert.Equal(TaskLifecycleStatus.InProgress, parsedStatus);
    }

    /// <summary>
    /// Ensures plans cannot be marked completed while open tasks remain.
    /// </summary>
    [Fact]
    public void CanCompletePlan_OpenTasksRemain_ReturnsFalse()
    {
        var tasks = new[]
        {
            CreateTaskRecord(1, TaskLifecycleStatus.Pending),
            CreateTaskRecord(2, TaskLifecycleStatus.Completed)
        };

        var canCompletePlan = PlanValidator.CanCompletePlan(tasks);

        Assert.False(canCompletePlan);
    }

    /// <summary>
    /// Ensures plans can be marked completed once all tasks are terminal.
    /// </summary>
    [Fact]
    public void CanCompletePlan_AllTasksTerminal_ReturnsTrue()
    {
        var tasks = new[]
        {
            CreateTaskRecord(1, TaskLifecycleStatus.Completed),
            CreateTaskRecord(2, TaskLifecycleStatus.Failed)
        };

        var canCompletePlan = PlanValidator.CanCompletePlan(tasks);

        Assert.True(canCompletePlan);
    }

    /// <summary>
    /// Creates a minimal task record for validation-focused tests.
    /// </summary>
    /// <param name="taskId">The task identifier.</param>
    /// <param name="status">The task status to assign.</param>
    /// <returns>A task record with stable default metadata.</returns>
    private static PlanTaskRecord CreateTaskRecord(long taskId, TaskLifecycleStatus status)
    {
        return new PlanTaskRecord(
            taskId,
            99,
            $"Task {taskId}",
            null,
            "normal",
            status,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            JsonDocument.Parse("{}"));
    }
}