using System.Security.Cryptography;
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
        try
        {
            if (toolName == RagInitSchemaToolName)
            {
                await _store.InitializeSchemaAsync(_settings.Ingestion.VectorDimensions, cancellationToken);
                var manifestSyncResult = await TrySyncRagSourcesManifestAsync(null, cancellationToken);
                return BuildToolResult("Schema initialized.", new JsonObject
                {
                    ["sourcesManifestPath"] = manifestSyncResult?.ManifestPath,
                    ["sourcesManifestSourceCount"] = manifestSyncResult?.SourceCount
                });
            }

            if (toolName == QueryProjectRagToolName)
            {
                var text = arguments?["text"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(text))
                {
                    // Return guidance instead of an error so accidental empty invocations do not spam failures.
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
                var items = new JsonArray();
                foreach (var item in results)
                {
                    items.Add(new JsonObject
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
                    ["matches"] = items
                });
            }

            if (toolName == RagHealthCheckToolName)
            {
                var health = await _managementService.HealthCheckAsync(cancellationToken);
                return BuildToolResult(health.Message, new JsonObject { ["isHealthy"] = health.IsHealthy }, isError: !health.IsHealthy);
            }

            if (toolName == RagServerInfoToolName)
            {
                return BuildToolResult("Server info.", new JsonObject
                {
                    ["serverName"] = "Nebula RAG",
                    ["databaseHost"] = _settings.Database.Host,
                    ["databasePort"] = _settings.Database.Port,
                    ["databaseName"] = _settings.Database.Database
                });
            }

            if (toolName == RagIndexStatsToolName)
            {
                var stats = await _managementService.GetStatsAsync(cancellationToken: cancellationToken);
                return BuildToolResult("Index stats.", new JsonObject
                {
                    ["documentCount"] = stats.DocumentCount,
                    ["chunkCount"] = stats.ChunkCount,
                    ["totalTokens"] = stats.TotalTokens
                });
            }

            if (toolName == RagListSourcesToolName)
            {
                var sources = await _managementService.ListSourcesAsync(cancellationToken: cancellationToken);
                var sourceItems = new JsonArray();
                foreach (var source in sources)
                {
                    sourceItems.Add(new JsonObject
                    {
                        ["sourcePath"] = source.SourcePath,
                        ["chunkCount"] = source.ChunkCount,
                        ["indexedAt"] = source.IndexedAt.ToUniversalTime().ToString("O")
                    });
                }

                return BuildToolResult("Indexed sources.", new JsonObject { ["items"] = sourceItems });
            }

            if (toolName == RagIndexPathToolName)
            {
                var sourcePath = arguments?["sourcePath"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(sourcePath))
                {
                    return BuildToolResult("Missing required argument: sourcePath", isError: true);
                }

                var summary = await _indexer.IndexDirectoryAsync(sourcePath, cancellationToken);
                var manifestSyncResult = await TrySyncRagSourcesManifestAsync(sourcePath, cancellationToken);
                return BuildToolResult("Index complete.", new JsonObject
                {
                    ["documentsIndexed"] = summary.DocumentsIndexed,
                    ["documentsSkipped"] = summary.DocumentsSkipped,
                    ["chunksIndexed"] = summary.ChunksIndexed,
                    ["sourcesManifestPath"] = manifestSyncResult?.ManifestPath,
                    ["sourcesManifestSourceCount"] = manifestSyncResult?.SourceCount
                });
            }

            if (toolName == RagIndexTextToolName)
            {
                var sourcePath = arguments?["sourcePath"]?.GetValue<string>();
                var projectRootPath = Directory.GetCurrentDirectory();
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
                var normalizedSourcePath = SourcePathNormalizer.NormalizeForStorage(sourcePath, projectRootPath);
                var updated = await _store.UpsertDocumentAsync(normalizedSourcePath, contentHash, chunkEmbeddings, cancellationToken);
                var manifestSyncResult = await TrySyncRagSourcesManifestAsync(normalizedSourcePath, cancellationToken);

                return BuildToolResult(updated ? "Source text indexed." : "Source text unchanged.", new JsonObject
                {
                    ["sourcePath"] = normalizedSourcePath,
                    ["updated"] = updated,
                    ["chunkCount"] = chunkEmbeddings.Count,
                    ["contentHash"] = contentHash,
                    ["sourcesManifestPath"] = manifestSyncResult?.ManifestPath,
                    ["sourcesManifestSourceCount"] = manifestSyncResult?.SourceCount
                });
            }

            if (toolName == RagIndexUrlToolName)
            {
                var url = arguments?["url"]?.GetValue<string>();
                var sourcePath = arguments?["sourcePath"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(url))
                {
                    return BuildToolResult("url is required.", isError: true);
                }

                var fetchedContent = await _httpClient.GetStringAsync(url, cancellationToken);
                var targetSourcePath = string.IsNullOrWhiteSpace(sourcePath) ? url : sourcePath;
                var indexArgs = new JsonObject
                {
                    ["sourcePath"] = targetSourcePath,
                    ["content"] = fetchedContent
                };

                return await ExecuteToolAsync(RagIndexTextToolName, indexArgs, cancellationToken);
            }

            if (toolName == RagReindexSourceToolName)
            {
                var sourcePath = arguments?["sourcePath"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(sourcePath))
                {
                    return BuildToolResult("sourcePath is required.", isError: true);
                }

                if (!File.Exists(sourcePath))
                {
                    return BuildToolResult("sourcePath must point to a readable file for reindex.", isError: true);
                }

                var content = await File.ReadAllTextAsync(sourcePath, cancellationToken);
                var reindexArgs = new JsonObject { ["sourcePath"] = sourcePath, ["content"] = content };
                return await ExecuteToolAsync(RagIndexTextToolName, reindexArgs, cancellationToken);
            }

            if (toolName == RagGetChunkToolName)
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

            if (toolName == RagSearchSimilarToolName)
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

            if (toolName == RagDeleteSourceToolName)
            {
                var sourcePath = arguments?["sourcePath"]?.GetValue<string>();
                var projectRootPath = Directory.GetCurrentDirectory();
                var confirm = arguments?["confirm"]?.GetValue<bool>() == true;
                if (string.IsNullOrWhiteSpace(sourcePath) || !confirm)
                {
                    return BuildToolResult("sourcePath and confirm=true are required.", isError: true);
                }

                var normalizedSourcePath = SourcePathNormalizer.NormalizeForStorage(sourcePath, projectRootPath);
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

            if (toolName == RagPurgeAllToolName)
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

            if (toolName == MemoryStoreToolName)
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

            if (toolName == MemoryRecallToolName)
            {
                var text = arguments?["text"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(text))
                {
                    return BuildToolResult("text is required.", isError: true);
                }

                var limit = Math.Clamp(arguments?["limit"]?.GetValue<int?>() ?? 10, 1, 50);
                var type = arguments?["type"]?.GetValue<string>();
                var tag = arguments?["tag"]?.GetValue<string>();
                var queryEmbedding = _embeddingGenerator.GenerateEmbedding(text, _settings.Ingestion.VectorDimensions);
                var memories = await _store.SearchMemoriesAsync(queryEmbedding, limit, type, tag, cancellationToken);
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

                return BuildToolResult($"Recalled {memories.Count} memories.", new JsonObject { ["items"] = items });
            }

            if (toolName == MemoryListToolName)
            {
                var limit = Math.Clamp(arguments?["limit"]?.GetValue<int?>() ?? 20, 1, 100);
                var type = arguments?["type"]?.GetValue<string>();
                var tag = arguments?["tag"]?.GetValue<string>();
                var memories = await _store.ListMemoriesAsync(limit, type, tag, cancellationToken);
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

                return BuildToolResult($"Listed {memories.Count} memories.", new JsonObject { ["items"] = items });
            }

            if (toolName == MemoryDeleteToolName)
            {
                var memoryId = arguments?["memoryId"]?.GetValue<long?>();
                if (memoryId is null || memoryId.Value <= 0)
                {
                    return BuildToolResult("memoryId is required and must be > 0.", isError: true);
                }

                var deleted = await _store.DeleteMemoryAsync(memoryId.Value, cancellationToken);
                return BuildToolResult(deleted ? "Memory deleted." : "Memory not found.", new JsonObject { ["deleted"] = deleted });
            }

            if (toolName == MemoryUpdateToolName)
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

            return BuildToolResult($"Unknown tool: {toolName}", isError: true);
        }
        catch (Exception ex)
        {
            return BuildToolResult($"Tool execution failed: {ex.Message}", isError: true);
        }
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
