using Npgsql;

namespace NebulaRAG.Core.Configuration;

public sealed class RagSettings
{
    public DatabaseSettings Database { get; init; } = new();
    public IngestionSettings Ingestion { get; init; } = new();
    public RetrievalSettings Retrieval { get; init; } = new();
}

public sealed class DatabaseSettings
{
    public string Host { get; init; } = "localhost";
    public int Port { get; init; } = 5432;
    public string Database { get; init; } = "nebularag";
    public string Username { get; init; } = "postgres";
    public string Password { get; init; } = string.Empty;
    public string SslMode { get; init; } = "Prefer";

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
}

public sealed class IngestionSettings
{
    public int VectorDimensions { get; init; } = 256;
    public int ChunkSize { get; init; } = 1200;
    public int ChunkOverlap { get; init; } = 200;
    public int MaxFileSizeBytes { get; init; } = 1_000_000;
    public List<string> IncludeExtensions { get; init; } =
    [
        ".md", ".txt", ".cs", ".json", ".ts", ".tsx", ".js", ".py"
    ];
}

public sealed class RetrievalSettings
{
    public int DefaultTopK { get; init; } = 5;
}
