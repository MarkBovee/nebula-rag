using System.Security.Cryptography;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using NebulaRAG.Core.Models;
using NebulaRAG.Core.Pathing;
using NebulaRAG.Core.Services;
using NebulaRAG.Core.Storage;

namespace NebulaRAG.Core.Mcp;

public sealed partial class McpTransportHandler
{
    /// <summary>
    /// Executes a requested MCP tool.
    /// </summary>
    /// <param name="toolName">Tool name.</param>
    /// <param name="arguments">Tool arguments.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>MCP tool result payload.</returns>
    private async Task<JsonObject> ExecuteToolAsync(string toolName, JsonObject? arguments, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        JsonObject? result = null;
        try
        {
            result = toolName switch
            {
                RagInitSchemaToolName => await ExecuteInitSchemaToolAsync(cancellationToken),
                QueryProjectRagToolName => await ExecuteQueryProjectRagToolAsync(arguments, cancellationToken),
                RagHealthCheckToolName => await ExecuteHealthCheckToolAsync(cancellationToken),
                RagServerInfoToolName => ExecuteServerInfoTool(),
                RagIndexStatsToolName => await ExecuteIndexStatsToolAsync(cancellationToken),
                RagListSourcesToolName => await ExecuteListSourcesToolAsync(arguments, cancellationToken),
                RagIndexPathToolName => await ExecuteIndexPathToolAsync(arguments, cancellationToken),
                RagIndexTextToolName => await ExecuteIndexTextToolAsync(arguments, cancellationToken),
                RagIndexUrlToolName => await ExecuteIndexUrlToolAsync(arguments, cancellationToken),
                RagReindexSourceToolName => await ExecuteReindexSourceToolAsync(arguments, cancellationToken),
                RagGetChunkToolName => await ExecuteGetChunkToolAsync(arguments, cancellationToken),
                RagSearchSimilarToolName => await ExecuteSearchSimilarToolAsync(arguments, cancellationToken),
                RagNormalizeSourcePathsToolName => await ExecuteNormalizeSourcePathsToolAsync(arguments, cancellationToken),
                RagDeleteSourceToolName => await ExecuteDeleteSourceToolAsync(arguments, cancellationToken),
                RagPurgeAllToolName => await ExecutePurgeAllToolAsync(arguments, cancellationToken),
                MemoryStoreToolName => await ExecuteMemoryStoreToolAsync(arguments, cancellationToken),
                MemoryRecallToolName => await ExecuteMemoryRecallToolAsync(arguments, cancellationToken),
                MemoryListToolName => await ExecuteMemoryListToolAsync(arguments, cancellationToken),
                MemoryDeleteToolName => await ExecuteMemoryDeleteToolAsync(arguments, cancellationToken),
                MemoryUpdateToolName => await ExecuteMemoryUpdateToolAsync(arguments, cancellationToken),
                CreatePlanToolName => await ExecuteCreatePlanToolAsync(arguments, cancellationToken),
                GetPlanToolName => await ExecuteGetPlanToolAsync(arguments, cancellationToken),
                ListPlansToolName => await ExecuteListPlansToolAsync(arguments, cancellationToken),
                UpdatePlanToolName => await ExecuteUpdatePlanToolAsync(arguments, cancellationToken),
                CompleteTaskToolName => await ExecuteCompleteTaskToolAsync(arguments, cancellationToken),
                UpdateTaskToolName => await ExecuteUpdateTaskToolAsync(arguments, cancellationToken),
                ArchivePlanToolName => await ExecuteArchivePlanToolAsync(arguments, cancellationToken),
                _ => BuildToolResult($"Unknown tool: {toolName}", isError: true)
            };

            return result;
        }
        catch (Exception ex)
        {
            result = BuildToolResult($"Tool execution failed: {ex.Message}", isError: true);
            return result;
        }
        finally
        {
            stopwatch.Stop();
            RecordToolTelemetry(toolName, result, stopwatch.Elapsed.TotalMilliseconds);
        }
    }

    /// <summary>
    /// Records activity and performance telemetry for one executed MCP tool call.
    /// </summary>
    /// <param name="toolName">Executed tool name.</param>
    /// <param name="result">Tool result payload.</param>
    /// <param name="elapsedMilliseconds">Execution duration in milliseconds.</param>
    private void RecordToolTelemetry(string toolName, JsonObject? result, double elapsedMilliseconds)
    {
        var isError = result?["isError"]?.GetValue<bool?>() == true;
        var status = isError ? "failed" : "ok";

        _telemetrySink.RecordActivity("mcp", $"MCP {toolName} ({status})", new Dictionary<string, string?>
        {
            ["durationMs"] = elapsedMilliseconds.ToString("F1")
        });

        if (!isError && (toolName == QueryProjectRagToolName || toolName == RagSearchSimilarToolName))
        {
            _telemetrySink.RecordQueryLatency(elapsedMilliseconds);
        }

        if (!isError)
        {
            var docsPerSecond = TryReadIndexedDocsPerSecond(toolName, result, elapsedMilliseconds);
            if (docsPerSecond > 0)
            {
                _telemetrySink.RecordIndexingRate(docsPerSecond);
            }
        }
    }

    /// <summary>
    /// Extracts indexed-doc throughput from a successful MCP result payload.
    /// </summary>
    /// <param name="toolName">Executed tool name.</param>
    /// <param name="result">Tool result payload.</param>
    /// <param name="elapsedMilliseconds">Execution duration in milliseconds.</param>
    /// <returns>Calculated documents-per-second throughput or zero when unavailable.</returns>
    private static double TryReadIndexedDocsPerSecond(string toolName, JsonObject? result, double elapsedMilliseconds)
    {
        if (elapsedMilliseconds <= 0)
        {
            return 0;
        }

        var structuredContent = result?["structuredContent"] as JsonObject;
        if (structuredContent is null)
        {
            return 0;
        }

        double indexedDocuments = 0;
        if (toolName == RagIndexPathToolName)
        {
            indexedDocuments = structuredContent["documentsIndexed"]?.GetValue<int?>() ?? 0;
        }
        else if (toolName is RagIndexTextToolName or RagIndexUrlToolName or RagReindexSourceToolName)
        {
            var updated = structuredContent["updated"]?.GetValue<bool?>() == true;
            indexedDocuments = updated ? 1 : 0;
        }

        return indexedDocuments <= 0 ? 0 : indexedDocuments / (elapsedMilliseconds / 1000d);
    }

    /// <summary>
    /// Executes schema initialization and manifest synchronization.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tool result payload.</returns>
    private async Task<JsonObject> ExecuteInitSchemaToolAsync(CancellationToken cancellationToken)
    {
        await _store.InitializeSchemaAsync(_settings.Ingestion.VectorDimensions, cancellationToken);
        var manifestSyncResult = await TrySyncRagSourcesManifestAsync(null, cancellationToken);
        return BuildToolResult("Schema initialized.", new JsonObject
        {
            ["sourcesManifestPath"] = manifestSyncResult?.ManifestPath,
            ["sourcesManifestSourceCount"] = manifestSyncResult?.SourceCount
        });
    }

    /// <summary>
    /// Executes project RAG query lookup.
    /// </summary>
    /// <param name="arguments">Tool arguments.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tool result payload.</returns>
    private async Task<JsonObject> ExecuteQueryProjectRagToolAsync(JsonObject? arguments, CancellationToken cancellationToken)
    {
        var text = arguments?["text"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return BuildToolResult("Provide a query in arguments.text. Example: { \"text\": \"where is mcp transport handled\", \"limit\": 5 }", new JsonObject
            {
                ["usageExample"] = new JsonObject
                {
                    ["text"] = "where is mcp transport handled",
                    ["limit"] = 5
                }
            });
        }

        var limit = Math.Clamp(arguments?["limit"]?.GetValue<int?>() ?? _settings.Retrieval.DefaultTopK, 1, 20);
        var results = await _queryService.QueryAsync(text, limit, cancellationToken);
        var matches = new JsonArray();
        foreach (var item in results)
        {
            matches.Add(new JsonObject
            {
                ["sourcePath"] = item.SourcePath,
                ["chunkIndex"] = item.ChunkIndex,
                ["score"] = item.Score,
                ["snippet"] = TrimSnippet(item.ChunkText)
            });
        }

        return BuildToolResult($"Found {results.Count} matches.", new JsonObject
        {
            ["query"] = text,
            ["limit"] = limit,
            ["matches"] = matches
        });
    }

    /// <summary>
    /// Executes health-check tool logic.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tool result payload.</returns>
    private async Task<JsonObject> ExecuteHealthCheckToolAsync(CancellationToken cancellationToken)
    {
        var health = await _managementService.HealthCheckAsync(cancellationToken);
        return BuildToolResult(health.Message, new JsonObject { ["isHealthy"] = health.IsHealthy }, isError: !health.IsHealthy);
    }

    /// <summary>
    /// Builds static runtime server info response.
    /// </summary>
    /// <returns>Tool result payload.</returns>
    private JsonObject ExecuteServerInfoTool()
    {
        return BuildToolResult("Server info.", new JsonObject
        {
            ["serverName"] = "Nebula RAG",
            ["databaseHost"] = _settings.Database.Host,
            ["databasePort"] = _settings.Database.Port,
            ["databaseName"] = _settings.Database.Database
        });
    }

    /// <summary>
    /// Executes index statistics lookup.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tool result payload.</returns>
    private async Task<JsonObject> ExecuteIndexStatsToolAsync(CancellationToken cancellationToken)
    {
        var stats = await _managementService.GetStatsAsync(cancellationToken: cancellationToken);
        return BuildToolResult("Index stats.", new JsonObject
        {
            ["documentCount"] = stats.DocumentCount,
            ["chunkCount"] = stats.ChunkCount,
            ["totalTokens"] = stats.TotalTokens
        });
    }

    /// <summary>
    /// Executes indexed source listing.
    /// </summary>
    /// <param name="arguments">Tool arguments.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tool result payload.</returns>
    private async Task<JsonObject> ExecuteListSourcesToolAsync(JsonObject? arguments, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(arguments?["limit"]?.GetValue<int?>() ?? 200, 1, 1000);
        var sources = await _managementService.ListSourcesAsync(cancellationToken: cancellationToken);
        var limitedSources = sources.Take(limit).ToList();
        var items = new JsonArray();
        foreach (var source in limitedSources)
        {
            items.Add(new JsonObject
            {
                ["sourcePath"] = source.SourcePath,
                ["chunkCount"] = source.ChunkCount,
                ["indexedAt"] = source.IndexedAt.ToUniversalTime().ToString("O")
            });
        }

        return BuildToolResult("Indexed sources.", new JsonObject
        {
            ["items"] = items,
            ["limit"] = limit,
            ["returnedCount"] = limitedSources.Count,
            ["totalCount"] = sources.Count
        });
    }

    /// <summary>
    /// Executes directory-path indexing.
    /// </summary>
    /// <param name="arguments">Tool arguments.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tool result payload.</returns>
    private async Task<JsonObject> ExecuteIndexPathToolAsync(JsonObject? arguments, CancellationToken cancellationToken)
    {
        var sourcePath = arguments?["sourcePath"]?.GetValue<string>();
        var projectId = arguments?["projectId"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return BuildToolResult("Missing required argument: sourcePath", isError: true);
        }

        var summary = await _indexer.IndexDirectoryAsync(sourcePath, projectId, cancellationToken);
        var manifestSyncResult = await TrySyncRagSourcesManifestAsync(sourcePath, cancellationToken);
        return BuildToolResult("Index complete.", new JsonObject
        {
            ["documentsIndexed"] = summary.DocumentsIndexed,
            ["documentsSkipped"] = summary.DocumentsSkipped,
            ["chunksIndexed"] = summary.ChunksIndexed,
            ["projectId"] = string.IsNullOrWhiteSpace(projectId) ? null : projectId,
            ["sourcesManifestPath"] = manifestSyncResult?.ManifestPath,
            ["sourcesManifestSourceCount"] = manifestSyncResult?.SourceCount
        });
    }

    /// <summary>
    /// Executes direct text indexing.
    /// </summary>
    /// <param name="arguments">Tool arguments.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tool result payload.</returns>
    private async Task<JsonObject> ExecuteIndexTextToolAsync(JsonObject? arguments, CancellationToken cancellationToken)
    {
        var sourcePath = arguments?["sourcePath"]?.GetValue<string>();
        var projectId = arguments?["projectId"]?.GetValue<string>();
        var content = arguments?["content"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(content))
        {
            return BuildToolResult("sourcePath and content are required.", isError: true);
        }

        var chunks = _chunker.Chunk(content, _settings.Ingestion.ChunkSize, _settings.Ingestion.ChunkOverlap);
        if (chunks.Count == 0)
        {
            return BuildToolResult("No indexable chunks produced.", isError: true);
        }

        var chunkEmbeddings = chunks.Select(chunk => new ChunkEmbedding(chunk.Index, chunk.Text, chunk.TokenCount, _embeddingGenerator.GenerateEmbedding(chunk.Text, _settings.Ingestion.VectorDimensions))).ToList();
        var contentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
        var normalizedSourcePath = SourcePathNormalizer.NormalizeForStorage(sourcePath, Directory.GetCurrentDirectory());
        var prefixedSourcePath = SourcePathNormalizer.ApplyExplicitProjectPrefix(normalizedSourcePath, projectId);
        var updated = await _store.UpsertDocumentAsync(prefixedSourcePath, contentHash, chunkEmbeddings, cancellationToken);
        var manifestSyncResult = await TrySyncRagSourcesManifestAsync(prefixedSourcePath, cancellationToken);

        return BuildToolResult(updated ? "Source text indexed." : "Source text unchanged.", new JsonObject
        {
            ["sourcePath"] = prefixedSourcePath,
            ["projectId"] = string.IsNullOrWhiteSpace(projectId) ? null : projectId,
            ["updated"] = updated,
            ["chunkCount"] = chunkEmbeddings.Count,
            ["contentHash"] = contentHash,
            ["sourcesManifestPath"] = manifestSyncResult?.ManifestPath,
            ["sourcesManifestSourceCount"] = manifestSyncResult?.SourceCount
        });
    }

    /// <summary>
    /// Executes URL fetch and indexing.
    /// </summary>
    /// <param name="arguments">Tool arguments.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tool result payload.</returns>
    private async Task<JsonObject> ExecuteIndexUrlToolAsync(JsonObject? arguments, CancellationToken cancellationToken)
    {
        var url = arguments?["url"]?.GetValue<string>();
        var sourcePath = arguments?["sourcePath"]?.GetValue<string>();
        var projectId = arguments?["projectId"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(url))
        {
            return BuildToolResult("url is required.", isError: true);
        }

        var fetchedContent = await _httpClient.GetStringAsync(url, cancellationToken);
        var targetSourcePath = string.IsNullOrWhiteSpace(sourcePath) ? url : sourcePath;
        var indexArgs = new JsonObject
        {
            ["sourcePath"] = targetSourcePath,
            ["content"] = fetchedContent,
            ["projectId"] = projectId
        };

        return await ExecuteIndexTextToolAsync(indexArgs, cancellationToken);
    }

    /// <summary>
    /// Executes reindex of an existing source file.
    /// </summary>
    /// <param name="arguments">Tool arguments.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tool result payload.</returns>
    private async Task<JsonObject> ExecuteReindexSourceToolAsync(JsonObject? arguments, CancellationToken cancellationToken)
    {
        var sourcePath = arguments?["sourcePath"]?.GetValue<string>();
        var projectId = arguments?["projectId"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return BuildToolResult("sourcePath is required.", isError: true);
        }

        if (!File.Exists(sourcePath))
        {
            return BuildToolResult("sourcePath must point to a readable file for reindex.", isError: true);
        }

        var content = await File.ReadAllTextAsync(sourcePath, cancellationToken);
        var reindexArgs = new JsonObject { ["sourcePath"] = sourcePath, ["content"] = content, ["projectId"] = projectId };
        return await ExecuteIndexTextToolAsync(reindexArgs, cancellationToken);
    }

    /// <summary>
    /// Executes single chunk retrieval by id.
    /// </summary>
    /// <param name="arguments">Tool arguments.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tool result payload.</returns>
    private async Task<JsonObject> ExecuteGetChunkToolAsync(JsonObject? arguments, CancellationToken cancellationToken)
    {
        if (!TryGetLongArgument(arguments, "chunkId", out var chunkId) || chunkId <= 0)
        {
            return BuildToolResult("chunkId is required and must be a positive integer. Example: { \"chunkId\": 123 }", isError: true);
        }

        var chunk = await _store.GetChunkByIdAsync(chunkId, cancellationToken);
        if (chunk is null)
        {
            return BuildToolResult("Chunk not found.", isError: true);
        }

        return BuildToolResult("Chunk retrieved.", new JsonObject
        {
            ["chunkId"] = chunk.ChunkId,
            ["sourcePath"] = chunk.SourcePath,
            ["chunkIndex"] = chunk.ChunkIndex,
            ["tokenCount"] = chunk.TokenCount,
            ["indexedAtUtc"] = chunk.IndexedAtUtc.ToUniversalTime().ToString("O"),
            ["chunkText"] = chunk.ChunkText
        });
    }

    /// <summary>
    /// Tries to read a long argument from tool arguments, accepting numeric and numeric-string JSON values.
    /// </summary>
    /// <param name="arguments">Tool arguments object.</param>
    /// <param name="argumentName">Argument name to read.</param>
    /// <param name="value">Parsed long value when successful.</param>
    /// <returns><c>true</c> when parsing succeeds; otherwise <c>false</c>.</returns>
    private static bool TryGetLongArgument(JsonObject? arguments, string argumentName, out long value)
    {
        value = 0;

        var argumentNode = arguments?[argumentName];
        if (argumentNode is null)
        {
            return false;
        }

        if (argumentNode is not JsonValue jsonValue)
        {
            return false;
        }

        if (jsonValue.TryGetValue<long>(out var longValue))
        {
            value = longValue;
            return true;
        }

        if (jsonValue.TryGetValue<int>(out var intValue))
        {
            value = intValue;
            return true;
        }

        if (!jsonValue.TryGetValue<string>(out var stringValue) || string.IsNullOrWhiteSpace(stringValue))
        {
            return false;
        }

        return long.TryParse(stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>
    /// Executes similarity search across indexed content.
    /// </summary>
    /// <param name="arguments">Tool arguments.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tool result payload.</returns>
    private async Task<JsonObject> ExecuteSearchSimilarToolAsync(JsonObject? arguments, CancellationToken cancellationToken)
    {
        var text = arguments?["text"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return BuildToolResult("text is required.", isError: true);
        }

        var limit = Math.Clamp(arguments?["limit"]?.GetValue<int?>() ?? _settings.Retrieval.DefaultTopK, 1, 20);
        var results = await _queryService.QueryAsync(text, limit, cancellationToken);
        var matches = new JsonArray();
        foreach (var item in results)
        {
            matches.Add(new JsonObject
            {
                ["sourcePath"] = item.SourcePath,
                ["chunkIndex"] = item.ChunkIndex,
                ["score"] = item.Score,
                ["snippet"] = TrimSnippet(item.ChunkText)
            });
        }

        return BuildToolResult($"Found {results.Count} similar chunks.", new JsonObject
        {
            ["query"] = text,
            ["limit"] = limit,
            ["matches"] = matches
        });
    }

    /// <summary>
    /// Executes source-path normalization migration.
    /// </summary>
    /// <param name="arguments">Tool arguments.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tool result payload.</returns>
    private async Task<JsonObject> ExecuteNormalizeSourcePathsToolAsync(JsonObject? arguments, CancellationToken cancellationToken)
    {
        var providedProjectRoot = arguments?["projectRootPath"]?.GetValue<string>();
        var projectRootPath = string.IsNullOrWhiteSpace(providedProjectRoot) ? Directory.GetCurrentDirectory() : providedProjectRoot;
        var normalizationResult = await _store.NormalizeSourcePathsAsync(projectRootPath, cancellationToken);
        var manifestSyncResult = await TrySyncRagSourcesManifestAsync(null, cancellationToken);
        return BuildToolResult("Source paths normalized.", new JsonObject
        {
            ["projectRootPath"] = projectRootPath,
            ["updatedCount"] = normalizationResult.UpdatedCount,
            ["duplicatesRemoved"] = normalizationResult.DuplicatesRemoved,
            ["sourcesManifestPath"] = manifestSyncResult?.ManifestPath,
            ["sourcesManifestSourceCount"] = manifestSyncResult?.SourceCount
        });
    }

    /// <summary>
    /// Executes source deletion.
    /// </summary>
    /// <param name="arguments">Tool arguments.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tool result payload.</returns>
    private async Task<JsonObject> ExecuteDeleteSourceToolAsync(JsonObject? arguments, CancellationToken cancellationToken)
    {
        var sourcePath = arguments?["sourcePath"]?.GetValue<string>();
        var confirm = arguments?["confirm"]?.GetValue<bool>() == true;
        if (string.IsNullOrWhiteSpace(sourcePath) || !confirm)
        {
            return BuildToolResult("sourcePath and confirm=true are required.", isError: true);
        }

        var normalizedSourcePath = SourcePathNormalizer.NormalizeForStorage(sourcePath, Directory.GetCurrentDirectory());
        var deleted = await _managementService.DeleteSourceAsync(normalizedSourcePath, cancellationToken);
        var manifestSyncResult = await TrySyncRagSourcesManifestAsync(normalizedSourcePath, cancellationToken);
        return BuildToolResult($"Deleted {deleted} items.", new JsonObject
        {
            ["sourcePath"] = normalizedSourcePath,
            ["deleted"] = deleted,
            ["sourcesManifestPath"] = manifestSyncResult?.ManifestPath,
            ["sourcesManifestSourceCount"] = manifestSyncResult?.SourceCount
        });
    }

    /// <summary>
    /// Executes purge-all operation.
    /// </summary>
    /// <param name="arguments">Tool arguments.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tool result payload.</returns>
    private async Task<JsonObject> ExecutePurgeAllToolAsync(JsonObject? arguments, CancellationToken cancellationToken)
    {
        var phrase = arguments?["confirmPhrase"]?.GetValue<string>();
        if (!string.Equals(phrase, "PURGE ALL", StringComparison.Ordinal))
        {
            return BuildToolResult("confirmPhrase must be PURGE ALL", isError: true);
        }

        await _managementService.PurgeAllAsync(cancellationToken);
        var manifestSyncResult = await TrySyncRagSourcesManifestAsync(null, cancellationToken);
        return BuildToolResult("Purge complete.", new JsonObject
        {
            ["sourcesManifestPath"] = manifestSyncResult?.ManifestPath,
            ["sourcesManifestSourceCount"] = manifestSyncResult?.SourceCount
        });
    }

    /// <summary>
    /// Executes memory store operation.
    /// </summary>
    /// <param name="arguments">Tool arguments.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tool result payload.</returns>
    private async Task<JsonObject> ExecuteMemoryStoreToolAsync(JsonObject? arguments, CancellationToken cancellationToken)
    {
        var sessionId = arguments?["sessionId"]?.GetValue<string>();
        var explicitProjectId = arguments?["projectId"]?.GetValue<string>();
        var type = arguments?["type"]?.GetValue<string>();
        var content = arguments?["content"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(content))
        {
            return BuildToolResult("type and content are required.", isError: true);
        }

        // Keep session affinity available without forcing clients to provide one.
        var resolvedSessionId = string.IsNullOrWhiteSpace(sessionId)
            ? $"session-{Guid.NewGuid():N}"
            : sessionId;
        var tags = ParseTags(arguments?["tags"]);
        var resolvedProjectId = ResolveProjectId(explicitProjectId, tags);
        var embedding = _embeddingGenerator.GenerateEmbedding(content, _settings.Ingestion.VectorDimensions);
        var memoryId = await _store.CreateMemoryAsync(resolvedSessionId, resolvedProjectId, type, content, tags, embedding, cancellationToken);
        return BuildToolResult("Memory stored.", new JsonObject
        {
            ["memoryId"] = memoryId,
            ["sessionId"] = resolvedSessionId,
            ["projectId"] = resolvedProjectId,
            ["type"] = type,
            ["tags"] = JsonSerializer.SerializeToNode(tags)
        });
    }

    /// <summary>
    /// Executes memory semantic recall.
    /// </summary>
    /// <param name="arguments">Tool arguments.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tool result payload.</returns>
    private async Task<JsonObject> ExecuteMemoryRecallToolAsync(JsonObject? arguments, CancellationToken cancellationToken)
    {
        var text = arguments?["text"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return BuildToolResult("text is required.", isError: true);
        }

        var limit = Math.Clamp(arguments?["limit"]?.GetValue<int?>() ?? 10, 1, 50);
        var type = arguments?["type"]?.GetValue<string>();
        var tag = arguments?["tag"]?.GetValue<string>();
        var sessionId = arguments?["sessionId"]?.GetValue<string>();
        var projectId = ResolveProjectId(arguments?["projectId"]?.GetValue<string>(), tags: null);
        var queryEmbedding = _embeddingGenerator.GenerateEmbedding(text, _settings.Ingestion.VectorDimensions);
        var memories = await _store.SearchMemoriesAsync(queryEmbedding, limit, type, tag, sessionId, projectId, cancellationToken);
        var usedFallback = false;
        if (memories.Count == 0)
        {
            // Fallback to recent-memory listing so recall remains useful when semantic ranking returns no hits.
            var listedMemories = await _store.ListMemoriesAsync(limit, type, tag, sessionId, projectId, cancellationToken);
            memories = listedMemories
                .Select(memory => new MemorySearchResult(memory.Id, memory.SessionId, memory.ProjectId, memory.Type, memory.Content, memory.Tags, memory.CreatedAtUtc, 0d))
                .ToList();
            usedFallback = memories.Count > 0;
        }

        var items = new JsonArray();
        foreach (var memory in memories)
        {
            items.Add(new JsonObject
            {
                ["id"] = memory.Id,
                ["sessionId"] = memory.SessionId,
                ["projectId"] = memory.ProjectId,
                ["type"] = memory.Type,
                ["content"] = memory.Content,
                ["tags"] = JsonSerializer.SerializeToNode(memory.Tags),
                ["createdAtUtc"] = memory.CreatedAtUtc.ToUniversalTime().ToString("O"),
                ["score"] = memory.Score
            });
        }

        return BuildToolResult($"Recalled {memories.Count} memories.", new JsonObject
        {
            ["items"] = items,
            ["sessionId"] = sessionId,
            ["projectId"] = projectId,
            ["fallbackUsed"] = usedFallback
        });
    }

    /// <summary>
    /// Executes memory list operation.
    /// </summary>
    /// <param name="arguments">Tool arguments.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tool result payload.</returns>
    private async Task<JsonObject> ExecuteMemoryListToolAsync(JsonObject? arguments, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(arguments?["limit"]?.GetValue<int?>() ?? 20, 1, 100);
        var type = arguments?["type"]?.GetValue<string>();
        var tag = arguments?["tag"]?.GetValue<string>();
        var sessionId = arguments?["sessionId"]?.GetValue<string>();
        var projectId = ResolveProjectId(arguments?["projectId"]?.GetValue<string>(), tags: null);
        var memories = await _store.ListMemoriesAsync(limit, type, tag, sessionId, projectId, cancellationToken);
        var items = new JsonArray();
        foreach (var memory in memories)
        {
            items.Add(new JsonObject
            {
                ["id"] = memory.Id,
                ["sessionId"] = memory.SessionId,
                ["projectId"] = memory.ProjectId,
                ["type"] = memory.Type,
                ["content"] = memory.Content,
                ["tags"] = JsonSerializer.SerializeToNode(memory.Tags),
                ["createdAtUtc"] = memory.CreatedAtUtc.ToUniversalTime().ToString("O")
            });
        }

        return BuildToolResult($"Listed {memories.Count} memories.", new JsonObject
        {
            ["items"] = items,
            ["sessionId"] = sessionId,
            ["projectId"] = projectId
        });
    }

    /// <summary>
    /// Executes memory delete operation.
    /// </summary>
    /// <param name="arguments">Tool arguments.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tool result payload.</returns>
    private async Task<JsonObject> ExecuteMemoryDeleteToolAsync(JsonObject? arguments, CancellationToken cancellationToken)
    {
        var memoryId = arguments?["memoryId"]?.GetValue<long?>();
        if (memoryId is null || memoryId.Value <= 0)
        {
            return BuildToolResult("memoryId is required and must be > 0.", isError: true);
        }

        var deleted = await _store.DeleteMemoryAsync(memoryId.Value, cancellationToken);
        return BuildToolResult(deleted ? "Memory deleted." : "Memory not found.", new JsonObject { ["deleted"] = deleted });
    }

    /// <summary>
    /// Executes memory update operation.
    /// </summary>
    /// <param name="arguments">Tool arguments.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tool result payload.</returns>
    private async Task<JsonObject> ExecuteMemoryUpdateToolAsync(JsonObject? arguments, CancellationToken cancellationToken)
    {
        var memoryId = arguments?["memoryId"]?.GetValue<long?>();
        if (memoryId is null || memoryId.Value <= 0)
        {
            return BuildToolResult("memoryId is required and must be > 0.", isError: true);
        }

        var type = arguments?["type"]?.GetValue<string>();
        var content = arguments?["content"]?.GetValue<string>();
        var tagsNode = arguments?["tags"];
        var tags = tagsNode is null ? null : ParseTags(tagsNode);
        var embedding = string.IsNullOrWhiteSpace(content) ? null : _embeddingGenerator.GenerateEmbedding(content, _settings.Ingestion.VectorDimensions);
        var updated = await _store.UpdateMemoryAsync(memoryId.Value, type, content, tags, embedding, cancellationToken);
        return BuildToolResult(updated ? "Memory updated." : "Memory not found.", new JsonObject { ["updated"] = updated });
    }

    /// <summary>
    /// Executes plan creation.
    /// </summary>
    /// <param name="arguments">Tool arguments.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tool result payload.</returns>
    private async Task<JsonObject> ExecuteCreatePlanToolAsync(JsonObject? arguments, CancellationToken cancellationToken)
    {
        var sessionId = arguments?["sessionId"]?.GetValue<string>()?.Trim();
        var planName = arguments?["planName"]?.GetValue<string>()?.Trim();
        var projectId = arguments?["projectId"]?.GetValue<string>()?.Trim();
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(planName) || string.IsNullOrWhiteSpace(projectId))
        {
            return BuildToolResult("sessionId, planName, and projectId are required.", isError: true);
        }

        await EnsurePlanSchemaInitializedAsync(cancellationToken);

        var changedBy = $"mcp:{sessionId}";
        var initialTasks = ParseInitialPlanTasks(arguments?["initialTasks"], changedBy);
        var createRequest = new CreatePlanRequest(projectId, sessionId, planName, null, initialTasks, changedBy);
        var (planId, _) = await _planService.CreatePlanAsync(createRequest, cancellationToken);
        var (plan, tasks) = await _planService.GetPlanWithTasksByIdAsync(planId, cancellationToken);

        return BuildToolResult("Plan created.", new JsonObject
        {
            ["plan"] = ToPlanJson(plan, tasks)
        });
    }

    /// <summary>
    /// Executes plan retrieval by id.
    /// </summary>
    /// <param name="arguments">Tool arguments.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tool result payload.</returns>
    private async Task<JsonObject> ExecuteGetPlanToolAsync(JsonObject? arguments, CancellationToken cancellationToken)
    {
        var sessionId = arguments?["sessionId"]?.GetValue<string>()?.Trim();
        if (string.IsNullOrWhiteSpace(sessionId) || !TryReadLongArgument(arguments, "planId", out var planId))
        {
            return BuildToolResult("sessionId and numeric planId are required.", isError: true);
        }

        await EnsurePlanSchemaInitializedAsync(cancellationToken);
        var (plan, tasks) = await _planService.GetPlanWithTasksByIdAsync(planId, cancellationToken);
        if (!string.Equals(plan.SessionId, sessionId, StringComparison.Ordinal))
        {
            return BuildToolResult("Access denied: plan does not belong to the provided sessionId.", isError: true);
        }

        return BuildToolResult("Plan retrieved.", new JsonObject
        {
            ["plan"] = ToPlanJson(plan, tasks)
        });
    }

    /// <summary>
    /// Executes session-scoped plan listing.
    /// </summary>
    /// <param name="arguments">Tool arguments.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tool result payload.</returns>
    private async Task<JsonObject> ExecuteListPlansToolAsync(JsonObject? arguments, CancellationToken cancellationToken)
    {
        var sessionId = arguments?["sessionId"]?.GetValue<string>()?.Trim();
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return BuildToolResult("sessionId is required.", isError: true);
        }

        await EnsurePlanSchemaInitializedAsync(cancellationToken);
        var plans = await _planService.ListPlansBySessionAsync(sessionId, cancellationToken);
        var items = new JsonArray();
        foreach (var plan in plans)
        {
            items.Add(ToPlanJson(plan));
        }

        return BuildToolResult($"Listed {plans.Count} plans.", new JsonObject
        {
            ["plans"] = items,
            ["sessionId"] = sessionId
        });
    }

    /// <summary>
    /// Executes plan update (name change and/or archive transition).
    /// </summary>
    /// <param name="arguments">Tool arguments.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tool result payload.</returns>
    private async Task<JsonObject> ExecuteUpdatePlanToolAsync(JsonObject? arguments, CancellationToken cancellationToken)
    {
        var sessionId = arguments?["sessionId"]?.GetValue<string>()?.Trim();
        var planName = arguments?["planName"]?.GetValue<string>()?.Trim();
        var status = arguments?["status"]?.GetValue<string>()?.Trim();
        if (string.IsNullOrWhiteSpace(sessionId) || !TryReadLongArgument(arguments, "planId", out var planId))
        {
            return BuildToolResult("sessionId and numeric planId are required.", isError: true);
        }

        if (string.IsNullOrWhiteSpace(planName) && string.IsNullOrWhiteSpace(status))
        {
            return BuildToolResult("Provide planName and/or status for update_plan.", isError: true);
        }

        await EnsurePlanSchemaInitializedAsync(cancellationToken);
        var existingPlan = await _planService.GetPlanByIdAsync(planId, cancellationToken);
        if (!string.Equals(existingPlan.SessionId, sessionId, StringComparison.Ordinal))
        {
            return BuildToolResult("Access denied: plan does not belong to the provided sessionId.", isError: true);
        }

        var changedBy = $"mcp:{sessionId}";
        if (!string.IsNullOrWhiteSpace(planName))
        {
            var request = new UpdatePlanRequest(planName, existingPlan.Description, changedBy);
            await _planService.UpdatePlanAsync(planId, request, changedBy, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!string.Equals(status, "archived", StringComparison.OrdinalIgnoreCase))
            {
                return BuildToolResult("Only status='archived' is currently supported by update_plan.", isError: true);
            }

            await _planService.ArchivePlanAsync(planId, changedBy, "Archived via MCP update_plan", cancellationToken);
        }

        var (updatedPlan, tasks) = await _planService.GetPlanWithTasksByIdAsync(planId, cancellationToken);
        return BuildToolResult("Plan updated.", new JsonObject
        {
            ["plan"] = ToPlanJson(updatedPlan, tasks)
        });
    }

    /// <summary>
    /// Executes task completion for a plan task.
    /// </summary>
    /// <param name="arguments">Tool arguments.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tool result payload.</returns>
    private async Task<JsonObject> ExecuteCompleteTaskToolAsync(JsonObject? arguments, CancellationToken cancellationToken)
    {
        var sessionId = arguments?["sessionId"]?.GetValue<string>()?.Trim();
        if (string.IsNullOrWhiteSpace(sessionId)
            || !TryReadLongArgument(arguments, "planId", out var planId)
            || !TryReadLongArgument(arguments, "taskId", out var taskId))
        {
            return BuildToolResult("sessionId and numeric planId/taskId are required.", isError: true);
        }

        await EnsurePlanSchemaInitializedAsync(cancellationToken);
        var plan = await _planService.GetPlanByIdAsync(planId, cancellationToken);
        if (!string.Equals(plan.SessionId, sessionId, StringComparison.Ordinal))
        {
            return BuildToolResult("Access denied: plan does not belong to the provided sessionId.", isError: true);
        }

        var task = await _taskService.GetTaskByIdAsync(taskId, cancellationToken);
        if (task.PlanId != planId)
        {
            return BuildToolResult("taskId does not belong to the specified planId.", isError: true);
        }

        var changedBy = $"mcp:{sessionId}";
        await _taskService.CompleteTaskAsync(taskId, changedBy, "Completed via MCP", cancellationToken);
        var updatedTask = await _taskService.GetTaskByIdAsync(taskId, cancellationToken);
        return BuildToolResult("Task completed.", new JsonObject
        {
            ["task"] = ToTaskJson(updatedTask)
        });
    }

    /// <summary>
    /// Executes task update (name update and/or completion transition).
    /// </summary>
    /// <param name="arguments">Tool arguments.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tool result payload.</returns>
    private async Task<JsonObject> ExecuteUpdateTaskToolAsync(JsonObject? arguments, CancellationToken cancellationToken)
    {
        var sessionId = arguments?["sessionId"]?.GetValue<string>()?.Trim();
        var taskName = arguments?["taskName"]?.GetValue<string>()?.Trim();
        var status = arguments?["status"]?.GetValue<string>()?.Trim();
        if (string.IsNullOrWhiteSpace(sessionId)
            || !TryReadLongArgument(arguments, "planId", out var planId)
            || !TryReadLongArgument(arguments, "taskId", out var taskId))
        {
            return BuildToolResult("sessionId and numeric planId/taskId are required.", isError: true);
        }

        if (string.IsNullOrWhiteSpace(taskName) && string.IsNullOrWhiteSpace(status))
        {
            return BuildToolResult("Provide taskName and/or status for update_task.", isError: true);
        }

        await EnsurePlanSchemaInitializedAsync(cancellationToken);
        var plan = await _planService.GetPlanByIdAsync(planId, cancellationToken);
        if (!string.Equals(plan.SessionId, sessionId, StringComparison.Ordinal))
        {
            return BuildToolResult("Access denied: plan does not belong to the provided sessionId.", isError: true);
        }

        var task = await _taskService.GetTaskByIdAsync(taskId, cancellationToken);
        if (task.PlanId != planId)
        {
            return BuildToolResult("taskId does not belong to the specified planId.", isError: true);
        }

        var changedBy = $"mcp:{sessionId}";
        if (!string.IsNullOrWhiteSpace(taskName))
        {
            var request = new UpdateTaskRequest(taskName, task.Description, task.Priority, changedBy);
            await _taskService.UpdateTaskAsync(taskId, request, changedBy, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase))
            {
                return BuildToolResult("Only status='completed' is currently supported by update_task.", isError: true);
            }

            await _taskService.CompleteTaskAsync(taskId, changedBy, "Completed via MCP update_task", cancellationToken);
        }

        var updatedTask = await _taskService.GetTaskByIdAsync(taskId, cancellationToken);
        return BuildToolResult("Task updated.", new JsonObject
        {
            ["task"] = ToTaskJson(updatedTask)
        });
    }

    /// <summary>
    /// Executes plan archiving.
    /// </summary>
    /// <param name="arguments">Tool arguments.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tool result payload.</returns>
    private async Task<JsonObject> ExecuteArchivePlanToolAsync(JsonObject? arguments, CancellationToken cancellationToken)
    {
        var sessionId = arguments?["sessionId"]?.GetValue<string>()?.Trim();
        if (string.IsNullOrWhiteSpace(sessionId) || !TryReadLongArgument(arguments, "planId", out var planId))
        {
            return BuildToolResult("sessionId and numeric planId are required.", isError: true);
        }

        await EnsurePlanSchemaInitializedAsync(cancellationToken);
        var plan = await _planService.GetPlanByIdAsync(planId, cancellationToken);
        if (!string.Equals(plan.SessionId, sessionId, StringComparison.Ordinal))
        {
            return BuildToolResult("Access denied: plan does not belong to the provided sessionId.", isError: true);
        }

        var changedBy = $"mcp:{sessionId}";
        await _planService.ArchivePlanAsync(planId, changedBy, "Archived via MCP archive_plan", cancellationToken);
        var (archivedPlan, tasks) = await _planService.GetPlanWithTasksByIdAsync(planId, cancellationToken);
        return BuildToolResult("Plan archived.", new JsonObject
        {
            ["plan"] = ToPlanJson(archivedPlan, tasks)
        });
    }

    /// <summary>
    /// Ensures plan storage schema is initialized exactly once per process.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task EnsurePlanSchemaInitializedAsync(CancellationToken cancellationToken)
    {
        if (_planSchemaInitialized)
        {
            return;
        }

        await _planSchemaLock.WaitAsync(cancellationToken);
        try
        {
            if (_planSchemaInitialized)
            {
                return;
            }

            await _planStore.InitializeSchemaAsync(cancellationToken);
            _planSchemaInitialized = true;
        }
        finally
        {
            _planSchemaLock.Release();
        }
    }

    /// <summary>
    /// Parses an integer identifier argument that may be passed as JSON number or numeric string.
    /// </summary>
    /// <param name="arguments">Tool arguments.</param>
    /// <param name="argumentName">Argument name.</param>
    /// <param name="value">Parsed positive value.</param>
    /// <returns>True when a valid positive integer was provided.</returns>
    private static bool TryReadLongArgument(JsonObject? arguments, string argumentName, out long value)
    {
        value = 0;
        var node = arguments?[argumentName];
        if (node is null)
        {
            return false;
        }

        if (node is JsonValue numeric && numeric.TryGetValue<long>(out value) && value > 0)
        {
            return true;
        }

        if (node is JsonValue textValue && textValue.TryGetValue<string>(out var text) && long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value > 0)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Parses initial task strings for plan creation.
    /// </summary>
    /// <param name="initialTasksNode">Optional initial task array node.</param>
    /// <param name="changedBy">Operator identifier for audit metadata.</param>
    /// <returns>Normalized initial task requests.</returns>
    private static IReadOnlyList<CreateTaskRequest> ParseInitialPlanTasks(JsonNode? initialTasksNode, string changedBy)
    {
        if (initialTasksNode is not JsonArray initialTasksArray)
        {
            return [];
        }

        var tasks = new List<CreateTaskRequest>();
        foreach (var item in initialTasksArray)
        {
            var title = item?.GetValue<string>()?.Trim();
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            tasks.Add(new CreateTaskRequest(title, null, "normal", changedBy));
        }

        return tasks;
    }

    /// <summary>
    /// Converts a plan record to MCP JSON representation.
    /// </summary>
    /// <param name="plan">Plan record.</param>
    /// <param name="tasks">Optional task list.</param>
    /// <returns>JSON object.</returns>
    private static JsonObject ToPlanJson(PlanRecord plan, IReadOnlyList<PlanTaskRecord>? tasks = null)
    {
        var json = new JsonObject
        {
            ["planId"] = plan.Id,
            ["projectId"] = plan.ProjectId,
            ["sessionId"] = plan.SessionId,
            ["name"] = plan.Name,
            ["description"] = plan.Description,
            ["status"] = plan.Status.ToString(),
            ["createdAtUtc"] = plan.CreatedAt.ToUniversalTime().ToString("O"),
            ["updatedAtUtc"] = plan.UpdatedAt.ToUniversalTime().ToString("O")
        };

        if (tasks is not null)
        {
            var taskItems = new JsonArray();
            foreach (var task in tasks)
            {
                taskItems.Add(ToTaskJson(task));
            }

            json["tasks"] = taskItems;
        }

        return json;
    }

    /// <summary>
    /// Converts a task record to MCP JSON representation.
    /// </summary>
    /// <param name="task">Task record.</param>
    /// <returns>JSON object.</returns>
    private static JsonObject ToTaskJson(PlanTaskRecord task)
    {
        return new JsonObject
        {
            ["taskId"] = task.Id,
            ["planId"] = task.PlanId,
            ["name"] = task.Title,
            ["description"] = task.Description,
            ["priority"] = task.Priority,
            ["status"] = task.Status.ToString(),
            ["createdAtUtc"] = task.CreatedAt.ToUniversalTime().ToString("O"),
            ["updatedAtUtc"] = task.UpdatedAt.ToUniversalTime().ToString("O")
        };
    }

    /// <summary>
    /// Builds MCP tool result payload.
    /// </summary>
    /// <param name="text">Result text.</param>
    /// <param name="structuredContent">Optional structured content.</param>
    /// <param name="isError">Whether the result is an error.</param>
    /// <returns>MCP tool result payload.</returns>
    private static JsonObject BuildToolResult(string text, JsonObject? structuredContent = null, bool isError = false)
    {
        var result = new JsonObject
        {
            ["content"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = text
                }
            }
        };

        if (structuredContent is not null)
        {
            result["structuredContent"] = structuredContent;
        }

        if (isError)
        {
            result["isError"] = true;
        }

        return result;
    }

    /// <summary>
    /// Tries to sync rag-sources manifest while keeping tool execution resilient.
    /// </summary>
    /// <param name="contextPath">Optional context path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Sync result or null on failure.</returns>
    private async Task<RagSourcesManifestSyncResult?> TrySyncRagSourcesManifestAsync(string? contextPath, CancellationToken cancellationToken)
    {
        try
        {
            return await _sourcesManifestService.SyncAsync(contextPath, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to synchronize rag-sources manifest after successful index operation.");
            return null;
        }
    }

    /// <summary>
    /// Parses string tags from JSON node.
    /// </summary>
    /// <param name="tagsNode">Tags JSON node.</param>
    /// <returns>Normalized tags list.</returns>
    private static List<string> ParseTags(JsonNode? tagsNode)
    {
        if (tagsNode is not JsonArray tagsArray)
        {
            return [];
        }

        var tags = new List<string>();
        foreach (var item in tagsArray)
        {
            var tag = item?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(tag))
            {
                continue;
            }

            tags.Add(tag.Trim());
        }

        return tags;
    }

    /// <summary>
    /// Resolves an optional project id, with tag-based fallback for project-scoped memory writes.
    /// </summary>
    /// <param name="explicitProjectId">Project id explicitly provided by caller.</param>
    /// <param name="tags">Optional memory tags to inspect for project:* conventions.</param>
    /// <returns>Resolved project id or <c>null</c> when no project scope is provided.</returns>
    private static string? ResolveProjectId(string? explicitProjectId, IReadOnlyList<string>? tags)
    {
        if (!string.IsNullOrWhiteSpace(explicitProjectId))
        {
            return explicitProjectId.Trim();
        }

        if (tags is null)
        {
            return null;
        }

        foreach (var tag in tags)
        {
            if (string.IsNullOrWhiteSpace(tag) || !tag.StartsWith("project:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var projectFromTag = tag["project:".Length..].Trim();
            if (!string.IsNullOrWhiteSpace(projectFromTag))
            {
                return projectFromTag;
            }
        }

        return null;
    }

    /// <summary>
    /// Trims chunk text for compact MCP output snippets.
    /// </summary>
    /// <param name="source">Source text.</param>
    /// <returns>Trimmed and truncated snippet.</returns>
    private static string TrimSnippet(string source)
    {
        var flattened = source.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return flattened.Length <= 280 ? flattened : $"{flattened[..280]}...";
    }
}
