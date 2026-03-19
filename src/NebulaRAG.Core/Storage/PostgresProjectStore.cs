using NebulaRAG.Core.Models;
using Npgsql;

namespace NebulaRAG.Core.Storage;

/// <summary>
/// Coordinates project-wide rename and delete operations across plans, memory, and indexed documents.
/// </summary>
public sealed class PostgresProjectStore
{
    private const string DerivedDocumentProjectSql = """
        COALESCE(NULLIF(
            CASE
                WHEN source_path ~* '^https?://' THEN split_part(split_part(source_path, '://', 2), '/', 1)
                ELSE split_part(replace(source_path, '\\', '/'), '/', 1)
            END,
            ''
        ), 'unscoped')
        """;

    private readonly string _connectionString;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgresProjectStore"/> class.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    public PostgresProjectStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string cannot be null or empty.", nameof(connectionString));
        }

        _connectionString = connectionString;
    }

    /// <summary>
    /// Deletes all plan, memory, and indexed-document data associated with a project.
    /// </summary>
    /// <param name="projectId">Project identifier to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A summary of affected rows.</returns>
    public async Task<ProjectMutationResult> DeleteProjectAsync(string projectId, CancellationToken cancellationToken = default)
    {
        var normalizedProjectId = NormalizeProjectId(projectId);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var summary = await CountProjectDataAsync(connection, transaction, normalizedProjectId, cancellationToken);

            await ExecuteNonQueryAsync(connection, transaction, "DELETE FROM memories WHERE project_id = @projectId", normalizedProjectId, cancellationToken);
            await ExecuteNonQueryAsync(connection, transaction, "DELETE FROM plans WHERE project_id = @projectId", normalizedProjectId, cancellationToken);
            await ExecuteNonQueryAsync(
                connection,
                transaction,
                $"DELETE FROM rag_documents WHERE {DerivedDocumentProjectSql} = @projectId",
                normalizedProjectId,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return new ProjectMutationResult(normalizedProjectId, null, summary.PlanCount, summary.TaskCount, summary.MemoryCount, summary.DocumentCount, summary.ChunkCount);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Renames a project across plans, memory, and prefix-based indexed-document source keys.
    /// </summary>
    /// <param name="projectId">Current project identifier.</param>
    /// <param name="targetProjectId">Replacement project identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A summary of affected rows.</returns>
    public async Task<ProjectMutationResult> RenameProjectAsync(string projectId, string targetProjectId, CancellationToken cancellationToken = default)
    {
        var normalizedProjectId = NormalizeProjectId(projectId);
        var normalizedTargetProjectId = NormalizeProjectId(targetProjectId);

        if (string.Equals(normalizedProjectId, normalizedTargetProjectId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Target project id must differ from the current project id.", nameof(targetProjectId));
        }

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var summary = await CountProjectDataAsync(connection, transaction, normalizedProjectId, cancellationToken);
            var sourcePaths = await GetProjectDocumentSourcePathsAsync(connection, transaction, normalizedProjectId, cancellationToken);

            var unsafeSources = sourcePaths
                .Where(sourcePath => !IsProjectPrefixSourcePath(sourcePath, normalizedProjectId))
                .Take(5)
                .ToList();

            if (unsafeSources.Count > 0)
            {
                throw new InvalidOperationException($"Project rename is only supported for prefixed source keys. Rename or remove these sources first: {string.Join(", ", unsafeSources)}");
            }

            var sourcePathSet = sourcePaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var allSourcePaths = await GetAllDocumentSourcePathsAsync(connection, transaction, cancellationToken);
            var collisionCandidates = sourcePaths
                .Select(sourcePath => new { SourcePath = sourcePath, TargetPath = RewriteProjectPrefix(sourcePath, normalizedProjectId, normalizedTargetProjectId) })
                .Where(row => allSourcePaths.Contains(row.TargetPath) && !sourcePathSet.Contains(row.TargetPath))
                .Select(row => row.TargetPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .ToList();

            if (collisionCandidates.Count > 0)
            {
                throw new InvalidOperationException($"Project rename would collide with existing indexed sources: {string.Join(", ", collisionCandidates)}");
            }

            await ExecuteRenameAsync(connection, transaction, normalizedProjectId, normalizedTargetProjectId, cancellationToken);

            foreach (var sourcePath in sourcePaths)
            {
                var rewrittenPath = RewriteProjectPrefix(sourcePath, normalizedProjectId, normalizedTargetProjectId);
                await using var command = new NpgsqlCommand("UPDATE rag_documents SET source_path = @targetPath WHERE source_path = @sourcePath", connection, transaction);
                command.Parameters.AddWithValue("targetPath", rewrittenPath);
                command.Parameters.AddWithValue("sourcePath", sourcePath);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return new ProjectMutationResult(normalizedProjectId, normalizedTargetProjectId, summary.PlanCount, summary.TaskCount, summary.MemoryCount, summary.DocumentCount, summary.ChunkCount);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static async Task ExecuteNonQueryAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, string projectId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("projectId", projectId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<HashSet<string>> GetAllDocumentSourcePathsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        var sourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = new NpgsqlCommand("SELECT source_path FROM rag_documents", connection, transaction);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            sourcePaths.Add(reader.GetString(0));
        }

        return sourcePaths;
    }

    private static async Task<List<string>> GetProjectDocumentSourcePathsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string projectId, CancellationToken cancellationToken)
    {
        var sourcePaths = new List<string>();
        var sql = $"SELECT source_path FROM rag_documents WHERE {DerivedDocumentProjectSql} = @projectId ORDER BY source_path ASC";

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("projectId", projectId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            sourcePaths.Add(reader.GetString(0));
        }

        return sourcePaths;
    }

    private static async Task ExecuteRenameAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string projectId, string targetProjectId, CancellationToken cancellationToken)
    {
        await using var planCommand = new NpgsqlCommand("UPDATE plans SET project_id = @targetProjectId WHERE project_id = @projectId", connection, transaction);
        planCommand.Parameters.AddWithValue("projectId", projectId);
        planCommand.Parameters.AddWithValue("targetProjectId", targetProjectId);
        await planCommand.ExecuteNonQueryAsync(cancellationToken);

        await using var memoryCommand = new NpgsqlCommand("UPDATE memories SET project_id = @targetProjectId WHERE project_id = @projectId", connection, transaction);
        memoryCommand.Parameters.AddWithValue("projectId", projectId);
        memoryCommand.Parameters.AddWithValue("targetProjectId", targetProjectId);
        await memoryCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<(int PlanCount, int TaskCount, long MemoryCount, int DocumentCount, int ChunkCount)> CountProjectDataAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string projectId,
        CancellationToken cancellationToken)
    {
        const string planCountSql = "SELECT COUNT(*)::int FROM plans WHERE project_id = @projectId";
        const string taskCountSql = "SELECT COUNT(t.id)::int FROM tasks t INNER JOIN plans p ON p.id = t.plan_id WHERE p.project_id = @projectId";
        const string memoryCountSql = "SELECT COUNT(*)::bigint FROM memories WHERE project_id = @projectId";
        var documentCountSql = $"SELECT COUNT(DISTINCT d.id)::int, COALESCE(COUNT(c.id), 0)::int FROM rag_documents d LEFT JOIN rag_chunks c ON d.id = c.document_id WHERE {DerivedDocumentProjectSql} = @projectId";

        var planCount = await ExecuteScalarAsync<int>(connection, transaction, planCountSql, projectId, cancellationToken);
        var taskCount = await ExecuteScalarAsync<int>(connection, transaction, taskCountSql, projectId, cancellationToken);
        var memoryCount = await ExecuteScalarAsync<long>(connection, transaction, memoryCountSql, projectId, cancellationToken);

        await using var documentCommand = new NpgsqlCommand(documentCountSql, connection, transaction);
        documentCommand.Parameters.AddWithValue("projectId", projectId);
        await using var reader = await documentCommand.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return (planCount, taskCount, memoryCount, 0, 0);
        }

        return (planCount, taskCount, memoryCount, reader.GetInt32(0), reader.GetInt32(1));
    }

    private static async Task<T> ExecuteScalarAsync<T>(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, string projectId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("projectId", projectId);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return (T)(value ?? throw new InvalidOperationException("Expected scalar result."));
    }

    private static bool IsProjectPrefixSourcePath(string sourcePath, string projectId)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return false;
        }

        return sourcePath.Equals(projectId, StringComparison.OrdinalIgnoreCase)
            || sourcePath.StartsWith($"{projectId}/", StringComparison.OrdinalIgnoreCase);
    }

    private static string RewriteProjectPrefix(string sourcePath, string projectId, string targetProjectId)
    {
        if (sourcePath.Equals(projectId, StringComparison.OrdinalIgnoreCase))
        {
            return targetProjectId;
        }

        return string.Concat(targetProjectId, sourcePath[projectId.Length..]);
    }

    private static string NormalizeProjectId(string? projectId)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            throw new ArgumentException("Project id cannot be null or empty.", nameof(projectId));
        }

        var normalized = projectId.Trim().Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("Project id cannot be null or empty.", nameof(projectId));
        }

        if (string.Equals(normalized, "unscoped", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The synthetic 'unscoped' project bucket cannot be renamed or deleted from the dashboard.");
        }

        return normalized;
    }
}