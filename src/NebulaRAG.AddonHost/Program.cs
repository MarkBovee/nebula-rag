using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NebulaRAG.Core.Chunking;
using NebulaRAG.Core.Configuration;
using NebulaRAG.Core.Embeddings;
using NebulaRAG.Core.Models;
using NebulaRAG.Core.Services;
using NebulaRAG.Core.Storage;
using Serilog;
using Serilog.Formatting.Compact;

const string QueryProjectRagToolName = "query_project_rag";
const string RagInitSchemaToolName = "rag_init_schema";
const string RagHealthCheckToolName = "rag_health_check";
const string RagServerInfoToolName = "rag_server_info";
const string RagIndexStatsToolName = "rag_index_stats";
const string RagListSourcesToolName = "rag_list_sources";
const string RagIndexPathToolName = "rag_index_path";
const string RagDeleteSourceToolName = "rag_delete_source";
const string RagPurgeAllToolName = "rag_purge_all";

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console(new CompactJsonFormatter())
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();

builder.Services.Configure<JsonOptions>(options =>
{
    options.SerializerOptions.WriteIndented = true;
});

var settings = LoadSettings();
settings.Validate();

var loggerFactory = LoggerFactory.Create(loggingBuilder =>
{
    loggingBuilder.AddSerilog();
    loggingBuilder.SetMinimumLevel(LogLevel.Information);
});

var store = new PostgresRagStore(settings.Database.BuildConnectionString());
var chunker = new TextChunker();
var embeddingGenerator = new HashEmbeddingGenerator();
var queryService = new RagQueryService(store, embeddingGenerator, settings, loggerFactory.CreateLogger<RagQueryService>());
var managementService = new RagManagementService(store, loggerFactory.CreateLogger<RagManagementService>());
var indexer = new RagIndexer(store, chunker, embeddingGenerator, settings, loggerFactory.CreateLogger<RagIndexer>());

await store.InitializeSchemaAsync(settings.Ingestion.VectorDimensions);

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", async (CancellationToken cancellationToken) =>
{
    var health = await managementService.HealthCheckAsync(cancellationToken);
    return Results.Json(new { health.IsHealthy, health.Message });
});

app.MapGet("/api/stats", async (CancellationToken cancellationToken) =>
{
    var stats = await managementService.GetStatsAsync(cancellationToken);
    return Results.Json(stats);
});

app.MapGet("/api/sources", async (int? limit, CancellationToken cancellationToken) =>
{
    var sources = await managementService.ListSourcesAsync(cancellationToken);
    var selected = sources.Take(Math.Clamp(limit ?? 100, 1, 500)).ToList();
    return Results.Json(selected);
});

app.MapPost("/api/query", async (QueryRequest request, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Text))
    {
        return Results.BadRequest(new { error = "text is required" });
    }

    var topK = Math.Clamp(request.Limit ?? settings.Retrieval.DefaultTopK, 1, 20);
    var results = await queryService.QueryAsync(request.Text, topK, cancellationToken);
    return Results.Json(new QueryResponse(request.Text, topK, results));
});

app.MapPost("/api/index", async (IndexRequest request, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.SourcePath))
    {
        return Results.BadRequest(new { error = "sourcePath is required" });
    }

    var summary = await indexer.IndexDirectoryAsync(request.SourcePath, cancellationToken);
    return Results.Json(summary);
});

app.MapPost("/api/source/delete", async (DeleteRequest request, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.SourcePath))
    {
        return Results.BadRequest(new { error = "sourcePath is required" });
    }

    var deleted = await managementService.DeleteSourceAsync(request.SourcePath, cancellationToken);
    return Results.Json(new { deleted });
});

app.MapPost("/api/purge", async (PurgeRequest request, CancellationToken cancellationToken) =>
{
    if (!string.Equals(request.ConfirmPhrase, "PURGE ALL", StringComparison.Ordinal))
    {
        return Results.BadRequest(new { error = "confirmPhrase must be PURGE ALL" });
    }

    await managementService.PurgeAllAsync(cancellationToken);
    return Results.Json(new { purged = true });
});

// Handle MCP JSON-RPC transport for initialize, ping, tool listing, and tool execution.
app.MapPost("/mcp", async (JsonObject request, CancellationToken cancellationToken) =>
{
    var method = request["method"]?.GetValue<string>();
    var id = request["id"]?.DeepClone();
    var parameters = request["params"]?.AsObject();

    if (string.IsNullOrWhiteSpace(method))
    {
        return Results.Json(BuildError(id, -32600, "Invalid request"));
    }

    if (method == "initialize")
    {
        return Results.Json(BuildResult(id, new JsonObject
        {
            ["protocolVersion"] = "2025-11-25",
            ["serverInfo"] = new JsonObject { ["name"] = "Nebula RAG Add-on MCP", ["version"] = "0.2.0" },
            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } }
        }));
    }

    if (method == "ping")
    {
        return Results.Json(BuildResult(id, new JsonObject()));
    }

    if (method == "tools/list")
    {
        return Results.Json(BuildResult(id, BuildToolsList()));
    }

    if (method != "tools/call")
    {
        return Results.Json(BuildError(id, -32601, $"Method not found: {method}"));
    }

    var toolName = parameters?["name"]?.GetValue<string>();
    var arguments = parameters?["arguments"]?.AsObject();
    if (string.IsNullOrWhiteSpace(toolName))
    {
        return Results.Json(BuildError(id, -32602, "Missing tool name"));
    }

    var result = await ExecuteToolAsync(toolName, arguments, queryService, managementService, store, indexer, settings, cancellationToken);
    return Results.Json(BuildResult(id, result));
});

app.Run();

/// <summary>
/// Loads runtime settings from /app and environment variables.
/// </summary>
static RagSettings LoadSettings()
{
    var configBuilder = new ConfigurationBuilder();
    configBuilder.AddJsonFile("/app/ragsettings.json", optional: true, reloadOnChange: false);
    configBuilder.AddEnvironmentVariables(prefix: "NEBULARAG_");
    return configBuilder.Build().Get<RagSettings>() ?? new RagSettings();
}

/// <summary>
/// Builds MCP tool catalog response.
/// </summary>
static JsonObject BuildToolsList()
{
    return new JsonObject
    {
        ["tools"] = new JsonArray
        {
            BuildToolDefinition(RagInitSchemaToolName, "Initialize Nebula RAG schema."),
            BuildToolDefinition(QueryProjectRagToolName, "Query Nebula RAG indexed context."),
            BuildToolDefinition(RagHealthCheckToolName, "Run health checks."),
            BuildToolDefinition(RagServerInfoToolName, "Get runtime server details."),
            BuildToolDefinition(RagIndexStatsToolName, "Get index statistics."),
            BuildToolDefinition(RagListSourcesToolName, "List indexed sources."),
            BuildToolDefinition(RagIndexPathToolName, "Index a source directory path."),
            BuildToolDefinition(RagDeleteSourceToolName, "Delete one indexed source path."),
            BuildToolDefinition(RagPurgeAllToolName, "Purge all indexed content.")
        }
    };
}

/// <summary>
/// Builds a minimal MCP tool definition.
/// </summary>
static JsonObject BuildToolDefinition(string name, string description)
{
    return new JsonObject
    {
        ["name"] = name,
        ["description"] = description,
        ["inputSchema"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject()
        }
    };
}

/// <summary>
/// Executes a supported MCP tool call.
/// </summary>
static async Task<JsonObject> ExecuteToolAsync(
    string toolName,
    JsonObject? arguments,
    RagQueryService queryService,
    RagManagementService managementService,
    PostgresRagStore store,
    RagIndexer indexer,
    RagSettings settings,
    CancellationToken cancellationToken)
{
    try
    {
        if (toolName == RagInitSchemaToolName)
        {
            await store.InitializeSchemaAsync(settings.Ingestion.VectorDimensions, cancellationToken);
            return BuildToolResult("Schema initialized.");
        }

        if (toolName == QueryProjectRagToolName)
        {
            var text = arguments?["text"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(text))
            {
                return BuildToolResult("Missing required argument: text", isError: true);
            }

            var limit = Math.Clamp(arguments?["limit"]?.GetValue<int?>() ?? settings.Retrieval.DefaultTopK, 1, 20);
            var results = await queryService.QueryAsync(text, limit, cancellationToken);
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
            var health = await managementService.HealthCheckAsync(cancellationToken);
            return BuildToolResult(health.Message, new JsonObject { ["isHealthy"] = health.IsHealthy }, isError: !health.IsHealthy);
        }

        if (toolName == RagServerInfoToolName)
        {
            return BuildToolResult("Server info.", new JsonObject
            {
                ["serverName"] = "Nebula RAG Add-on MCP",
                ["databaseHost"] = settings.Database.Host,
                ["databasePort"] = settings.Database.Port,
                ["databaseName"] = settings.Database.Database
            });
        }

        if (toolName == RagIndexStatsToolName)
        {
            var stats = await managementService.GetStatsAsync(cancellationToken);
            return BuildToolResult("Index stats.", new JsonObject
            {
                ["documentCount"] = stats.DocumentCount,
                ["chunkCount"] = stats.ChunkCount,
                ["totalTokens"] = stats.TotalTokens
            });
        }

        if (toolName == RagListSourcesToolName)
        {
            var sources = await managementService.ListSourcesAsync(cancellationToken);
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

            var summary = await indexer.IndexDirectoryAsync(sourcePath, cancellationToken);
            return BuildToolResult("Index complete.", new JsonObject
            {
                ["documentsIndexed"] = summary.DocumentsIndexed,
                ["documentsSkipped"] = summary.DocumentsSkipped,
                ["chunksIndexed"] = summary.ChunksIndexed
            });
        }

        if (toolName == RagDeleteSourceToolName)
        {
            var sourcePath = arguments?["sourcePath"]?.GetValue<string>();
            var confirm = arguments?["confirm"]?.GetValue<bool>() == true;
            if (string.IsNullOrWhiteSpace(sourcePath) || !confirm)
            {
                return BuildToolResult("sourcePath and confirm=true are required.", isError: true);
            }

            var deleted = await managementService.DeleteSourceAsync(sourcePath, cancellationToken);
            return BuildToolResult($"Deleted {deleted} items.", new JsonObject { ["deleted"] = deleted });
        }

        if (toolName == RagPurgeAllToolName)
        {
            var phrase = arguments?["confirmPhrase"]?.GetValue<string>();
            if (!string.Equals(phrase, "PURGE ALL", StringComparison.Ordinal))
            {
                return BuildToolResult("confirmPhrase must be PURGE ALL", isError: true);
            }

            await managementService.PurgeAllAsync(cancellationToken);
            return BuildToolResult("Purge complete.");
        }

        return BuildToolResult($"Unknown tool: {toolName}", isError: true);
    }
    catch (Exception ex)
    {
        return BuildToolResult($"Tool execution failed: {ex.Message}", isError: true);
    }
}

/// <summary>
/// Returns a JSON-RPC success envelope.
/// </summary>
static JsonObject BuildResult(JsonNode? id, JsonObject result)
{
    return new JsonObject
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["result"] = result
    };
}

/// <summary>
/// Returns a JSON-RPC error envelope.
/// </summary>
static JsonObject BuildError(JsonNode? id, int code, string message)
{
    return new JsonObject
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["error"] = new JsonObject
        {
            ["code"] = code,
            ["message"] = message
        }
    };
}

/// <summary>
/// Creates an MCP tool result payload.
/// </summary>
static JsonObject BuildToolResult(string text, JsonObject? structuredContent = null, bool isError = false)
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
/// Trims chunk text for compact tool output.
/// </summary>
static string TrimSnippet(string source)
{
    var flattened = source.Replace('\r', ' ').Replace('\n', ' ').Trim();
    return flattened.Length <= 280 ? flattened : $"{flattened[..280]}...";
}

/// <summary>
/// Query API request.
/// </summary>
internal sealed record QueryRequest(string Text, int? Limit);

/// <summary>
/// Query API response.
/// </summary>
internal sealed record QueryResponse(string Query, int Limit, IReadOnlyList<RagSearchResult> Matches);

/// <summary>
/// Index API request.
/// </summary>
internal sealed record IndexRequest(string SourcePath);

/// <summary>
/// Delete source API request.
/// </summary>
internal sealed record DeleteRequest(string SourcePath);

/// <summary>
/// Purge request.
/// </summary>
internal sealed record PurgeRequest(string ConfirmPhrase);
