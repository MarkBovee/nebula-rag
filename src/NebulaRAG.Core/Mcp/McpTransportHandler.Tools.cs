using System.Security.Cryptography;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using NebulaRAG.Core.Models;
using NebulaRAG.Core.Pathing;
using NebulaRAG.Core.Services;

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
                RagListSourcesToolName => await ExecuteListSourcesToolAsync(cancellationToken),
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
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tool result payload.</returns>
    private async Task<JsonObject> ExecuteListSourcesToolAsync(CancellationToken cancellationToken)
    {
        var sources = await _managementService.ListSourcesAsync(cancellationToken: cancellationToken);
        var items = new JsonArray();
        foreach (var source in sources)
        {
            items.Add(new JsonObject
            {
                ["sourcePath"] = source.SourcePath,
                ["chunkCount"] = source.ChunkCount,
                ["indexedAt"] = source.IndexedAt.ToUniversalTime().ToString("O")
            });
        }

        return BuildToolResult("Indexed sources.", new JsonObject { ["items"] = items });
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
        var projectName = arguments?["projectName"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return BuildToolResult("Missing required argument: sourcePath", isError: true);
        }

        var summary = await _indexer.IndexDirectoryAsync(sourcePath, projectName, cancellationToken);
        var manifestSyncResult = await TrySyncRagSourcesManifestAsync(sourcePath, cancellationToken);
        return BuildToolResult("Index complete.", new JsonObject
        {
            ["documentsIndexed"] = summary.DocumentsIndexed,
            ["documentsSkipped"] = summary.DocumentsSkipped,
            ["chunksIndexed"] = summary.ChunksIndexed,
            ["projectName"] = string.IsNullOrWhiteSpace(projectName) ? null : projectName,
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
        var projectName = arguments?["projectName"]?.GetValue<string>();
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
        var prefixedSourcePath = SourcePathNormalizer.ApplyExplicitProjectPrefix(normalizedSourcePath, projectName);
        var updated = await _store.UpsertDocumentAsync(prefixedSourcePath, contentHash, chunkEmbeddings, cancellationToken);
        var manifestSyncResult = await TrySyncRagSourcesManifestAsync(prefixedSourcePath, cancellationToken);

        return BuildToolResult(updated ? "Source text indexed." : "Source text unchanged.", new JsonObject
        {
            ["sourcePath"] = prefixedSourcePath,
            ["projectName"] = string.IsNullOrWhiteSpace(projectName) ? null : projectName,
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
        var projectName = arguments?["projectName"]?.GetValue<string>();
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
            ["projectName"] = projectName
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
        var projectName = arguments?["projectName"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return BuildToolResult("sourcePath is required.", isError: true);
        }

        if (!File.Exists(sourcePath))
        {
            return BuildToolResult("sourcePath must point to a readable file for reindex.", isError: true);
        }

        var content = await File.ReadAllTextAsync(sourcePath, cancellationToken);
        var reindexArgs = new JsonObject { ["sourcePath"] = sourcePath, ["content"] = content, ["projectName"] = projectName };
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
        var chunkId = arguments?["chunkId"]?.GetValue<long?>();
        if (chunkId is null || chunkId.Value <= 0)
        {
            return BuildToolResult("chunkId is required and must be > 0.", isError: true);
        }

        var chunk = await _store.GetChunkByIdAsync(chunkId.Value, cancellationToken);
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
        var type = arguments?["type"]?.GetValue<string>();
        var content = arguments?["content"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(content))
        {
            return BuildToolResult("sessionId, type and content are required.", isError: true);
        }

        var tags = ParseTags(arguments?["tags"]);
        var embedding = _embeddingGenerator.GenerateEmbedding(content, _settings.Ingestion.VectorDimensions);
        var memoryId = await _store.CreateMemoryAsync(sessionId, type, content, tags, embedding, cancellationToken);
        return BuildToolResult("Memory stored.", new JsonObject
        {
            ["memoryId"] = memoryId,
            ["sessionId"] = sessionId,
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
        var queryEmbedding = _embeddingGenerator.GenerateEmbedding(text, _settings.Ingestion.VectorDimensions);
        var memories = await _store.SearchMemoriesAsync(queryEmbedding, limit, type, tag, sessionId, cancellationToken);
        var usedFallback = false;
        if (memories.Count == 0)
        {
            // Fallback to recent-memory listing so recall remains useful when semantic ranking returns no hits.
            var listedMemories = await _store.ListMemoriesAsync(limit, type, tag, sessionId, cancellationToken);
            memories = listedMemories
                .Select(memory => new MemorySearchResult(memory.Id, memory.SessionId, memory.Type, memory.Content, memory.Tags, memory.CreatedAtUtc, 0d))
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
        var memories = await _store.ListMemoriesAsync(limit, type, tag, sessionId, cancellationToken);
        var items = new JsonArray();
        foreach (var memory in memories)
        {
            items.Add(new JsonObject
            {
                ["id"] = memory.Id,
                ["sessionId"] = memory.SessionId,
                ["type"] = memory.Type,
                ["content"] = memory.Content,
                ["tags"] = JsonSerializer.SerializeToNode(memory.Tags),
                ["createdAtUtc"] = memory.CreatedAtUtc.ToUniversalTime().ToString("O")
            });
        }

        return BuildToolResult($"Listed {memories.Count} memories.", new JsonObject
        {
            ["items"] = items,
            ["sessionId"] = sessionId
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
