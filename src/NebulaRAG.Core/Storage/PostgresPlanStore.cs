using System.Text.Json;
using NebulaRAG.Core.Exceptions;
using NebulaRAG.Core.Models;
using Npgsql;

namespace NebulaRAG.Core.Storage;

/// <summary>
/// PostgreSQL-based storage backend for plan lifecycle management.
/// Manages plans, tasks, and their associated history tables with database-enforced constraints.
/// </summary>
public sealed class PostgresPlanStore
{
    private readonly string _connectionString;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgresPlanStore"/> class.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <exception cref="ArgumentException">Thrown if connectionString is null or empty.</exception>
    public PostgresPlanStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string cannot be null or empty.", nameof(connectionString));
        }

        _connectionString = connectionString;
    }

    /// <summary>
    /// Initializes the PostgreSQL schema including tables, constraints, and indexes.
    /// Creates plans, tasks, plan_history, and task_history tables with CHECK constraints
    /// for status validation and composite indexes for efficient queries.
    /// This method is idempotent and safe to run multiple times.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task InitializeSchemaAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS plans (
                id BIGSERIAL PRIMARY KEY,
                project_id TEXT NOT NULL,
                session_id TEXT NOT NULL,
                name TEXT NOT NULL,
                description TEXT,
                status TEXT NOT NULL CHECK (status IN ('draft', 'active', 'completed', 'archived')),
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                metadata JSONB DEFAULT '{}'
            );

            CREATE TABLE IF NOT EXISTS tasks (
                id BIGSERIAL PRIMARY KEY,
                plan_id BIGINT NOT NULL REFERENCES plans(id) ON DELETE CASCADE,
                title TEXT NOT NULL,
                description TEXT,
                priority TEXT NOT NULL DEFAULT 'normal',
                status TEXT NOT NULL CHECK (status IN ('pending', 'in_progress', 'completed', 'failed')),
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                metadata JSONB DEFAULT '{}'
            );

            CREATE TABLE IF NOT EXISTS plan_history (
                id BIGSERIAL PRIMARY KEY,
                plan_id BIGINT NOT NULL REFERENCES plans(id) ON DELETE CASCADE,
                old_status TEXT,
                new_status TEXT,
                changed_by TEXT NOT NULL,
                changed_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                reason TEXT
            );

            CREATE TABLE IF NOT EXISTS task_history (
                id BIGSERIAL PRIMARY KEY,
                task_id BIGINT NOT NULL REFERENCES tasks(id) ON DELETE CASCADE,
                old_status TEXT,
                new_status TEXT,
                changed_by TEXT NOT NULL,
                changed_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                reason TEXT
            );

            CREATE INDEX IF NOT EXISTS ix_plans_session_status ON plans(session_id, status);
            CREATE INDEX IF NOT EXISTS ix_plans_project_name ON plans(project_id, name);
            CREATE INDEX IF NOT EXISTS ix_tasks_plan_id ON tasks(plan_id);
            CREATE INDEX IF NOT EXISTS ix_plan_history_plan_id ON plan_history(plan_id);
            CREATE INDEX IF NOT EXISTS ix_task_history_task_id ON task_history(task_id);
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await using var command = new NpgsqlCommand(sql, connection);

        await connection.OpenAsync(cancellationToken);
        await command.ExecuteNonQueryAsync(cancellationToken);
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
        const string sql = @"
            SELECT id, project_id, session_id, name, description, status, created_at, updated_at, metadata
            FROM plans
            WHERE id = @planId";

        await using var connection = new NpgsqlConnection(_connectionString);
        await using var command = new NpgsqlCommand(sql, connection);

        command.Parameters.AddWithValue("planId", planId);

        await connection.OpenAsync(cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (await reader.ReadAsync(cancellationToken))
        {
            return ReadPlanFromReader(reader);
        }

        throw new PlanNotFoundException(planId);
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
        const string sql = @"
            SELECT id, project_id, session_id, name, description, status, created_at, updated_at, metadata
            FROM plans
            WHERE project_id = @projectId AND name = @name
            LIMIT 1";

        await using var connection = new NpgsqlConnection(_connectionString);
        await using var command = new NpgsqlCommand(sql, connection);

        command.Parameters.AddWithValue("projectId", projectId);
        command.Parameters.AddWithValue("name", name);

        await connection.OpenAsync(cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (await reader.ReadAsync(cancellationToken))
        {
            return ReadPlanFromReader(reader);
        }

        throw new PlanNotFoundException(0);
    }

    /// <summary>
    /// Creates a new plan with optional initial tasks in a single atomic transaction.
    /// </summary>
    /// <param name="request">The plan creation request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A tuple containing the created plan ID and list of task IDs.</returns>
    public async Task<(long planId, IReadOnlyList<long> taskIds)> CreatePlanAsync(CreatePlanRequest request, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            const string insertPlanSql = @"
                INSERT INTO plans (project_id, session_id, name, description, status, created_at, updated_at, metadata)
                VALUES (@projectId, @sessionId, @name, @description, @status, NOW(), NOW(), '{}'::jsonb)
                RETURNING id";

            var planId = await ExecuteScalarAsync<long>(
                connection, insertPlanSql,
                new Dictionary<string, object?>
                {
                    { "projectId", request.ProjectId },
                    { "sessionId", request.SessionId },
                    { "name", request.Name },
                    { "description", request.Description },
                    { "status", PlanStatus.Draft.ToString().ToLowerInvariant() }
                },
                cancellationToken);

            var taskIds = new List<long>();
            foreach (var task in request.InitialTasks)
            {
                const string insertTaskSql = @"
                    INSERT INTO tasks (plan_id, title, description, priority, status, created_at, updated_at, metadata)
                    VALUES (@planId, @title, @description, @priority, @status, NOW(), NOW(), '{}'::jsonb)
                    RETURNING id";

                var taskId = await ExecuteScalarAsync<long>(
                    connection, insertTaskSql,
                    new Dictionary<string, object?>
                    {
                        { "planId", planId },
                        { "title", task.Title },
                        { "description", task.Description },
                        { "priority", task.Priority },
                        { "status", Models.TaskStatus.Pending.ToString().ToLowerInvariant() }
                    },
                    cancellationToken);
                taskIds.Add(taskId);
            }

            const string insertHistorySql = @"
                INSERT INTO plan_history (plan_id, old_status, new_status, changed_by, changed_at, reason)
                VALUES (@planId, NULL, @newStatus, @changedBy, NOW(), @reason)";

            await ExecuteNonQueryAsync(
                connection, insertHistorySql,
                new Dictionary<string, object?>
                {
                    { "planId", planId },
                    { "newStatus", PlanStatus.Draft.ToString().ToLowerInvariant() },
                    { "changedBy", request.ChangedBy },
                    { "reason", request.Reason }
                },
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return (planId, taskIds);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Updates plan details.
    /// </summary>
    /// <param name="planId">The plan identifier.</param>
    /// <param name="request">The plan update request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="PlanNotFoundException">Thrown when the plan is not found.</exception>
    public async Task UpdatePlanAsync(long planId, UpdatePlanRequest request, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE plans
            SET name = COALESCE(@name, name),
                description = @description,
                updated_at = NOW()
            WHERE id = @planId";

        await using var connection = new NpgsqlConnection(_connectionString);
        await using var command = new NpgsqlCommand(sql, connection);

        command.Parameters.AddWithValue("planId", planId);
        command.Parameters.AddWithValue("name", request.Name ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("description", request.Description ?? (object)DBNull.Value);

        await connection.OpenAsync(cancellationToken);
        var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);

        if (rowsAffected == 0)
        {
            throw new PlanNotFoundException(planId);
        }
    }

    /// <summary>
    /// Archives a plan by setting its status to archived and recording the history.
    /// </summary>
    /// <param name="planId">The plan identifier.</param>
    /// <param name="changedBy">Identifier of who made the change.</param>
    /// <param name="reason">Optional reason for the change.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="PlanNotFoundException">Thrown when the plan is not found.</exception>
    public async Task ArchivePlanAsync(long planId, string changedBy, string? reason, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var currentPlan = await GetPlanByIdAsync(planId, cancellationToken);
            var oldStatus = currentPlan.Status.ToString().ToLowerInvariant();

            const string updateSql = @"
                UPDATE plans
                SET status = 'archived', updated_at = NOW()
                WHERE id = @planId";

            await ExecuteNonQueryAsync(
                connection, updateSql,
                new Dictionary<string, object?> { { "planId", planId } },
                cancellationToken);

            const string historySql = @"
                INSERT INTO plan_history (plan_id, old_status, new_status, changed_by, changed_at, reason)
                VALUES (@planId, @oldStatus, @newStatus, @changedBy, NOW(), @reason)";

            await ExecuteNonQueryAsync(
                connection, historySql,
                new Dictionary<string, object?>
                {
                    { "planId", planId },
                    { "oldStatus", oldStatus },
                    { "newStatus", "archived" },
                    { "changedBy", changedBy },
                    { "reason", reason }
                },
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch (PlanNotFoundException)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Lists all plans for a given session identifier.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A read-only list of plan records ordered by creation date descending.</returns>
    public async Task<IReadOnlyList<PlanRecord>> ListPlansBySessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT id, project_id, session_id, name, description, status, created_at, updated_at, metadata
            FROM plans
            WHERE session_id = @sessionId
            ORDER BY created_at DESC";

        await using var connection = new NpgsqlConnection(_connectionString);
        await using var command = new NpgsqlCommand(sql, connection);

        command.Parameters.AddWithValue("sessionId", sessionId);

        await connection.OpenAsync(cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var plans = new List<PlanRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            plans.Add(ReadPlanFromReader(reader));
        }

        return plans;
    }

    private static PlanRecord ReadPlanFromReader(NpgsqlDataReader reader)
    {
        var id = reader.GetInt64(reader.GetOrdinal("id"));
        var projectId = reader.GetString(reader.GetOrdinal("project_id"));
        var sessionId = reader.GetString(reader.GetOrdinal("session_id"));
        var name = reader.GetString(reader.GetOrdinal("name"));
        var description = reader.IsDBNull(reader.GetOrdinal("description"))
            ? null
            : reader.GetString(reader.GetOrdinal("description"));
        var statusString = reader.GetString(reader.GetOrdinal("status"));
        var status = Enum.Parse<PlanStatus>(statusString, ignoreCase: true);
        var createdAt = reader.GetDateTime(reader.GetOrdinal("created_at"));
        var updatedAt = reader.GetDateTime(reader.GetOrdinal("updated_at"));
        var metadataJson = reader.GetString(reader.GetOrdinal("metadata"));
        var metadata = JsonDocument.Parse(metadataJson);

        return new PlanRecord(
            id, projectId, sessionId, name, description, status, createdAt, updatedAt, metadata);
    }

    private static async Task<T> ExecuteScalarAsync<T>(
        NpgsqlConnection connection,
        string sql,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (key, value) in parameters)
        {
            command.Parameters.AddWithValue(key, value ?? DBNull.Value);
        }

        var result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is null || result == DBNull.Value)
        {
            throw new InvalidOperationException($"Expected scalar result from query: {sql}");
        }

        return (T)result;
    }

    private static async Task<int> ExecuteNonQueryAsync(
        NpgsqlConnection connection,
        string sql,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (key, value) in parameters)
        {
            command.Parameters.AddWithValue(key, value ?? DBNull.Value);
        }

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

/// <summary>
/// Request object for creating a new plan.
/// </summary>
/// <param name="ProjectId">The project identifier.</param>
/// <param name="SessionId">The session identifier.</param>
/// <param name="Name">The plan name.</param>
/// <param name="Description">Optional description.</param>
/// <param name="InitialTasks">Optional list of initial tasks to create.</param>
/// <param name="ChangedBy">Identifier of who is creating the plan.</param>
/// <param name="Reason">Optional reason for creation.</param>
public sealed record CreatePlanRequest(
    string ProjectId,
    string SessionId,
    string Name,
    string? Description,
    IReadOnlyList<CreateTaskRequest> InitialTasks,
    string ChangedBy,
    string? Reason = null);

/// <summary>
/// Request object for updating a plan.
/// </summary>
/// <param name="Name">Optional new name.</param>
/// <param name="Description">Optional new description.</param>
/// <param name="ChangedBy">Identifier of who is updating the plan.</param>
public sealed record UpdatePlanRequest(
    string? Name,
    string? Description,
    string ChangedBy);

/// <summary>
/// Request object for creating a task.
/// </summary>
/// <param name="Title">The task title.</param>
/// <param name="Description">Optional description.</param>
/// <param name="Priority">The task priority.</param>
/// <param name="ChangedBy">Identifier of who is creating the task.</param>
public sealed record CreateTaskRequest(
    string Title,
    string? Description,
    string Priority,
    string ChangedBy);
