using Microsoft.Extensions.Configuration;
using NebulaRAG.Core.Chunking;
using NebulaRAG.Core.Configuration;
using NebulaRAG.Core.Embeddings;
using NebulaRAG.Core.Services;
using NebulaRAG.Core.Storage;

return await ProgramMain.RunAsync(args);

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
        var settings = LoadSettings(options.TryGetValue("config", out var configPath) ? configPath : null);

        var store = new PostgresRagStore(settings.Database.BuildConnectionString());
        var chunker = new TextChunker();
        var embeddingGenerator = new HashEmbeddingGenerator();

        switch (command)
        {
            case "init":
                await store.InitializeSchemaAsync(settings.Ingestion.VectorDimensions);
                Console.WriteLine("NebulaRAG schema initialized.");
                return 0;

            case "index":
                var source = options.TryGetValue("source", out var sourceDirectory)
                    ? sourceDirectory
                    : Directory.GetCurrentDirectory();

                var indexer = new RagIndexer(store, chunker, embeddingGenerator, settings);
                var summary = await indexer.IndexDirectoryAsync(source);
                Console.WriteLine(
                    $"Index complete. Documents indexed: {summary.DocumentsIndexed}, " +
                    $"documents skipped: {summary.DocumentsSkipped}, chunks indexed: {summary.ChunksIndexed}.");
                return 0;

            case "query":
                if (!options.TryGetValue("text", out var queryText) || string.IsNullOrWhiteSpace(queryText))
                {
                    Console.Error.WriteLine("Missing required option --text for query command.");
                    return 1;
                }

                var topK = settings.Retrieval.DefaultTopK;
                if (options.TryGetValue("limit", out var limitValue) &&
                    int.TryParse(limitValue, out var parsedLimit) &&
                    parsedLimit > 0)
                {
                    topK = parsedLimit;
                }

                var queryService = new RagQueryService(store, embeddingGenerator, settings);
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

            default:
                Console.Error.WriteLine($"Unknown command: {command}");
                PrintUsage();
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

    private static void PrintUsage()
    {
        Console.WriteLine("Nebula RAG CLI");
        Console.WriteLine("Usage:");
        Console.WriteLine("  nebularag init [--config <path>]");
        Console.WriteLine("  nebularag index [--source <directory>] [--config <path>]");
        Console.WriteLine("  nebularag query --text <query> [--limit <n>] [--config <path>]");
    }
}
