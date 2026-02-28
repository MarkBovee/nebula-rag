using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Formatting.Compact;
using NebulaRAG.Core.Chunking;
using NebulaRAG.Core.Configuration;
using NebulaRAG.Core.Embeddings;
using NebulaRAG.Core.Exceptions;
using NebulaRAG.Core.Services;
using NebulaRAG.Core.Storage;
using Npgsql;

// Setup Serilog
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console(new CompactJsonFormatter())
    .CreateLogger();

try
{
    return await ProgramMain.RunAsync(args);
}
catch (RagException ex)
{
    Log.Error("Rag error [{Code}]: {Message}", ex.ErrorCode, ex.Message);
    Console.Error.WriteLine($"✗ Error [{ex.ErrorCode}]: {ex.Message}");
    return 1;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Unexpected error");
    Console.Error.WriteLine($"✗ Unexpected error: {ex.Message}");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}

internal static class ProgramMain
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        var command = args[0].ToLowerInvariant();
        var options = ParseOptions(args[1..]);
        
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddSerilog();
            builder.SetMinimumLevel(LogLevel.Information);
        });
        var logger = loggerFactory.CreateLogger("NebulaRAG.CLI");

        try
        {
            var settings = LoadSettings(options.TryGetValue("config", out var configPath) ? configPath : null);
            settings.Validate();
            logger.LogInformation("Configuration loaded and validated");

            var store = new PostgresRagStore(settings.Database.BuildConnectionString());
            var chunker = new TextChunker();
            var embeddingGenerator = new HashEmbeddingGenerator();
            var sourcesManifestLogger = loggerFactory.CreateLogger<RagSourcesManifestService>();
            var sourcesManifestService = new RagSourcesManifestService(store, settings, sourcesManifestLogger);

            switch (command)
            {
                case "init":
                    logger.LogInformation("Initializing database schema");
                    await store.InitializeSchemaAsync(settings.Ingestion.VectorDimensions);
                    await TrySyncRagSourcesManifestAsync(sourcesManifestService, null, logger);
                    logger.LogInformation("Database schema initialized successfully");
                    Console.WriteLine("✓ NebulaRAG schema initialized.");
                    return 0;

                case "index":
                    {
                        var source = options.TryGetValue("source", out var sourceDirectory)
                            ? sourceDirectory
                            : Directory.GetCurrentDirectory();
                        var indexerLogger = loggerFactory.CreateLogger<RagIndexer>();
                        var indexer = new RagIndexer(store, chunker, embeddingGenerator, settings, indexerLogger);
                        var summary = await indexer.IndexDirectoryAsync(source);
                        await TrySyncRagSourcesManifestAsync(sourcesManifestService, source, logger);
                        Console.WriteLine(
                            $"✓ Index complete: {summary.DocumentsIndexed} documents indexed, " +
                            $"{summary.ChunksIndexed} chunks, {summary.DocumentsSkipped} skipped.");
                        return 0;
                    }

                case "query":
                    {
                        if (!options.TryGetValue("text", out var queryText) || string.IsNullOrWhiteSpace(queryText))
                        {
                            Console.Error.WriteLine("✗ Missing required option --text for query command.");
                            return 1;
                        }

                        var topK = settings.Retrieval.DefaultTopK;
                        if (options.TryGetValue("limit", out var limitValue) &&
                            int.TryParse(limitValue, out var parsedLimit) &&
                            parsedLimit > 0)
                        {
                            topK = parsedLimit;
                        }

                        var queryLogger = loggerFactory.CreateLogger<RagQueryService>();
                        var queryService = new RagQueryService(store, embeddingGenerator, settings, queryLogger);
                        var results = await queryService.QueryAsync(queryText, topK);

                        if (results.Count == 0)
                        {
                            Console.WriteLine("No matches found.");
                            return 0;
                        }

                        foreach (var result in results)
                        {
                            var snippet = result.ChunkText.Replace('\n', ' ').Trim();
                            if (snippet.Length > 220)
                            {
                                snippet = $"{snippet[..220]}...";
                            }

                            Console.WriteLine($"[{result.Score:F4}] {result.SourcePath}#{result.ChunkIndex}");
                            Console.WriteLine(snippet);
                            Console.WriteLine();
                        }

                        return 0;
                    }

                case "stats":
                    {
                        var mgmtLogger = loggerFactory.CreateLogger<RagManagementService>();
                           var mgmtService = new RagManagementService(store, embeddingGenerator, settings, mgmtLogger);
                        var stats = await mgmtService.GetStatsAsync();
                        Console.WriteLine($"✓ Index Statistics:");
                        Console.WriteLine($"  Documents: {stats.DocumentCount}");
                        Console.WriteLine($"  Chunks: {stats.ChunkCount}");
                        Console.WriteLine($"  Total Tokens: {stats.TotalTokens:N0}");
                        if (stats.OldestIndexedAt.HasValue)
                            Console.WriteLine($"  Oldest: {stats.OldestIndexedAt:g}");
                        if (stats.NewestIndexedAt.HasValue)
                            Console.WriteLine($"  Newest: {stats.NewestIndexedAt:g}");
                        return 0;
                    }

                case "list-sources":
                    {
                        var mgmtLogger = loggerFactory.CreateLogger<RagManagementService>();
                           var mgmtService = new RagManagementService(store, embeddingGenerator, settings, mgmtLogger);
                        var sources = await mgmtService.ListSourcesAsync();
                        if (sources.Count == 0)
                        {
                            Console.WriteLine("No indexed sources.");
                            return 0;
                        }
                        Console.WriteLine("✓ Indexed Sources:");
                        foreach (var src in sources)
                        {
                            Console.WriteLine($"  {src.SourcePath} | {src.ChunkCount} chunks | {src.IndexedAt:g}");
                        }
                        return 0;
                    }

                case "delete":
                    {
                        if (!options.TryGetValue("source", out var deleteSource) || string.IsNullOrWhiteSpace(deleteSource))
                        {
                            Console.Error.WriteLine("✗ Missing required option --source for delete command.");
                            return 1;
                        }
                        var mgmtLogger = loggerFactory.CreateLogger<RagManagementService>();
                           var mgmtService = new RagManagementService(store, embeddingGenerator, settings, mgmtLogger);
                        var result = await mgmtService.DeleteSourceAsync(deleteSource);
                        if (result > 0)
                        {
                            await TrySyncRagSourcesManifestAsync(sourcesManifestService, deleteSource, logger);
                            Console.WriteLine($"✓ Deleted {result} documents.");
                        }
                        else
                        {
                            Console.WriteLine("No documents found for that source.");
                        }
                        return 0;
                    }

                case "purge-all":
                    {
                        Console.Write("⚠ WARNING: This will delete ALL indexed data. Type 'yes' to confirm: ");
                        var confirmation = Console.ReadLine();
                        if (confirmation == "yes")
                        {
                            var mgmtLogger = loggerFactory.CreateLogger<RagManagementService>();
                               var mgmtService = new RagManagementService(store, embeddingGenerator, settings, mgmtLogger);
                            await mgmtService.PurgeAllAsync();
                            await TrySyncRagSourcesManifestAsync(sourcesManifestService, null, logger);
                            Console.WriteLine("✓ Database purged successfully.");
                        }
                        else
                        {
                            Console.WriteLine("Cancelled.");
                        }
                        return 0;
                    }

                case "health-check":
                    {
                        var mgmtLogger = loggerFactory.CreateLogger<RagManagementService>();
                           var mgmtService = new RagManagementService(store, embeddingGenerator, settings, mgmtLogger);
                        var health = await mgmtService.HealthCheckAsync();
                        if (health.IsHealthy)
                        {
                            Console.WriteLine($"✓ {health.Message}");
                            return 0;
                        }
                        else
                        {
                            Console.Error.WriteLine($"✗ {health.Message}");
                            return 1;
                        }
                    }

                case "repair-source-prefix":
                    {
                        if (!options.TryGetValue("from", out var fromPrefix) || string.IsNullOrWhiteSpace(fromPrefix))
                        {
                            Console.Error.WriteLine("✗ Missing required option --from for repair-source-prefix command.");
                            return 1;
                        }

                        if (!options.TryGetValue("to", out var toPrefix) || string.IsNullOrWhiteSpace(toPrefix))
                        {
                            Console.Error.WriteLine("✗ Missing required option --to for repair-source-prefix command.");
                            return 1;
                        }

                        var rewriteResult = await store.RewriteSourcePathPrefixAsync(fromPrefix, toPrefix);
                        await TrySyncRagSourcesManifestAsync(sourcesManifestService, null, logger);
                        Console.WriteLine(
                            $"✓ Source prefix repair complete: {rewriteResult.UpdatedCount} updated, {rewriteResult.DuplicatesRemoved} duplicates removed.");
                        return 0;
                    }

                case "clone-db":
                    {
                        var sourceDatabase = options.TryGetValue("from", out var fromDatabase) && !string.IsNullOrWhiteSpace(fromDatabase)
                            ? fromDatabase.Trim()
                            : "brewmind";
                        var targetDatabase = options.TryGetValue("to", out var toDatabase) && !string.IsNullOrWhiteSpace(toDatabase)
                            ? toDatabase.Trim()
                            : settings.Database.Database;

                        var force = !options.TryGetValue("force", out var forceValue) || !string.Equals(forceValue, "false", StringComparison.OrdinalIgnoreCase);

                        var verification = await CloneDatabaseAsync(settings.Database, sourceDatabase, targetDatabase, force, logger);

                        Console.WriteLine($"✓ Cloned database '{sourceDatabase}' -> '{targetDatabase}'.");
                        Console.WriteLine($"  rag_documents: {verification.Source.RagDocuments}");
                        Console.WriteLine($"  rag_chunks: {verification.Source.RagChunks}");
                        Console.WriteLine($"  memories: {verification.Source.Memories}");
                        Console.WriteLine($"  plans: {verification.Source.Plans}");
                        Console.WriteLine($"  tasks: {verification.Source.Tasks}");
                        Console.WriteLine($"  plan_history: {verification.Source.PlanHistory}");
                        Console.WriteLine($"  task_history: {verification.Source.TaskHistory}");
                        Console.WriteLine("✓ Verification: key table counts match.");
                        return 0;
                    }

                default:
                    Console.Error.WriteLine($"✗ Unknown command: {command}");
                    PrintUsage();
                    return 1;
            }
        }
        catch (RagConfigurationException ex)
        {
            logger.LogError("Configuration error: {Message}", ex.Message);
            Console.Error.WriteLine($"✗ Configuration error: {ex.Message}");
            return 1;
        }
        catch (RagDatabaseException ex)
        {
            logger.LogError("Database error: {Message}", ex.Message);
            Console.Error.WriteLine($"✗ Database error: {ex.Message}");
            return 1;
        }
        catch (RagException ex)
        {
            logger.LogError("Error [{Code}]: {Message}", ex.ErrorCode, ex.Message);
            Console.Error.WriteLine($"✗ Error [{ex.ErrorCode}]: {ex.Message}");
            return 1;
        }
    }

    private static RagSettings LoadSettings(string? configPath)
    {
        var configBuilder = new ConfigurationBuilder();

        if (!string.IsNullOrWhiteSpace(configPath))
        {
            var absoluteConfigPath = Path.IsPathRooted(configPath)
                ? configPath
                : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), configPath));

            configBuilder.AddJsonFile(absoluteConfigPath, optional: false, reloadOnChange: false);

            var configDirectory = Path.GetDirectoryName(absoluteConfigPath);
            if (!string.IsNullOrWhiteSpace(configDirectory))
            {
                var localOverridePath = Path.Combine(configDirectory, "ragsettings.local.json");
                if (File.Exists(localOverridePath))
                {
                    configBuilder.AddJsonFile(localOverridePath, optional: true, reloadOnChange: false);
                }
            }
        }
        else
        {
            var baseConfigPath = ResolveConfigPath("ragsettings.json", required: true)
                                 ?? throw new FileNotFoundException("Could not locate required config file 'ragsettings.json'.");
            configBuilder.AddJsonFile(baseConfigPath, optional: false, reloadOnChange: false);

            var localConfigPath = ResolveConfigPath("ragsettings.local.json", required: false);
            if (localConfigPath is not null)
            {
                configBuilder.AddJsonFile(localConfigPath, optional: true, reloadOnChange: false);
            }
        }

        configBuilder.AddEnvironmentVariables(prefix: "NEBULARAG_");

        var settings = configBuilder.Build().Get<RagSettings>();
        return settings ?? new RagSettings();
    }

    private static string? ResolveConfigPath(string fileName, bool required)
    {
        var candidates = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), fileName),
            Path.Combine(AppContext.BaseDirectory, fileName),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", fileName))
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        if (required)
        {
            throw new FileNotFoundException($"Could not locate required config file '{fileName}'.");
        }

        return null;
    }

    private static Dictionary<string, string> ParseOptions(IReadOnlyList<string> args)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var key = arg[2..];
            var hasValue = i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal);
            options[key] = hasValue ? args[++i] : "true";
        }

        return options;
    }

    /// <summary>
    /// Synchronizes rag-sources markdown after index mutations without failing the main command when sync fails.
    /// </summary>
    /// <param name="sourcesManifestService">Service that writes rag-sources markdown from indexed metadata.</param>
    /// <param name="contextPath">Optional path context that helps resolve output location.</param>
    /// <param name="logger">Command logger for non-fatal warnings.</param>
    private static async Task TrySyncRagSourcesManifestAsync(RagSourcesManifestService sourcesManifestService, string? contextPath, Microsoft.Extensions.Logging.ILogger logger)
    {
        try
        {
            var syncResult = await sourcesManifestService.SyncAsync(contextPath);
            logger.LogInformation("RAG sources manifest synchronized at {ManifestPath} ({SourceCount} rows).", syncResult.ManifestPath, syncResult.SourceCount);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to synchronize rag-sources.md automatically.");
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Nebula RAG CLI - v0.2.0 (Phase 1)");
        Console.WriteLine("Usage:");
        Console.WriteLine();
        Console.WriteLine("Core Commands:");
        Console.WriteLine("  init                                  Initialize database schema");
        Console.WriteLine("  index [--source <directory>]          Index documents from directory");
        Console.WriteLine("  query --text <query> [--limit <n>]    Execute semantic search");
        Console.WriteLine();
        Console.WriteLine("Management Commands:");
        Console.WriteLine("  stats                                 Show index statistics");
        Console.WriteLine("  list-sources                          List all indexed sources");
        Console.WriteLine("  delete --source <path>                Delete documents by source path");
        Console.WriteLine("  purge-all                             Clear entire index (with confirmation)");
        Console.WriteLine("  health-check                          Verify database connectivity");
        Console.WriteLine("  repair-source-prefix --from <p> --to <p>  Rewrite stored source-path prefixes");
        Console.WriteLine("  clone-db [--from <db>] [--to <db>] [--force true|false]  Clone source DB into target DB and verify counts");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --config <path>                       Path to configuration file");
        Console.WriteLine();
        Console.WriteLine("Example:");
        Console.WriteLine("  dotnet run -- index --source ./docs");
        Console.WriteLine("  dotnet run -- query --text 'How does indexing work?'");
        Console.WriteLine("  dotnet run -- stats");
    }

    /// <summary>
    /// Clones a source PostgreSQL database into a target database and verifies core table counts.
    /// </summary>
    /// <param name="databaseSettings">Database connection settings.</param>
    /// <param name="sourceDatabase">Source database name.</param>
    /// <param name="targetDatabase">Target database name.</param>
    /// <param name="force">When true, drops existing target database before cloning.</param>
    /// <param name="logger">Logger instance.</param>
    /// <returns>Verification payload containing source and target key table counts.</returns>
    private static async Task<CloneVerification> CloneDatabaseAsync(DatabaseSettings databaseSettings, string sourceDatabase, string targetDatabase, bool force, Microsoft.Extensions.Logging.ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(sourceDatabase))
        {
            throw new ArgumentException("Source database is required.", nameof(sourceDatabase));
        }

        if (string.IsNullOrWhiteSpace(targetDatabase))
        {
            throw new ArgumentException("Target database is required.", nameof(targetDatabase));
        }

        if (string.Equals(sourceDatabase, targetDatabase, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Source and target database must differ.");
        }

        var builder = new NpgsqlConnectionStringBuilder(databaseSettings.BuildConnectionString())
        {
            Database = "postgres"
        };

        await using var adminConnection = new NpgsqlConnection(builder.ConnectionString);
        await adminConnection.OpenAsync();

        await EnsureDatabaseExistsAsync(adminConnection, sourceDatabase);
        await HandleTargetDatabaseAsync(adminConnection, targetDatabase, force);

        await using (var createCommand = adminConnection.CreateCommand())
        {
            createCommand.CommandText = $"CREATE DATABASE \"{targetDatabase.Replace("\"", "\"\"")}\" WITH TEMPLATE \"{sourceDatabase.Replace("\"", "\"\"")}\";";
            await createCommand.ExecuteNonQueryAsync();
        }

        logger.LogInformation("Database clone complete: {SourceDatabase} -> {TargetDatabase}", sourceDatabase, targetDatabase);

        var sourceCounts = await ReadCoreCountsAsync(builder, sourceDatabase);
        var targetCounts = await ReadCoreCountsAsync(builder, targetDatabase);

        if (!sourceCounts.Equals(targetCounts))
        {
            throw new InvalidOperationException("Database clone verification failed: source and target counts differ.");
        }

        return new CloneVerification(sourceCounts, targetCounts);
    }

    /// <summary>
    /// Ensures the specified database exists.
    /// </summary>
    /// <param name="adminConnection">Open admin connection to postgres database.</param>
    /// <param name="databaseName">Database name to validate.</param>
    private static async Task EnsureDatabaseExistsAsync(NpgsqlConnection adminConnection, string databaseName)
    {
        await using var command = adminConnection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM pg_database WHERE datname = @db);";
        command.Parameters.AddWithValue("db", databaseName);
        var exists = (bool)(await command.ExecuteScalarAsync() ?? false);
        if (!exists)
        {
            throw new InvalidOperationException($"Source database '{databaseName}' does not exist.");
        }
    }

    /// <summary>
    /// Drops the target database when present and force is enabled.
    /// </summary>
    /// <param name="adminConnection">Open admin connection to postgres database.</param>
    /// <param name="targetDatabase">Target database name.</param>
    /// <param name="force">Force drop existing target database.</param>
    private static async Task HandleTargetDatabaseAsync(NpgsqlConnection adminConnection, string targetDatabase, bool force)
    {
        await using var existsCommand = adminConnection.CreateCommand();
        existsCommand.CommandText = "SELECT EXISTS(SELECT 1 FROM pg_database WHERE datname = @db);";
        existsCommand.Parameters.AddWithValue("db", targetDatabase);
        var targetExists = (bool)(await existsCommand.ExecuteScalarAsync() ?? false);

        if (!targetExists)
        {
            return;
        }

        if (!force)
        {
            throw new InvalidOperationException($"Target database '{targetDatabase}' already exists. Use --force true to overwrite.");
        }

        await using (var terminateCommand = adminConnection.CreateCommand())
        {
            terminateCommand.CommandText =
                """
                SELECT pg_terminate_backend(pid)
                FROM pg_stat_activity
                WHERE datname = @db
                  AND pid <> pg_backend_pid();
                """;
            terminateCommand.Parameters.AddWithValue("db", targetDatabase);
            await terminateCommand.ExecuteNonQueryAsync();
        }

        await using var dropCommand = adminConnection.CreateCommand();
        dropCommand.CommandText = $"DROP DATABASE \"{targetDatabase.Replace("\"", "\"\"")}\";";
        await dropCommand.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Reads key table counts used to verify clone integrity.
    /// </summary>
    /// <param name="baseConnectionBuilder">Connection string builder with server credentials.</param>
    /// <param name="database">Database to inspect.</param>
    /// <returns>Key table count payload.</returns>
    private static async Task<CoreTableCounts> ReadCoreCountsAsync(NpgsqlConnectionStringBuilder baseConnectionBuilder, string database)
    {
        var databaseBuilder = new NpgsqlConnectionStringBuilder(baseConnectionBuilder.ConnectionString)
        {
            Database = database
        };

        await using var connection = new NpgsqlConnection(databaseBuilder.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                (SELECT COUNT(*) FROM public.rag_documents)::bigint,
                (SELECT COUNT(*) FROM public.rag_chunks)::bigint,
                (SELECT COUNT(*) FROM public.memories)::bigint,
                (SELECT COUNT(*) FROM public.plans)::bigint,
                (SELECT COUNT(*) FROM public.tasks)::bigint,
                (SELECT COUNT(*) FROM public.plan_history)::bigint,
                (SELECT COUNT(*) FROM public.task_history)::bigint;
            """;

        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();

        return new CoreTableCounts(
            RagDocuments: reader.GetInt64(0),
            RagChunks: reader.GetInt64(1),
            Memories: reader.GetInt64(2),
            Plans: reader.GetInt64(3),
            Tasks: reader.GetInt64(4),
            PlanHistory: reader.GetInt64(5),
            TaskHistory: reader.GetInt64(6));
    }

    /// <summary>
    /// Core table counts used for clone validation.
    /// </summary>
    /// <param name="RagDocuments">Count of rag_documents rows.</param>
    /// <param name="RagChunks">Count of rag_chunks rows.</param>
    /// <param name="Memories">Count of memories rows.</param>
    /// <param name="Plans">Count of plans rows.</param>
    /// <param name="Tasks">Count of tasks rows.</param>
    /// <param name="PlanHistory">Count of plan_history rows.</param>
    /// <param name="TaskHistory">Count of task_history rows.</param>
    private sealed record CoreTableCounts(long RagDocuments, long RagChunks, long Memories, long Plans, long Tasks, long PlanHistory, long TaskHistory);

    /// <summary>
    /// Clone verification payload for source and target table counts.
    /// </summary>
    /// <param name="Source">Source database table counts.</param>
    /// <param name="Target">Target database table counts.</param>
    private sealed record CloneVerification(CoreTableCounts Source, CoreTableCounts Target);
}
