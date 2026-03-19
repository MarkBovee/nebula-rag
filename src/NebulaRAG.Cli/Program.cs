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
    .MinimumLevel.Warning()
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
            builder.SetMinimumLevel(LogLevel.Warning);
        });
        var logger = loggerFactory.CreateLogger("NebulaRAG.CLI");

        var dotEnvResult = DotEnvLoader.LoadStandardDotEnv();
        if (dotEnvResult.FoundFile)
        {
            logger.LogDebug("Loaded .env from {Path}. Applied {LoadedCount} keys ({SkippedCount} skipped due to existing values).", dotEnvResult.Path, dotEnvResult.LoadedCount, dotEnvResult.SkippedCount);
        }
        else
        {
            Console.Error.WriteLine("! No .env file found. Using existing process environment variables only.");
        }

        try
        {
            var settings = LoadSettings(options.TryGetValue("config", out var configPath) ? configPath : null);
            settings.Validate();
            logger.LogDebug("Configuration loaded and validated");

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

                case "preload":
                    {
                        var indexerLogger = loggerFactory.CreateLogger<RagIndexer>();
                        var indexer = new RagIndexer(store, chunker, embeddingGenerator, settings, indexerLogger);
                        return await ExecutePreloadAsync(options, settings, indexer, sourcesManifestService, logger);
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
                           var mgmtService = new RagManagementService(store, chunker, embeddingGenerator, settings, mgmtLogger);
                        var stats = await mgmtService.GetStatsAsync();
                        var projectStats = await mgmtService.GetProjectRagStatsAsync();
                        Console.WriteLine($"✓ Index Statistics:");
                        Console.WriteLine($"  Documents: {stats.DocumentCount}");
                        Console.WriteLine($"  Chunks: {stats.ChunkCount}");
                        Console.WriteLine($"  Total Tokens: {stats.TotalTokens:N0}");
                        Console.WriteLine($"  Projects: {stats.ProjectCount}");
                        if (stats.OldestIndexedAt.HasValue)
                            Console.WriteLine($"  Oldest: {stats.OldestIndexedAt:g}");
                        if (stats.NewestIndexedAt.HasValue)
                            Console.WriteLine($"  Newest: {stats.NewestIndexedAt:g}");

                        if (projectStats.Count > 0)
                        {
                            Console.WriteLine();
                            Console.WriteLine("✓ Project Breakdown:");
                            foreach (var project in projectStats)
                            {
                                var latestIndexedAt = project.NewestIndexedAt.HasValue ? project.NewestIndexedAt.Value.ToString("g") : "n/a";
                                Console.WriteLine($"  {project.ProjectId} | docs: {project.DocumentCount} | chunks: {project.ChunkCount} | tokens: {project.TotalTokens:N0} | newest: {latestIndexedAt}");
                            }
                        }
                        return 0;
                    }

                case "list-sources":
                    {
                        var mgmtLogger = loggerFactory.CreateLogger<RagManagementService>();
                           var mgmtService = new RagManagementService(store, chunker, embeddingGenerator, settings, mgmtLogger);
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
                           var mgmtService = new RagManagementService(store, chunker, embeddingGenerator, settings, mgmtLogger);
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
                               var mgmtService = new RagManagementService(store, chunker, embeddingGenerator, settings, mgmtLogger);
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
                           var mgmtService = new RagManagementService(store, chunker, embeddingGenerator, settings, mgmtLogger);
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
    /// Executes preload flow by auto-detecting project roots and prompting the user when confidence is low.
    /// </summary>
    /// <param name="options">Command-line options.</param>
    /// <param name="settings">Resolved RAG settings.</param>
    /// <param name="indexer">Indexer service.</param>
    /// <param name="sourcesManifestService">RAG sources manifest service.</param>
    /// <param name="logger">Command logger.</param>
    /// <returns>Exit code.</returns>
    private static async Task<int> ExecutePreloadAsync(Dictionary<string, string> options, RagSettings settings, RagIndexer indexer, RagSourcesManifestService sourcesManifestService, Microsoft.Extensions.Logging.ILogger logger)
    {
        if (options.TryGetValue("source", out var explicitSource) && !string.IsNullOrWhiteSpace(explicitSource))
        {
            return await RunPreloadForPathsAsync([explicitSource.Trim()], indexer, sourcesManifestService, logger, dryRun: IsOptionEnabled(options, "dry-run"));
        }

        var currentDirectory = Directory.GetCurrentDirectory();
        var candidates = DetectPreloadCandidates(currentDirectory, settings);
        var selection = SelectPreloadPaths(candidates, currentDirectory);

        if (!selection.IsConfirmed)
        {
            return 1;
        }

        return await RunPreloadForPathsAsync(selection.Paths, indexer, sourcesManifestService, logger, dryRun: IsOptionEnabled(options, "dry-run"));
    }

    /// <summary>
    /// Indexes one or more selected paths and emits an aggregate summary.
    /// </summary>
    /// <param name="paths">Paths selected for preload.</param>
    /// <param name="indexer">Indexer service.</param>
    /// <param name="sourcesManifestService">Manifest synchronization service.</param>
    /// <param name="logger">Command logger.</param>
    /// <param name="dryRun">When true, prints what would be indexed without writing.</param>
    /// <returns>Exit code.</returns>
    private static async Task<int> RunPreloadForPathsAsync(IReadOnlyList<string> paths, RagIndexer indexer, RagSourcesManifestService sourcesManifestService, Microsoft.Extensions.Logging.ILogger logger, bool dryRun)
    {
        var normalizedPaths = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalizedPaths.Count == 0)
        {
            Console.Error.WriteLine("✗ No preload paths were selected.");
            return 1;
        }

        if (dryRun)
        {
            Console.WriteLine("✓ Preload dry-run plan:");
            foreach (var path in normalizedPaths)
            {
                Console.WriteLine($"  - {path}");
            }

            Console.WriteLine("No indexing changes were written.");
            return 0;
        }

        var totalIndexed = 0;
        var totalSkipped = 0;
        var totalChunks = 0;

        foreach (var path in normalizedPaths)
        {
            var summary = await indexer.IndexDirectoryAsync(path);
            await TrySyncRagSourcesManifestAsync(sourcesManifestService, path, logger);
            totalIndexed += summary.DocumentsIndexed;
            totalSkipped += summary.DocumentsSkipped;
            totalChunks += summary.ChunksIndexed;
        }

        Console.WriteLine($"✓ Preload complete: {totalIndexed} documents indexed, {totalChunks} chunks, {totalSkipped} skipped.");
        return 0;
    }

    /// <summary>
    /// Detects likely preload candidates from the current folder and immediate child folders.
    /// </summary>
    /// <param name="workingDirectory">Current working directory.</param>
    /// <param name="settings">RAG settings used for extension and exclusion rules.</param>
    /// <returns>Candidate list sorted by confidence score descending.</returns>
    private static List<PreloadCandidate> DetectPreloadCandidates(string workingDirectory, RagSettings settings)
    {
        var includeExtensions = new HashSet<string>(settings.Ingestion.IncludeExtensions, StringComparer.OrdinalIgnoreCase);
        var excludedDirectories = new HashSet<string>(settings.Ingestion.ExcludeDirectories, StringComparer.OrdinalIgnoreCase);

        var rootsToEvaluate = new List<string> { Path.GetFullPath(workingDirectory) };
        try
        {
            rootsToEvaluate.AddRange(Directory.EnumerateDirectories(workingDirectory)
                .Take(8)
                .Select(Path.GetFullPath));
        }
        catch
        {
            // Ignore child directory discovery failures and keep current directory fallback.
        }

        var candidates = new List<PreloadCandidate>();
        foreach (var root in rootsToEvaluate.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            var hasGit = Directory.Exists(Path.Combine(root, ".git"));
            var hasSrc = Directory.Exists(Path.Combine(root, "src"));
            var hasDocs = Directory.Exists(Path.Combine(root, "docs"));
            var hasTests = Directory.Exists(Path.Combine(root, "tests")) || Directory.Exists(Path.Combine(root, "test"));
            var hasProjectMarkers = File.Exists(Path.Combine(root, "README.md"))
                || Directory.EnumerateFiles(root, "*.sln", SearchOption.TopDirectoryOnly).Any()
                || Directory.EnumerateFiles(root, "*.slnx", SearchOption.TopDirectoryOnly).Any()
                || Directory.EnumerateFiles(root, "*.csproj", SearchOption.TopDirectoryOnly).Any()
                || File.Exists(Path.Combine(root, "package.json"))
                || File.Exists(Path.Combine(root, "pyproject.toml"));

            var eligibleFiles = CountEligibleFiles(root, includeExtensions, excludedDirectories, maxFiles: 2500);
            if (!hasProjectMarkers && eligibleFiles == 0)
            {
                continue;
            }

            var score = 0;
            if (hasGit)
            {
                score += 30;
            }

            if (hasProjectMarkers)
            {
                score += 20;
            }

            if (hasSrc)
            {
                score += 12;
            }

            if (hasDocs)
            {
                score += 6;
            }

            if (hasTests)
            {
                score += 4;
            }

            score += Math.Min(30, eligibleFiles / 20);

            if (!string.Equals(root, workingDirectory, StringComparison.OrdinalIgnoreCase))
            {
                score -= 4;
            }

            var reason = BuildCandidateReason(hasGit, hasProjectMarkers, hasSrc, hasDocs, hasTests, eligibleFiles);
            candidates.Add(new PreloadCandidate(root, Math.Max(score, 0), eligibleFiles, reason));
        }

        return candidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => candidate.EligibleFileCount)
            .ToList();
    }

    /// <summary>
    /// Builds a short human-readable reason string describing why a path was selected.
    /// </summary>
    /// <param name="hasGit">Whether path contains a git repository.</param>
    /// <param name="hasProjectMarkers">Whether path has project marker files.</param>
    /// <param name="hasSrc">Whether path has a src directory.</param>
    /// <param name="hasDocs">Whether path has a docs directory.</param>
    /// <param name="hasTests">Whether path has tests directory.</param>
    /// <param name="eligibleFiles">Detected file count eligible for indexing.</param>
    /// <returns>Reason string.</returns>
    private static string BuildCandidateReason(bool hasGit, bool hasProjectMarkers, bool hasSrc, bool hasDocs, bool hasTests, int eligibleFiles)
    {
        var signals = new List<string>();
        if (hasGit)
        {
            signals.Add("git");
        }

        if (hasProjectMarkers)
        {
            signals.Add("project markers");
        }

        if (hasSrc)
        {
            signals.Add("src");
        }

        if (hasDocs)
        {
            signals.Add("docs");
        }

        if (hasTests)
        {
            signals.Add("tests");
        }

        signals.Add($"eligible files: {eligibleFiles}");
        return string.Join(", ", signals);
    }

    /// <summary>
    /// Selects preload paths, prompting the user only when detection confidence is low.
    /// </summary>
    /// <param name="candidates">Detected candidates.</param>
    /// <param name="workingDirectory">Current working directory fallback.</param>
    /// <returns>Selection result.</returns>
    private static (bool IsConfirmed, List<string> Paths) SelectPreloadPaths(IReadOnlyList<PreloadCandidate> candidates, string workingDirectory)
    {
        if (candidates.Count == 0)
        {
            Console.WriteLine("Could not confidently detect a project root for preload.");
            return PromptForPathSelection(candidates, workingDirectory, isUncertain: true);
        }

        var top = candidates[0];
        var secondScore = candidates.Count > 1 ? candidates[1].Score : 0;
        var confident = top.Score >= 45 && (candidates.Count == 1 || top.Score - secondScore >= 12);

        if (confident)
        {
            Console.WriteLine($"✓ Auto-detected preload source: {top.Path}");
            Console.WriteLine($"  Signals: {top.Reason}");
            return (true, [top.Path]);
        }

        Console.WriteLine("Preload detection found multiple possible sources.");
        return PromptForPathSelection(candidates, workingDirectory, isUncertain: true);
    }

    /// <summary>
    /// Prompts the user to select a detected source, all sources, or a custom path.
    /// </summary>
    /// <param name="candidates">Detected candidates.</param>
    /// <param name="workingDirectory">Fallback current directory path.</param>
    /// <param name="isUncertain">Whether the prompt is due to uncertain detection.</param>
    /// <returns>Selection result.</returns>
    private static (bool IsConfirmed, List<string> Paths) PromptForPathSelection(IReadOnlyList<PreloadCandidate> candidates, string workingDirectory, bool isUncertain)
    {
        if (isUncertain)
        {
            Console.WriteLine("Select what to preload:");
        }

        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            Console.WriteLine($"  {i + 1}) {candidate.Path}  [{candidate.Reason}]");
        }

        Console.WriteLine("  A) All detected sources");
        Console.WriteLine("  C) Custom path");
        Console.WriteLine($"  D) Current directory ({workingDirectory})");
        Console.WriteLine("  Q) Cancel");
        Console.Write("Choice (default 1): ");

        var input = (Console.ReadLine() ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            if (candidates.Count == 0)
            {
                return (true, [workingDirectory]);
            }

            return (true, [candidates[0].Path]);
        }

        if (string.Equals(input, "Q", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Cancelled.");
            return (false, []);
        }

        if (string.Equals(input, "A", StringComparison.OrdinalIgnoreCase))
        {
            var allPaths = candidates.Select(candidate => candidate.Path).ToList();
            if (allPaths.Count == 0)
            {
                allPaths.Add(workingDirectory);
            }

            return (true, allPaths);
        }

        if (string.Equals(input, "D", StringComparison.OrdinalIgnoreCase))
        {
            return (true, [workingDirectory]);
        }

        if (string.Equals(input, "C", StringComparison.OrdinalIgnoreCase))
        {
            Console.Write("Enter directory path to preload: ");
            var customPath = (Console.ReadLine() ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(customPath))
            {
                Console.Error.WriteLine("✗ Custom path is required.");
                return (false, []);
            }

            return (true, [customPath]);
        }

        if (int.TryParse(input, out var selectedIndex) && selectedIndex >= 1 && selectedIndex <= candidates.Count)
        {
            return (true, [candidates[selectedIndex - 1].Path]);
        }

        Console.Error.WriteLine("✗ Invalid selection.");
        return (false, []);
    }

    /// <summary>
    /// Counts index-eligible files under a root using current extension and exclusion rules.
    /// </summary>
    /// <param name="rootDirectory">Directory to inspect.</param>
    /// <param name="includeExtensions">Allowed extensions.</param>
    /// <param name="excludedDirectories">Excluded directory names.</param>
    /// <param name="maxFiles">Maximum file count to inspect before early exit.</param>
    /// <returns>Eligible file count up to maxFiles.</returns>
    private static int CountEligibleFiles(string rootDirectory, HashSet<string> includeExtensions, HashSet<string> excludedDirectories, int maxFiles)
    {
        var count = 0;
        foreach (var filePath in Directory.EnumerateFiles(rootDirectory, "*", SearchOption.AllDirectories))
        {
            var segments = filePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (segments.Any(excludedDirectories.Contains))
            {
                continue;
            }

            if (!includeExtensions.Contains(Path.GetExtension(filePath)))
            {
                continue;
            }

            count++;
            if (count >= maxFiles)
            {
                return count;
            }
        }

        return count;
    }

    /// <summary>
    /// Returns whether an option key is present and enabled.
    /// </summary>
    /// <param name="options">Command-line options.</param>
    /// <param name="key">Option key without leading dashes.</param>
    /// <returns><c>true</c> when option is present and not explicitly false.</returns>
    private static bool IsOptionEnabled(Dictionary<string, string> options, string key)
    {
        if (!options.TryGetValue(key, out var rawValue))
        {
            return false;
        }

        return !string.Equals(rawValue, "false", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Retained for compatibility; automatic rag-sources sidecar synchronization is disabled.
    /// </summary>
    /// <param name="sourcesManifestService">Service that writes rag-sources markdown from indexed metadata.</param>
    /// <param name="contextPath">Optional path context that helps resolve output location.</param>
    /// <param name="logger">Command logger for non-fatal warnings.</param>
    private static Task TrySyncRagSourcesManifestAsync(RagSourcesManifestService sourcesManifestService, string? contextPath, Microsoft.Extensions.Logging.ILogger logger)
    {
        return Task.CompletedTask;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Nebula RAG CLI - v0.2.0 (Phase 1)");
        Console.WriteLine("Usage:");
        Console.WriteLine();
        Console.WriteLine("Core Commands:");
        Console.WriteLine("  init                                  Initialize database schema");
        Console.WriteLine("  index [--source <directory>]          Index documents from directory");
        Console.WriteLine("  preload [--source <directory>] [--dry-run]  Auto-detect and preload project data");
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
    /// Candidate source for preload auto-detection.
    /// </summary>
    /// <param name="Path">Candidate directory path.</param>
    /// <param name="Score">Confidence score used for ranking.</param>
    /// <param name="EligibleFileCount">Estimated eligible file count.</param>
    /// <param name="Reason">Human-readable confidence signals.</param>
    private sealed record PreloadCandidate(string Path, int Score, int EligibleFileCount, string Reason);

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
