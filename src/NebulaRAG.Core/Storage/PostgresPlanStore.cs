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
}
