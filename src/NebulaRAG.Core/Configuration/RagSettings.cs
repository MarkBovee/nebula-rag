using System.ComponentModel.DataAnnotations;
using Npgsql;
using NebulaRAG.Core.Exceptions;

namespace NebulaRAG.Core.Configuration;

/// <summary>
/// Root configuration settings for NebulaRAG.
/// </summary>
public sealed class RagSettings
{
    public DatabaseSettings Database { get; init; } = new();
    public IngestionSettings Ingestion { get; init; } = new();
    public RetrievalSettings Retrieval { get; init; } = new();

    /// <summary>
    /// Validates all configuration settings.
    /// </summary>
    /// <exception cref="RagConfigurationException">Thrown if any settings are invalid.</exception>
    public void Validate()
    {
        var errors = new List<string>();

        if (Database == null)
            errors.Add("Database settings are required.");
        else
            Database.Validate(errors);

        if (Ingestion == null)
            errors.Add("Ingestion settings are required.");
        else
            Ingestion.Validate(errors);

        if (Retrieval == null)
            errors.Add("Retrieval settings are required.");
        else
            Retrieval.Validate(errors);

        if (errors.Count > 0)
        {
            var message = string.Join("; ", errors);
            throw new RagConfigurationException($"Configuration validation failed: {message}");
        }
    }
}

/// <summary>
/// Database connection settings.
/// </summary>
public sealed class DatabaseSettings
{
    public string Host { get; init; } = "localhost";
    public int Port { get; init; } = 5432;
    public string Database { get; init; } = "nebularag";
    public string Username { get; init; } = "postgres";
    public string Password { get; init; } = string.Empty;
    public string SslMode { get; init; } = "Prefer";

    /// <summary>
    /// Builds a valid PostgreSQL connection string.
    /// </summary>
    public string BuildConnectionString()
    {
        var sslMode = Enum.TryParse<SslMode>(SslMode, true, out var parsedSslMode)
            ? parsedSslMode
            : Npgsql.SslMode.Prefer;

        return new NpgsqlConnectionStringBuilder
        {
            Host = Host,
            Port = Port,
            Database = Database,
            Username = Username,
            Password = Password,
            SslMode = sslMode,
            Timeout = 15,
            CommandTimeout = 60
        }.ConnectionString;
    }

    internal void Validate(List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(Host))
            errors.Add("Database.Host is required.");
        if (Port <= 0 || Port > 65535)
            errors.Add("Database.Port must be between 1 and 65535.");
        if (string.IsNullOrWhiteSpace(Database))
            errors.Add("Database.Database is required.");
        if (string.IsNullOrWhiteSpace(Username))
            errors.Add("Database.Username is required.");
        if (string.IsNullOrWhiteSpace(Password))
            errors.Add("Database.Password is required (set in ragsettings.local.json or NEBULARAG_Database__Password env var).");
        if (!Enum.TryParse<SslMode>(SslMode, true, out _))
            errors.Add($"Database.SslMode '{SslMode}' is not a valid SSL mode (Disable, Allow, Require, Prefer).");
    }
}

/// <summary>
/// Document ingestion and embedding settings.
/// </summary>
public sealed class IngestionSettings
{
    public int VectorDimensions { get; init; } = 256;
    public string EmbeddingModel { get; init; } = "nomic-embed-text";
    public int ChunkSize { get; init; } = 1200;
    public int ChunkOverlap { get; init; } = 200;
    public int MaxFileSizeBytes { get; init; } = 1_000_000;
    public List<string> IncludeExtensions { get; init; } =
    [
        ".md", ".txt", ".cs", ".json", ".ts", ".tsx", ".js", ".py"
    ];

    public List<string> ExcludeDirectories { get; init; } =
    [
        "bin", "obj", ".git", "node_modules", ".next", "dist", "build"
    ];

    internal void Validate(List<string> errors)
    {
        if (VectorDimensions <= 0 || VectorDimensions > 4096)
            errors.Add("Ingestion.VectorDimensions must be between 1 and 4096.");
        if (string.IsNullOrWhiteSpace(EmbeddingModel))
            errors.Add("Ingestion.EmbeddingModel is required.");
        if (ChunkSize < 100 || ChunkSize > 5000)
            errors.Add("Ingestion.ChunkSize must be between 100 and 5000.");
        if (ChunkOverlap < 0 || ChunkOverlap >= ChunkSize)
            errors.Add("Ingestion.ChunkOverlap must be between 0 and ChunkSize-1.");
        if (MaxFileSizeBytes < 100_000 || MaxFileSizeBytes > 100_000_000)
            errors.Add("Ingestion.MaxFileSizeBytes must be between 100KB and 100MB.");
        if (IncludeExtensions == null || IncludeExtensions.Count == 0)
            errors.Add("Ingestion.IncludeExtensions must have at least one extension.");
        if (ExcludeDirectories == null)
            errors.Add("Ingestion.ExcludeDirectories must not be null.");
    }
}

/// <summary>
/// Query and retrieval settings.
/// </summary>
public sealed class RetrievalSettings
{
    public int DefaultTopK { get; init; } = 5;

    internal void Validate(List<string> errors)
    {
        if (DefaultTopK <= 0 || DefaultTopK > 100)
            errors.Add("Retrieval.DefaultTopK must be between 1 and 100.");
    }
}
