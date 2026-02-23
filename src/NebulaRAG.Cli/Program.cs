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
                        var mgmtService = new RagManagementService(store, mgmtLogger);
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
                        var mgmtService = new RagManagementService(store, mgmtLogger);
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
                        var mgmtService = new RagManagementService(store, mgmtLogger);
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
                            var mgmtService = new RagManagementService(store, mgmtLogger);
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
                        var mgmtService = new RagManagementService(store, mgmtLogger);
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
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --config <path>                       Path to configuration file");
        Console.WriteLine();
        Console.WriteLine("Example:");
        Console.WriteLine("  dotnet run -- index --source ./docs");
        Console.WriteLine("  dotnet run -- query --text 'How does indexing work?'");
        Console.WriteLine("  dotnet run -- stats");
    }
}
