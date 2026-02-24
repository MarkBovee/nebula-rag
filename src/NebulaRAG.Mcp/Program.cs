using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Formatting.Compact;
using NebulaRAG.Core.Chunking;
using NebulaRAG.Core.Configuration;
using NebulaRAG.Core.Embeddings;
using NebulaRAG.Core.Models;
using NebulaRAG.Core.Pathing;
using NebulaRAG.Core.Services;
using NebulaRAG.Core.Storage;

const string QueryProjectRagToolName = "query_project_rag";
const string RagInitSchemaToolName = "rag_init_schema";
const string RagHealthCheckToolName = "rag_health_check";
const string RagServerInfoToolName = "rag_server_info";
const string RagIndexStatsToolName = "rag_index_stats";
const string RagRecentSourcesToolName = "rag_recent_sources";
const string RagListSourcesToolName = "rag_list_sources";
const string RagIndexPathToolName = "rag_index_path";
const string RagUpsertSourceToolName = "rag_upsert_source";
const string RagDeleteSourceToolName = "rag_delete_source";
const string RagPurgeAllToolName = "rag_purge_all";
const string RagNormalizeSourcesToolName = "rag_normalize_sources";
const string PathMappingsEnvironmentVariable = "NEBULARAG_PathMappings";
const int DefaultToolTimeoutMs = 30000;
const int InitSchemaToolTimeoutMs = 30000;
const int QueryToolTimeoutMs = 30000;
const int HealthToolTimeoutMs = 10000;
const int ServerInfoToolTimeoutMs = 5000;
const int IndexStatsToolTimeoutMs = 10000;
const int RecentSourcesToolTimeoutMs = 10000;
const int ListSourcesToolTimeoutMs = 10000;
const int IndexPathToolTimeoutMs = 120000;
const int UpsertSourceToolTimeoutMs = 60000;
const int DeleteSourceToolTimeoutMs = 10000;
const int PurgeAllToolTimeoutMs = 20000;
const int NormalizeSourcesToolTimeoutMs = 60000;

var supportedProtocolVersions = new[] { "2025-11-25", "2025-03-26", "2024-11-05" };

// Setup Serilog for MCP server
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    // MCP stdio servers must avoid stdout logging because it corrupts protocol frames.
    .WriteTo.Console(new CompactJsonFormatter(), standardErrorFromLevel: Serilog.Events.LogEventLevel.Verbose)
    .CreateLogger();

var configPath = GetConfigPath(args);
var settings = LoadSettings(configPath);
var runSelfTests = !HasFlag(args, "--skip-self-test");
var selfTestOnly = HasFlag(args, "--self-test-only");
var store = new PostgresRagStore(settings.Database.BuildConnectionString());
var embeddingGenerator = new HashEmbeddingGenerator();
var chunker = new TextChunker();

var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddSerilog();
    builder.SetMinimumLevel(LogLevel.Information);
});
var queryLogger = loggerFactory.CreateLogger<RagQueryService>();
var queryService = new RagQueryService(store, embeddingGenerator, settings, queryLogger);
var managementLogger = loggerFactory.CreateLogger<RagManagementService>();
var managementService = new RagManagementService(store, managementLogger);
var sourcesManifestLogger = loggerFactory.CreateLogger<RagSourcesManifestService>();
var sourcesManifestService = new RagSourcesManifestService(store, settings, sourcesManifestLogger);
var indexerLogger = loggerFactory.CreateLogger<RagIndexer>();
var indexer = new RagIndexer(store, chunker, embeddingGenerator, settings, indexerLogger);
var sourcePathMappings = LoadSourcePathMappings();

if (runSelfTests)
{
    await RunStartupSelfTestsAsync(store, queryService, settings);
}

if (selfTestOnly)
{
    return;
}

await RunServerAsync(queryService, managementService, sourcesManifestService, store, chunker, embeddingGenerator, indexer, settings, sourcePathMappings, supportedProtocolVersions);
return;

/// <summary>
/// Runs MCP startup diagnostics before accepting JSON-RPC traffic.
/// </summary>
/// <param name="store">PostgreSQL RAG store used for schema and connectivity checks.</param>
/// <param name="queryService">RAG query service used for retrieval smoke tests.</param>
/// <param name="settings">Resolved runtime settings for vector dimensions and retrieval defaults.</param>
static async Task RunStartupSelfTestsAsync(PostgresRagStore store, RagQueryService queryService, RagSettings settings)
{
    var tests = new[]
    {
        new SelfTestCase(
            "Stargate Link",
            "Verify database connectivity and schema readiness.",
            ct => store.InitializeSchemaAsync(settings.Ingestion.VectorDimensions, ct)),
        new SelfTestCase(
            "Vector Echo",
            "Run a smoke retrieval against the embedding index.",
            async ct =>
            {
                _ = await queryService.QueryAsync("nebula rag startup self test", 1, ct);
            }),
        new SelfTestCase(
            "Constellation Map",
            "Confirm MCP tool catalog includes required Nebula RAG tools.",
            _ =>
            {
                ValidateToolCatalog();
                return Task.CompletedTask;
            })
    };

    await Console.Error.WriteLineAsync("[Nebula RAG MCP] === Nebula Startup Diagnostics ===");
    await Console.Error.WriteLineAsync("[Nebula RAG MCP] Preparing warp checks before opening the MCP channel.");

    var overall = Stopwatch.StartNew();

    for (var i = 0; i < tests.Length; i++)
    {
        var test = tests[i];
        var timer = Stopwatch.StartNew();
        await Console.Error.WriteLineAsync($"[Nebula RAG MCP] ({i + 1}/{tests.Length}) * {test.Name}: {test.Description}");

        try
        {
            await test.Run(CancellationToken.None);
            await Console.Error.WriteLineAsync($"[Nebula RAG MCP]     PASS  {test.Name} [{timer.ElapsedMilliseconds} ms]");
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"[Nebula RAG MCP]     FAIL  {test.Name} [{timer.ElapsedMilliseconds} ms]");
            await Console.Error.WriteLineAsync($"[Nebula RAG MCP]     REASON: {ex.Message}");
            throw new InvalidOperationException($"Nebula startup diagnostics failed at '{test.Name}'.", ex);
        }
    }

    await Console.Error.WriteLineAsync($"[Nebula RAG MCP] All systems luminous. {tests.Length}/{tests.Length} checks passed in {overall.ElapsedMilliseconds} ms.");
}

/// <summary>
/// Validates that the MCP tool catalog contains the expected RAG tool contract.
/// </summary>
static void ValidateToolCatalog()
{
    var tools = BuildToolsList()["tools"]?.AsArray()
                ?? throw new InvalidOperationException("Tool catalog is missing.");

    var toolNames = new HashSet<string>(StringComparer.Ordinal);
    foreach (var tool in tools)
    {
        if (tool is not JsonObject toolObject)
        {
            continue;
        }

        var toolName = toolObject["name"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(toolName))
        {
            continue;
        }

        toolNames.Add(toolName);

        if (!string.Equals(toolName, QueryProjectRagToolName, StringComparison.Ordinal))
        {
            continue;
        }

        var required = toolObject["inputSchema"]?["required"]?.AsArray();
        var hasTextRequirement = required?.Any(x => string.Equals(x?.GetValue<string>(), "text", StringComparison.Ordinal)) == true;
        if (!hasTextRequirement)
        {
            throw new InvalidOperationException("query_project_rag input schema must require 'text'.");
        }
    }

    if (!toolNames.Contains(QueryProjectRagToolName) ||
        !toolNames.Contains(RagInitSchemaToolName) ||
        !toolNames.Contains(RagHealthCheckToolName) ||
        !toolNames.Contains(RagServerInfoToolName) ||
        !toolNames.Contains(RagIndexStatsToolName) ||
        !toolNames.Contains(RagRecentSourcesToolName) ||
        !toolNames.Contains(RagListSourcesToolName) ||
        !toolNames.Contains(RagIndexPathToolName) ||
        !toolNames.Contains(RagUpsertSourceToolName) ||
        !toolNames.Contains(RagDeleteSourceToolName) ||
        !toolNames.Contains(RagPurgeAllToolName) ||
        !toolNames.Contains(RagNormalizeSourcesToolName))
    {
        throw new InvalidOperationException("Required Nebula RAG MCP tools are not fully registered.");
    }
}

static async Task RunServerAsync(
    RagQueryService queryService,
    RagManagementService managementService,
    RagSourcesManifestService sourcesManifestService,
    PostgresRagStore store,
    TextChunker chunker,
    IEmbeddingGenerator embeddingGenerator,
    RagIndexer indexer,
    RagSettings settings,
    IReadOnlyList<SourcePathMapping> sourcePathMappings,
    IReadOnlyList<string> supportedProtocolVersions)
{
    using var input = Console.OpenStandardInput();
    using var output = Console.OpenStandardOutput();
    var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    var messageFraming = McpMessageFraming.Unknown;

    while (true)
    {
        var (payload, detectedFraming) = await ReadMessageAsync(input);
        if (payload is null)
        {
            return;
        }

        messageFraming = detectedFraming;

        JsonObject request;
        try
        {
            request = JsonNode.Parse(payload)?.AsObject()
                      ?? throw new InvalidOperationException("Invalid JSON request.");
        }
        catch
        {
            await WriteErrorResponseAsync(output, null, -32700, "Parse error", messageFraming);
            continue;
        }

        var method = request["method"]?.GetValue<string>();
        var id = request["id"]?.DeepClone();
        if (string.IsNullOrWhiteSpace(method))
        {
            await WriteErrorResponseAsync(output, id, -32600, "Invalid request", messageFraming);
            continue;
        }

        if (method.StartsWith("notifications/", StringComparison.Ordinal))
        {
            continue;
        }

        switch (method)
        {
            case "initialize":
                var requestedVersion = request["params"]?["protocolVersion"]?.GetValue<string>();
                var negotiatedVersion = NegotiateProtocolVersion(requestedVersion, supportedProtocolVersions);
                if (negotiatedVersion is null)
                {
                    await WriteErrorResponseAsync(
                        output,
                        id,
                        -32602,
                        "Unsupported protocol version",
                        messageFraming,
                        new JsonObject
                        {
                            ["requested"] = requestedVersion,
                            ["supported"] = JsonSerializer.SerializeToNode(supportedProtocolVersions, jsonOptions)
                        });
                    break;
                }

                await WriteResponseAsync(output, id, new JsonObject
                {
                    ["protocolVersion"] = negotiatedVersion,
                    ["serverInfo"] = new JsonObject
                    {
                        ["name"] = "Nebula RAG MCP",
                        ["version"] = "1.0.0"
                    },
                    ["capabilities"] = new JsonObject
                    {
                        ["tools"] = new JsonObject
                        {
                            ["listChanged"] = false
                        }
                    }
                }, jsonOptions, messageFraming);
                break;

            case "ping":
                await WriteResponseAsync(output, id, new JsonObject(), jsonOptions, messageFraming);
                break;

            case "tools/list":
                await WriteResponseAsync(output, id, BuildToolsList(), jsonOptions, messageFraming);
                break;

            case "tools/call":
                await HandleToolsCallAsync(output, id, request["params"]?.AsObject(), queryService, managementService, sourcesManifestService, store, chunker, embeddingGenerator, indexer, settings, sourcePathMappings, jsonOptions, messageFraming);
                break;

            default:
                await WriteErrorResponseAsync(output, id, -32601, $"Method not found: {method}", messageFraming);
                break;
        }
    }
}

static JsonObject BuildToolsList()
{
    return new JsonObject
    {
        ["tools"] = new JsonArray
        {
            BuildRagInitSchemaToolDefinition(),
            BuildQueryProjectRagToolDefinition(),
            BuildRagHealthCheckToolDefinition(),
            BuildRagServerInfoToolDefinition(),
            BuildRagIndexStatsToolDefinition(),
            BuildRagRecentSourcesToolDefinition(),
            BuildRagListSourcesToolDefinition(),
            BuildRagIndexPathToolDefinition(),
            BuildRagUpsertSourceToolDefinition(),
            BuildRagDeleteSourceToolDefinition(),
            BuildRagPurgeAllToolDefinition(),
            BuildRagNormalizeSourcesToolDefinition()
        }
    };
}

static JsonObject BuildRagInitSchemaToolDefinition()
{
    return new JsonObject
    {
        ["name"] = RagInitSchemaToolName,
        ["title"] = "RAG Init Schema",
        ["description"] = "Initialize Nebula RAG database schema and vector extension.",
        ["inputSchema"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject()
        },
        ["outputSchema"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["vectorDimensions"] = new JsonObject { ["type"] = "integer" }
            },
            ["required"] = new JsonArray("vectorDimensions")
        }
    };
}

static JsonObject BuildQueryProjectRagToolDefinition()
{
    return new JsonObject
    {
        ["name"] = QueryProjectRagToolName,
        ["title"] = "Query Project RAG",
        ["description"] = "Query Nebula RAG indexed context for this project before coding answers.",
        ["inputSchema"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["text"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Natural language query about the project codebase."
                },
                ["limit"] = new JsonObject
                {
                    ["type"] = "integer",
                    ["minimum"] = 1,
                    ["maximum"] = 20,
                    ["description"] = "Maximum number of chunks to return."
                },
                ["sourcePathContains"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Optional case-insensitive substring filter applied to sourcePath."
                },
                ["minScore"] = new JsonObject
                {
                    ["type"] = "number",
                    ["description"] = "Optional minimum cosine similarity score threshold."
                }
            },
            ["required"] = new JsonArray("text")
        },
        ["outputSchema"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["query"] = new JsonObject { ["type"] = "string" },
                ["limit"] = new JsonObject { ["type"] = "integer" },
                ["matchCount"] = new JsonObject { ["type"] = "integer" },
                ["sourcePathContains"] = new JsonObject { ["type"] = "string" },
                ["minScore"] = new JsonObject { ["type"] = "number" },
                ["matches"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["sourcePath"] = new JsonObject { ["type"] = "string" },
                            ["chunkIndex"] = new JsonObject { ["type"] = "integer" },
                            ["score"] = new JsonObject { ["type"] = "number" },
                            ["snippet"] = new JsonObject { ["type"] = "string" }
                        },
                        ["required"] = new JsonArray("sourcePath", "chunkIndex", "score", "snippet")
                    }
                }
            },
            ["required"] = new JsonArray("query", "limit", "matchCount", "matches")
        }
    };
}

static JsonObject BuildRagHealthCheckToolDefinition()
{
    return new JsonObject
    {
        ["name"] = RagHealthCheckToolName,
        ["title"] = "RAG Health Check",
        ["description"] = "Validate Nebula RAG database connectivity and retrieval path.",
        ["inputSchema"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["smokeQuery"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Optional query used for retrieval probe.",
                    ["default"] = "nebula rag health probe"
                }
            }
        },
        ["outputSchema"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["status"] = new JsonObject { ["type"] = "string" },
                ["probeQuery"] = new JsonObject { ["type"] = "string" },
                ["vectorDimensions"] = new JsonObject { ["type"] = "integer" },
                ["defaultTopK"] = new JsonObject { ["type"] = "integer" },
                ["retrievalProbeCount"] = new JsonObject { ["type"] = "integer" }
            },
            ["required"] = new JsonArray("status", "probeQuery", "vectorDimensions", "defaultTopK", "retrievalProbeCount")
        }
    };
}

static JsonObject BuildRagServerInfoToolDefinition()
{
    return new JsonObject
    {
        ["name"] = RagServerInfoToolName,
        ["title"] = "RAG Server Info",
        ["description"] = "Return non-sensitive Nebula RAG runtime settings and capabilities.",
        ["inputSchema"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject()
        },
        ["outputSchema"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["serverName"] = new JsonObject { ["type"] = "string" },
                ["serverVersion"] = new JsonObject { ["type"] = "string" },
                ["databaseHost"] = new JsonObject { ["type"] = "string" },
                ["databasePort"] = new JsonObject { ["type"] = "integer" },
                ["databaseName"] = new JsonObject { ["type"] = "string" },
                ["vectorDimensions"] = new JsonObject { ["type"] = "integer" },
                ["defaultTopK"] = new JsonObject { ["type"] = "integer" },
                ["toolCount"] = new JsonObject { ["type"] = "integer" }
            },
            ["required"] = new JsonArray("serverName", "serverVersion", "databaseHost", "databasePort", "databaseName", "vectorDimensions", "defaultTopK", "toolCount")
        }
    };
}

static JsonObject BuildRagIndexStatsToolDefinition()
{
    return new JsonObject
    {
        ["name"] = RagIndexStatsToolName,
        ["title"] = "RAG Index Stats",
        ["description"] = "Return aggregate index statistics from Nebula RAG storage.",
        ["inputSchema"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject()
        },
        ["outputSchema"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["documentCount"] = new JsonObject { ["type"] = "integer" },
                ["chunkCount"] = new JsonObject { ["type"] = "integer" },
                ["latestIndexedAtUtc"] = new JsonObject { ["type"] = "string" }
            },
            ["required"] = new JsonArray("documentCount", "chunkCount")
        }
    };
}

static JsonObject BuildRagRecentSourcesToolDefinition()
{
    return new JsonObject
    {
        ["name"] = RagRecentSourcesToolName,
        ["title"] = "RAG Recent Sources",
        ["description"] = "List recently indexed source files with chunk counts.",
        ["inputSchema"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["limit"] = new JsonObject
                {
                    ["type"] = "integer",
                    ["minimum"] = 1,
                    ["maximum"] = 50,
                    ["description"] = "Maximum number of source files to return.",
                    ["default"] = 10
                }
            }
        },
        ["outputSchema"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["count"] = new JsonObject { ["type"] = "integer" },
                ["items"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["sourcePath"] = new JsonObject { ["type"] = "string" },
                            ["indexedAtUtc"] = new JsonObject { ["type"] = "string" },
                            ["chunkCount"] = new JsonObject { ["type"] = "integer" }
                        },
                        ["required"] = new JsonArray("sourcePath", "indexedAtUtc", "chunkCount")
                    }
                }
            },
            ["required"] = new JsonArray("count", "items")
        }
    };
}

static JsonObject BuildRagListSourcesToolDefinition()
{
    return new JsonObject
    {
        ["name"] = RagListSourcesToolName,
        ["title"] = "RAG List Sources",
        ["description"] = "List all indexed source paths and metadata for index management.",
        ["inputSchema"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["limit"] = new JsonObject
                {
                    ["type"] = "integer",
                    ["minimum"] = 1,
                    ["maximum"] = 500,
                    ["default"] = 100,
                    ["description"] = "Maximum number of sources to return."
                }
            }
        },
        ["outputSchema"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["count"] = new JsonObject { ["type"] = "integer" },
                ["items"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["sourcePath"] = new JsonObject { ["type"] = "string" },
                            ["chunkCount"] = new JsonObject { ["type"] = "integer" },
                            ["indexedAtUtc"] = new JsonObject { ["type"] = "string" }
                        },
                        ["required"] = new JsonArray("sourcePath", "chunkCount", "indexedAtUtc")
                    }
                }
            },
            ["required"] = new JsonArray("count", "items")
        }
    };
}

static JsonObject BuildRagUpsertSourceToolDefinition()
{
    return new JsonObject
    {
        ["name"] = RagUpsertSourceToolName,
        ["title"] = "RAG Upsert Source",
        ["description"] = "Index or update source content directly in Nebula RAG storage.",
        ["inputSchema"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["sourcePath"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Logical source path key for this content (for example docs/guide.md)."
                },
                ["content"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Full text content to chunk, embed, and index."
                }
            },
            ["required"] = new JsonArray("sourcePath", "content")
        },
        ["outputSchema"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["sourcePath"] = new JsonObject { ["type"] = "string" },
                ["updated"] = new JsonObject { ["type"] = "boolean" },
                ["chunkCount"] = new JsonObject { ["type"] = "integer" },
                ["contentHash"] = new JsonObject { ["type"] = "string" }
            },
            ["required"] = new JsonArray("sourcePath", "updated", "chunkCount", "contentHash")
        }
    };
}

static JsonObject BuildRagIndexPathToolDefinition()
{
    return new JsonObject
    {
        ["name"] = RagIndexPathToolName,
        ["title"] = "RAG Index Path",
        ["description"] = "Recursively index files from a caller or server directory path. Host paths can be translated with NEBULARAG_PathMappings.",
        ["inputSchema"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["sourcePath"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Absolute or relative directory path from the caller machine or MCP runtime."
                }
            },
            ["required"] = new JsonArray("sourcePath")
        },
        ["outputSchema"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["sourcePath"] = new JsonObject { ["type"] = "string" },
                ["resolvedSourcePath"] = new JsonObject { ["type"] = "string" },
                ["documentsIndexed"] = new JsonObject { ["type"] = "integer" },
                ["documentsSkipped"] = new JsonObject { ["type"] = "integer" },
                ["chunksIndexed"] = new JsonObject { ["type"] = "integer" }
            },
            ["required"] = new JsonArray("sourcePath", "resolvedSourcePath", "documentsIndexed", "documentsSkipped", "chunksIndexed")
        }
    };
}

static JsonObject BuildRagDeleteSourceToolDefinition()
{
    return new JsonObject
    {
        ["name"] = RagDeleteSourceToolName,
        ["title"] = "RAG Delete Source",
        ["description"] = "Delete a specific indexed source path and all associated chunks.",
        ["inputSchema"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["sourcePath"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Exact source path to delete from the index."
                },
                ["confirm"] = new JsonObject
                {
                    ["type"] = "boolean",
                    ["description"] = "Must be true to execute deletion.",
                    ["default"] = false
                }
            },
            ["required"] = new JsonArray("sourcePath", "confirm")
        }
    };
}

static JsonObject BuildRagPurgeAllToolDefinition()
{
    return new JsonObject
    {
        ["name"] = RagPurgeAllToolName,
        ["title"] = "RAG Purge All",
        ["description"] = "Permanently delete all indexed RAG documents and chunks. Requires explicit confirmation phrase.",
        ["inputSchema"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["confirmPhrase"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Must be exactly 'PURGE ALL'."
                }
            },
            ["required"] = new JsonArray("confirmPhrase")
        }
    };
}

static JsonObject BuildRagNormalizeSourcesToolDefinition()
{
    return new JsonObject
    {
        ["name"] = RagNormalizeSourcesToolName,
        ["title"] = "RAG Normalize Sources",
        ["description"] = "Normalize indexed source paths to project-relative keys and remove duplicates.",
        ["inputSchema"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject()
        },
        ["outputSchema"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["updatedCount"] = new JsonObject { ["type"] = "integer" },
                ["duplicatesRemoved"] = new JsonObject { ["type"] = "integer" }
            },
            ["required"] = new JsonArray("updatedCount", "duplicatesRemoved")
        }
    };
}

static async Task HandleToolsCallAsync(
    Stream output,
    JsonNode? id,
    JsonObject? parameters,
    RagQueryService queryService,
    RagManagementService managementService,
    RagSourcesManifestService sourcesManifestService,
    PostgresRagStore store,
    TextChunker chunker,
    IEmbeddingGenerator embeddingGenerator,
    RagIndexer indexer,
    RagSettings settings,
    IReadOnlyList<SourcePathMapping> sourcePathMappings,
    JsonSerializerOptions jsonOptions,
    McpMessageFraming framing)
{
    var toolName = parameters?["name"]?.GetValue<string>();
    var arguments = parameters?["arguments"]?.AsObject();

    if (string.IsNullOrWhiteSpace(toolName))
    {
        await WriteErrorResponseAsync(output, id, -32602, "Missing required parameter: name", framing);
        return;
    }

    var timeoutMs = ResolveToolTimeoutMs(toolName);
    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs));

    try
    {
        await DispatchToolCallAsync(
            output,
            id,
            toolName,
            arguments,
            queryService,
            managementService,
            sourcesManifestService,
            store,
            chunker,
            embeddingGenerator,
            indexer,
            settings,
            sourcePathMappings,
            jsonOptions,
            framing,
            timeoutCts.Token);
    }
    catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
    {
        var structuredResult = new JsonObject
        {
            ["toolName"] = toolName,
            ["timeoutMs"] = timeoutMs
        };

        await WriteResponseAsync(
            output,
            id,
            BuildToolResult($"Tool '{toolName}' timed out after {timeoutMs} ms.", structuredResult, isError: true),
            jsonOptions,
            framing);
    }
}

static async Task DispatchToolCallAsync(
    Stream output,
    JsonNode? id,
    string toolName,
    JsonObject? arguments,
    RagQueryService queryService,
    RagManagementService managementService,
    RagSourcesManifestService sourcesManifestService,
    PostgresRagStore store,
    TextChunker chunker,
    IEmbeddingGenerator embeddingGenerator,
    RagIndexer indexer,
    RagSettings settings,
    IReadOnlyList<SourcePathMapping> sourcePathMappings,
    JsonSerializerOptions jsonOptions,
    McpMessageFraming framing,
    CancellationToken cancellationToken)
{
    if (string.Equals(toolName, RagInitSchemaToolName, StringComparison.Ordinal))
    {
        await HandleRagInitSchemaToolAsync(output, id, store, sourcesManifestService, settings, jsonOptions, framing, cancellationToken);
        return;
    }

    if (string.Equals(toolName, QueryProjectRagToolName, StringComparison.Ordinal))
    {
        await HandleQueryProjectRagToolAsync(output, id, arguments, queryService, settings, jsonOptions, framing, cancellationToken);
        return;
    }

    if (string.Equals(toolName, RagHealthCheckToolName, StringComparison.Ordinal))
    {
        await HandleRagHealthCheckToolAsync(output, id, arguments, queryService, store, settings, jsonOptions, framing, cancellationToken);
        return;
    }

    if (string.Equals(toolName, RagServerInfoToolName, StringComparison.Ordinal))
    {
        await HandleRagServerInfoToolAsync(output, id, settings, sourcePathMappings, jsonOptions, framing, cancellationToken);
        return;
    }

    if (string.Equals(toolName, RagIndexStatsToolName, StringComparison.Ordinal))
    {
        await HandleRagIndexStatsToolAsync(output, id, store, jsonOptions, framing, cancellationToken);
        return;
    }

    if (string.Equals(toolName, RagRecentSourcesToolName, StringComparison.Ordinal))
    {
        await HandleRagRecentSourcesToolAsync(output, id, arguments, store, jsonOptions, framing, cancellationToken);
        return;
    }

    if (string.Equals(toolName, RagListSourcesToolName, StringComparison.Ordinal))
    {
        await HandleRagListSourcesToolAsync(output, id, arguments, managementService, jsonOptions, framing, cancellationToken);
        return;
    }

    if (string.Equals(toolName, RagIndexPathToolName, StringComparison.Ordinal))
    {
        await HandleRagIndexPathToolAsync(output, id, arguments, indexer, sourcesManifestService, sourcePathMappings, jsonOptions, framing, cancellationToken);
        return;
    }

    if (string.Equals(toolName, RagUpsertSourceToolName, StringComparison.Ordinal))
    {
        await HandleRagUpsertSourceToolAsync(output, id, arguments, store, chunker, embeddingGenerator, sourcesManifestService, settings, jsonOptions, framing, cancellationToken);
        return;
    }

    if (string.Equals(toolName, RagDeleteSourceToolName, StringComparison.Ordinal))
    {
        await HandleRagDeleteSourceToolAsync(output, id, arguments, managementService, sourcesManifestService, jsonOptions, framing, cancellationToken);
        return;
    }

    if (string.Equals(toolName, RagPurgeAllToolName, StringComparison.Ordinal))
    {
        await HandleRagPurgeAllToolAsync(output, id, arguments, managementService, sourcesManifestService, jsonOptions, framing, cancellationToken);
        return;
    }

    if (string.Equals(toolName, RagNormalizeSourcesToolName, StringComparison.Ordinal))
    {
        await HandleRagNormalizeSourcesToolAsync(output, id, store, sourcesManifestService, jsonOptions, framing, cancellationToken);
        return;
    }

    await WriteErrorResponseAsync(output, id, -32602, "Unknown tool name.", framing);
}

static int ResolveToolTimeoutMs(string toolName)
{
    if (string.Equals(toolName, RagInitSchemaToolName, StringComparison.Ordinal))
    {
        return InitSchemaToolTimeoutMs;
    }

    if (string.Equals(toolName, QueryProjectRagToolName, StringComparison.Ordinal))
    {
        return QueryToolTimeoutMs;
    }

    if (string.Equals(toolName, RagHealthCheckToolName, StringComparison.Ordinal))
    {
        return HealthToolTimeoutMs;
    }

    if (string.Equals(toolName, RagServerInfoToolName, StringComparison.Ordinal))
    {
        return ServerInfoToolTimeoutMs;
    }

    if (string.Equals(toolName, RagIndexStatsToolName, StringComparison.Ordinal))
    {
        return IndexStatsToolTimeoutMs;
    }

    if (string.Equals(toolName, RagRecentSourcesToolName, StringComparison.Ordinal))
    {
        return RecentSourcesToolTimeoutMs;
    }

    if (string.Equals(toolName, RagListSourcesToolName, StringComparison.Ordinal))
    {
        return ListSourcesToolTimeoutMs;
    }

    if (string.Equals(toolName, RagIndexPathToolName, StringComparison.Ordinal))
    {
        return IndexPathToolTimeoutMs;
    }

    if (string.Equals(toolName, RagUpsertSourceToolName, StringComparison.Ordinal))
    {
        return UpsertSourceToolTimeoutMs;
    }

    if (string.Equals(toolName, RagDeleteSourceToolName, StringComparison.Ordinal))
    {
        return DeleteSourceToolTimeoutMs;
    }

    if (string.Equals(toolName, RagPurgeAllToolName, StringComparison.Ordinal))
    {
        return PurgeAllToolTimeoutMs;
    }

    if (string.Equals(toolName, RagNormalizeSourcesToolName, StringComparison.Ordinal))
    {
        return NormalizeSourcesToolTimeoutMs;
    }

    return DefaultToolTimeoutMs;
}

static async Task HandleQueryProjectRagToolAsync(
    Stream output,
    JsonNode? id,
    JsonObject? arguments,
    RagQueryService queryService,
    RagSettings settings,
    JsonSerializerOptions jsonOptions,
    McpMessageFraming framing,
    CancellationToken cancellationToken)
{
    var text = arguments?["text"]?.GetValue<string>();
    if (string.IsNullOrWhiteSpace(text))
    {
        await WriteErrorResponseAsync(output, id, -32602, "Missing required argument: text", framing);
        return;
    }

    var limit = settings.Retrieval.DefaultTopK;
    var limitNode = arguments?["limit"];
    if (limitNode is not null && int.TryParse(limitNode.ToString(), out var parsedLimit) && parsedLimit > 0)
    {
        limit = Math.Min(parsedLimit, 20);
    }

    var sourcePathContains = arguments?["sourcePathContains"]?.GetValue<string>();
    var minScore = ParseMinScore(arguments?["minScore"]);

    try
    {
        var queryLimit = sourcePathContains is null && minScore is null
            ? limit
            : Math.Min(limit * 5, 100);

        var rawResults = await queryService.QueryAsync(text, queryLimit, cancellationToken);
        var filteredResults = rawResults
            .Where(r => sourcePathContains is null || r.SourcePath.Contains(sourcePathContains, StringComparison.OrdinalIgnoreCase))
            .Where(r => minScore is null || r.Score >= minScore.Value)
            .Take(limit)
            .ToList();

        var textResult = FormatResults(filteredResults);
        var structuredResult = BuildQueryStructuredResult(text, limit, sourcePathContains, minScore, filteredResults);

        await WriteResponseAsync(output, id, BuildToolResult(textResult, structuredResult), jsonOptions, framing);
    }
    catch (OperationCanceledException)
    {
        throw;
    }
    catch (Exception ex)
    {
        await WriteResponseAsync(output, id, BuildToolResult($"RAG query failed: {ex.Message}", isError: true), jsonOptions, framing);
    }
}

static async Task HandleRagInitSchemaToolAsync(
    Stream output,
    JsonNode? id,
    PostgresRagStore store,
    RagSourcesManifestService sourcesManifestService,
    RagSettings settings,
    JsonSerializerOptions jsonOptions,
    McpMessageFraming framing,
    CancellationToken cancellationToken)
{
    try
    {
        await store.InitializeSchemaAsync(settings.Ingestion.VectorDimensions, cancellationToken);
        var manifestSyncResult = await TrySyncRagSourcesManifestAsync(sourcesManifestService, null, cancellationToken);
        var structuredResult = new JsonObject
        {
            ["vectorDimensions"] = settings.Ingestion.VectorDimensions,
            ["sourcesManifestPath"] = manifestSyncResult?.ManifestPath,
            ["sourcesManifestSourceCount"] = manifestSyncResult?.SourceCount
        };

        await WriteResponseAsync(output, id, BuildToolResult("Nebula RAG schema initialized.", structuredResult), jsonOptions, framing);
    }
    catch (OperationCanceledException)
    {
        throw;
    }
    catch (Exception ex)
    {
        await WriteResponseAsync(output, id, BuildToolResult($"Failed to initialize schema: {ex.Message}", isError: true), jsonOptions, framing);
    }
}

static async Task HandleRagHealthCheckToolAsync(
    Stream output,
    JsonNode? id,
    JsonObject? arguments,
    RagQueryService queryService,
    PostgresRagStore store,
    RagSettings settings,
    JsonSerializerOptions jsonOptions,
    McpMessageFraming framing,
    CancellationToken cancellationToken)
{
    var probeQuery = arguments?["smokeQuery"]?.GetValue<string>();
    if (string.IsNullOrWhiteSpace(probeQuery))
    {
        probeQuery = "nebula rag health probe";
    }

    try
    {
        await store.InitializeSchemaAsync(settings.Ingestion.VectorDimensions, cancellationToken);
        var results = await queryService.QueryAsync(probeQuery, 1, cancellationToken);
        var structuredResult = new JsonObject
        {
            ["status"] = "healthy",
            ["probeQuery"] = probeQuery,
            ["vectorDimensions"] = settings.Ingestion.VectorDimensions,
            ["defaultTopK"] = settings.Retrieval.DefaultTopK,
            ["retrievalProbeCount"] = results.Count
        };

        await WriteResponseAsync(output, id, BuildToolResult("Nebula RAG MCP health check passed.", structuredResult), jsonOptions, framing);
    }
    catch (OperationCanceledException)
    {
        throw;
    }
    catch (Exception ex)
    {
        await WriteResponseAsync(output, id, BuildToolResult($"Nebula RAG MCP health check failed: {ex.Message}", isError: true), jsonOptions, framing);
    }
}

static async Task HandleRagServerInfoToolAsync(Stream output, JsonNode? id, RagSettings settings, IReadOnlyList<SourcePathMapping> sourcePathMappings, JsonSerializerOptions jsonOptions, McpMessageFraming framing, CancellationToken cancellationToken)
{
    cancellationToken.ThrowIfCancellationRequested();

    var toolCount = BuildToolsList()["tools"]?.AsArray()?.Count ?? 0;
    var mappings = new JsonArray();
    foreach (var mapping in sourcePathMappings)
    {
        mappings.Add(new JsonObject
        {
            ["callerPrefix"] = mapping.CallerPrefix,
            ["runtimePrefix"] = mapping.RuntimePrefix
        });
    }

    var structuredResult = new JsonObject
    {
        ["serverName"] = "Nebula RAG MCP",
        ["serverVersion"] = "1.0.0",
        ["databaseHost"] = settings.Database.Host,
        ["databasePort"] = settings.Database.Port,
        ["databaseName"] = settings.Database.Database,
        ["vectorDimensions"] = settings.Ingestion.VectorDimensions,
        ["defaultTopK"] = settings.Retrieval.DefaultTopK,
        ["toolCount"] = toolCount,
        ["cwd"] = Directory.GetCurrentDirectory(),
        ["pathMappings"] = mappings
    };

    await WriteResponseAsync(output, id, BuildToolResult("Nebula RAG MCP server information.", structuredResult), jsonOptions, framing);
}

static async Task HandleRagIndexStatsToolAsync(Stream output, JsonNode? id, PostgresRagStore store, JsonSerializerOptions jsonOptions, McpMessageFraming framing, CancellationToken cancellationToken)
{
    try
    {
        var stats = await store.GetIndexStatsAsync(cancellationToken: cancellationToken);
        var structuredResult = new JsonObject
        {
            ["documentCount"] = stats.DocumentCount,
            ["chunkCount"] = stats.ChunkCount,
            ["totalTokens"] = stats.TotalTokens,
            ["oldestIndexedAt"] = stats.OldestIndexedAt?.ToString("O"),
            ["newestIndexedAt"] = stats.NewestIndexedAt?.ToString("O")
        };

        await WriteResponseAsync(output, id, BuildToolResult("Nebula RAG index statistics retrieved.", structuredResult), jsonOptions, framing);
    }
    catch (OperationCanceledException)
    {
        throw;
    }
    catch (Exception ex)
    {
        await WriteResponseAsync(output, id, BuildToolResult($"Failed to retrieve index statistics: {ex.Message}", isError: true), jsonOptions, framing);
    }
}

static async Task HandleRagRecentSourcesToolAsync(
    Stream output,
    JsonNode? id,
    JsonObject? arguments,
    PostgresRagStore store,
    JsonSerializerOptions jsonOptions,
    McpMessageFraming framing,
    CancellationToken cancellationToken)
{
    var limit = 10;
    var limitNode = arguments?["limit"];
    if (limitNode is not null && int.TryParse(limitNode.ToString(), out var parsedLimit) && parsedLimit > 0)
    {
        limit = Math.Min(parsedLimit, 50);
    }

    try
    {
        var recentDocuments = await store.GetRecentDocumentsAsync(limit, cancellationToken);
        var items = new JsonArray();
        foreach (var document in recentDocuments)
        {
            items.Add(new JsonObject
            {
                ["sourcePath"] = document.SourcePath,
                ["indexedAtUtc"] = document.IndexedAtUtc.ToUniversalTime().ToString("O"),
                ["chunkCount"] = document.ChunkCount
            });
        }

        var structuredResult = new JsonObject
        {
            ["count"] = recentDocuments.Count,
            ["items"] = items
        };

        await WriteResponseAsync(output, id, BuildToolResult("Nebula RAG recent sources retrieved.", structuredResult), jsonOptions, framing);
    }
    catch (OperationCanceledException)
    {
        throw;
    }
    catch (Exception ex)
    {
        await WriteResponseAsync(output, id, BuildToolResult($"Failed to retrieve recent sources: {ex.Message}", isError: true), jsonOptions, framing);
    }
}

static async Task HandleRagListSourcesToolAsync(
    Stream output,
    JsonNode? id,
    JsonObject? arguments,
    RagManagementService managementService,
    JsonSerializerOptions jsonOptions,
    McpMessageFraming framing,
    CancellationToken cancellationToken)
{
    var limit = 100;
    if (arguments?["limit"] is not null && int.TryParse(arguments["limit"]!.ToString(), out var parsedLimit) && parsedLimit > 0)
    {
        limit = Math.Min(parsedLimit, 500);
    }

    try
    {
        var selected = await managementService.ListSourcesAsync(limit, cancellationToken);
        var items = new JsonArray();
        foreach (var source in selected)
        {
            items.Add(new JsonObject
            {
                ["sourcePath"] = source.SourcePath,
                ["chunkCount"] = source.ChunkCount,
                ["indexedAtUtc"] = source.IndexedAt.ToUniversalTime().ToString("O")
            });
        }

        var structuredResult = new JsonObject
        {
            ["count"] = selected.Count,
            ["items"] = items
        };

        await WriteResponseAsync(output, id, BuildToolResult("Nebula RAG indexed sources retrieved.", structuredResult), jsonOptions, framing);
    }
    catch (OperationCanceledException)
    {
        throw;
    }
    catch (Exception ex)
    {
        await WriteResponseAsync(output, id, BuildToolResult($"Failed to list sources: {ex.Message}", isError: true), jsonOptions, framing);
    }
}

static async Task HandleRagDeleteSourceToolAsync(
    Stream output,
    JsonNode? id,
    JsonObject? arguments,
    RagManagementService managementService,
    RagSourcesManifestService sourcesManifestService,
    JsonSerializerOptions jsonOptions,
    McpMessageFraming framing,
    CancellationToken cancellationToken)
{
    var sourcePath = arguments?["sourcePath"]?.GetValue<string>();
    var projectRootPath = Directory.GetCurrentDirectory();
    var confirm = arguments?["confirm"]?.GetValue<bool>() == true;

    if (string.IsNullOrWhiteSpace(sourcePath))
    {
        await WriteErrorResponseAsync(output, id, -32602, "Missing required argument: sourcePath", framing);
        return;
    }

    if (!confirm)
    {
        await WriteErrorResponseAsync(output, id, -32602, "Deletion requires confirm=true.", framing);
        return;
    }

    try
    {
        var normalizedSourcePath = SourcePathNormalizer.NormalizeForStorage(sourcePath, projectRootPath);
        var deletedCount = await managementService.DeleteSourceAsync(normalizedSourcePath, cancellationToken);
        var manifestSyncResult = await TrySyncRagSourcesManifestAsync(sourcesManifestService, normalizedSourcePath, cancellationToken);
        var structuredResult = new JsonObject
        {
            ["sourcePath"] = normalizedSourcePath,
            ["deletedCount"] = deletedCount,
            ["sourcesManifestPath"] = manifestSyncResult?.ManifestPath,
            ["sourcesManifestSourceCount"] = manifestSyncResult?.SourceCount
        };

        await WriteResponseAsync(output, id, BuildToolResult($"Deleted {deletedCount} document rows for source '{normalizedSourcePath}'.", structuredResult), jsonOptions, framing);
    }
    catch (OperationCanceledException)
    {
        throw;
    }
    catch (Exception ex)
    {
        await WriteResponseAsync(output, id, BuildToolResult($"Failed to delete source: {ex.Message}", isError: true), jsonOptions, framing);
    }
}

static async Task HandleRagUpsertSourceToolAsync(
    Stream output,
    JsonNode? id,
    JsonObject? arguments,
    PostgresRagStore store,
    TextChunker chunker,
    IEmbeddingGenerator embeddingGenerator,
    RagSourcesManifestService sourcesManifestService,
    RagSettings settings,
    JsonSerializerOptions jsonOptions,
    McpMessageFraming framing,
    CancellationToken cancellationToken)
{
    var sourcePath = arguments?["sourcePath"]?.GetValue<string>();
    var projectRootPath = Directory.GetCurrentDirectory();
    var content = arguments?["content"]?.GetValue<string>();

    if (string.IsNullOrWhiteSpace(sourcePath))
    {
        await WriteErrorResponseAsync(output, id, -32602, "Missing required argument: sourcePath", framing);
        return;
    }

    if (string.IsNullOrWhiteSpace(content))
    {
        await WriteErrorResponseAsync(output, id, -32602, "Missing required argument: content", framing);
        return;
    }

    try
    {
        var chunks = chunker.Chunk(content, settings.Ingestion.ChunkSize, settings.Ingestion.ChunkOverlap);
        if (chunks.Count == 0)
        {
            await WriteResponseAsync(output, id, BuildToolResult("No indexable chunks were produced from the provided content.", isError: true), jsonOptions, framing);
            return;
        }

        var chunkEmbeddings = chunks
            .Select(chunk => new ChunkEmbedding(
                chunk.Index,
                chunk.Text,
                chunk.TokenCount,
                embeddingGenerator.GenerateEmbedding(chunk.Text, settings.Ingestion.VectorDimensions)))
            .ToList();

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
        var normalizedSourcePath = SourcePathNormalizer.NormalizeForStorage(sourcePath, projectRootPath);
        var updated = await store.UpsertDocumentAsync(normalizedSourcePath, hash, chunkEmbeddings, cancellationToken);
        var manifestSyncResult = await TrySyncRagSourcesManifestAsync(sourcesManifestService, normalizedSourcePath, cancellationToken);

        var structuredResult = new JsonObject
        {
            ["sourcePath"] = normalizedSourcePath,
            ["updated"] = updated,
            ["chunkCount"] = chunkEmbeddings.Count,
            ["contentHash"] = hash,
            ["sourcesManifestPath"] = manifestSyncResult?.ManifestPath,
            ["sourcesManifestSourceCount"] = manifestSyncResult?.SourceCount
        };

        var text = updated
            ? $"Indexed source '{normalizedSourcePath}' with {chunkEmbeddings.Count} chunks."
            : $"Source '{normalizedSourcePath}' is unchanged and was not re-indexed.";

        await WriteResponseAsync(output, id, BuildToolResult(text, structuredResult), jsonOptions, framing);
    }
    catch (OperationCanceledException)
    {
        throw;
    }
    catch (Exception ex)
    {
        await WriteResponseAsync(output, id, BuildToolResult($"Failed to upsert source: {ex.Message}", isError: true), jsonOptions, framing);
    }
}

static async Task HandleRagIndexPathToolAsync(
    Stream output,
    JsonNode? id,
    JsonObject? arguments,
    RagIndexer indexer,
    RagSourcesManifestService sourcesManifestService,
    IReadOnlyList<SourcePathMapping> sourcePathMappings,
    JsonSerializerOptions jsonOptions,
    McpMessageFraming framing,
    CancellationToken cancellationToken)
{
    var sourcePath = arguments?["sourcePath"]?.GetValue<string>();

    if (string.IsNullOrWhiteSpace(sourcePath))
    {
        await WriteErrorResponseAsync(output, id, -32602, "Missing required argument: sourcePath", framing);
        return;
    }

    try
    {
        var resolvedSourcePath = ResolveSourcePathForRuntime(sourcePath, sourcePathMappings);
        if (!Directory.Exists(resolvedSourcePath))
        {
            var mappingHint = sourcePathMappings.Count == 0
                ? $" Configure {PathMappingsEnvironmentVariable} to map caller paths to runtime mount paths (example: C:\\repo=/workspace)."
                : string.Empty;
            throw new DirectoryNotFoundException($"Resolved source directory does not exist. Requested='{sourcePath}' Resolved='{resolvedSourcePath}'.{mappingHint}");
        }

        var summary = await indexer.IndexDirectoryAsync(resolvedSourcePath, cancellationToken);
        var manifestSyncResult = await TrySyncRagSourcesManifestAsync(sourcesManifestService, resolvedSourcePath, cancellationToken);
        var structuredResult = new JsonObject
        {
            ["sourcePath"] = sourcePath,
            ["resolvedSourcePath"] = resolvedSourcePath,
            ["documentsIndexed"] = summary.DocumentsIndexed,
            ["documentsSkipped"] = summary.DocumentsSkipped,
            ["chunksIndexed"] = summary.ChunksIndexed,
            ["sourcesManifestPath"] = manifestSyncResult?.ManifestPath,
            ["sourcesManifestSourceCount"] = manifestSyncResult?.SourceCount
        };

        var text =
            $"Index complete for '{sourcePath}' (resolved to '{resolvedSourcePath}'): {summary.DocumentsIndexed} documents indexed, {summary.ChunksIndexed} chunks, {summary.DocumentsSkipped} skipped.";
        await WriteResponseAsync(output, id, BuildToolResult(text, structuredResult), jsonOptions, framing);
    }
    catch (OperationCanceledException)
    {
        throw;
    }
    catch (Exception ex)
    {
        await WriteResponseAsync(output, id, BuildToolResult($"Failed to index path: {ex.Message}", isError: true), jsonOptions, framing);
    }
}

static async Task HandleRagPurgeAllToolAsync(
    Stream output,
    JsonNode? id,
    JsonObject? arguments,
    RagManagementService managementService,
    RagSourcesManifestService sourcesManifestService,
    JsonSerializerOptions jsonOptions,
    McpMessageFraming framing,
    CancellationToken cancellationToken)
{
    var confirmPhrase = arguments?["confirmPhrase"]?.GetValue<string>();

    if (!string.Equals(confirmPhrase, "PURGE ALL", StringComparison.Ordinal))
    {
        await WriteErrorResponseAsync(output, id, -32602, "Purge requires confirmPhrase='PURGE ALL'.", framing);
        return;
    }

    try
    {
        await managementService.PurgeAllAsync(cancellationToken);
        var manifestSyncResult = await TrySyncRagSourcesManifestAsync(sourcesManifestService, null, cancellationToken);
        var structuredResult = new JsonObject
        {
            ["sourcesManifestPath"] = manifestSyncResult?.ManifestPath,
            ["sourcesManifestSourceCount"] = manifestSyncResult?.SourceCount
        };

        await WriteResponseAsync(output, id, BuildToolResult("Nebula RAG index purge complete.", structuredResult), jsonOptions, framing);
    }
    catch (OperationCanceledException)
    {
        throw;
    }
    catch (Exception ex)
    {
        await WriteResponseAsync(output, id, BuildToolResult($"Failed to purge index: {ex.Message}", isError: true), jsonOptions, framing);
    }
}

static async Task HandleRagNormalizeSourcesToolAsync(
    Stream output,
    JsonNode? id,
    PostgresRagStore store,
    RagSourcesManifestService sourcesManifestService,
    JsonSerializerOptions jsonOptions,
    McpMessageFraming framing,
    CancellationToken cancellationToken)
{
    try
    {
        var projectRootPath = Directory.GetCurrentDirectory();
        var (updatedCount, duplicatesRemoved) = await store.NormalizeSourcePathsAsync(projectRootPath, cancellationToken);
        var manifestSyncResult = await TrySyncRagSourcesManifestAsync(sourcesManifestService, projectRootPath, cancellationToken);
        var structuredResult = new JsonObject
        {
            ["updatedCount"] = updatedCount,
            ["duplicatesRemoved"] = duplicatesRemoved,
            ["sourcesManifestPath"] = manifestSyncResult?.ManifestPath,
            ["sourcesManifestSourceCount"] = manifestSyncResult?.SourceCount
        };

        var text = $"Source normalization complete: {updatedCount} source paths updated, {duplicatesRemoved} duplicates removed.";
        await WriteResponseAsync(output, id, BuildToolResult(text, structuredResult), jsonOptions, framing);
    }
    catch (OperationCanceledException)
    {
        throw;
    }
    catch (Exception ex)
    {
        await WriteResponseAsync(output, id, BuildToolResult($"Failed to normalize sources: {ex.Message}", isError: true), jsonOptions, framing);
    }
}

static JsonObject BuildQueryStructuredResult(
    string query,
    int limit,
    string? sourcePathContains,
    double? minScore,
    IReadOnlyList<RagSearchResult> results)
{
    var matches = new JsonArray();
    foreach (var result in results)
    {
        var snippet = result.ChunkText.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (snippet.Length > 280)
        {
            snippet = $"{snippet[..280]}...";
        }

        matches.Add(new JsonObject
        {
            ["sourcePath"] = result.SourcePath,
            ["chunkIndex"] = result.ChunkIndex,
            ["score"] = result.Score,
            ["snippet"] = snippet
        });
    }

    return new JsonObject
    {
        ["query"] = query,
        ["limit"] = limit,
        ["matchCount"] = results.Count,
        ["sourcePathContains"] = sourcePathContains,
        ["minScore"] = minScore,
        ["matches"] = matches
    };
}

static double? ParseMinScore(JsonNode? minScoreNode)
{
    if (minScoreNode is null)
    {
        return null;
    }

    if (!double.TryParse(minScoreNode.ToString(), out var minScore))
    {
        return null;
    }

    return minScore;
}

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
/// Synchronizes rag-sources markdown without failing a successful tool operation.
/// </summary>
/// <param name="sourcesManifestService">Service that writes rag-sources markdown.</param>
/// <param name="contextPath">Optional source path to help determine output location.</param>
/// <param name="cancellationToken">Cancellation token.</param>
/// <returns>Sync result when successful; otherwise <c>null</c>.</returns>
static async Task<RagSourcesManifestSyncResult?> TrySyncRagSourcesManifestAsync(
    RagSourcesManifestService sourcesManifestService,
    string? contextPath,
    CancellationToken cancellationToken)
{
    try
    {
        return await sourcesManifestService.SyncAsync(contextPath, cancellationToken);
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "Failed to synchronize rag-sources.md after a successful MCP operation.");
        return null;
    }
}

static string FormatResults(IReadOnlyList<NebulaRAG.Core.Models.RagSearchResult> results)
{
    if (results.Count == 0)
    {
        return "No RAG matches were found for this query.";
    }

    var builder = new StringBuilder();
    builder.AppendLine("Nebula RAG context:");
    foreach (var result in results)
    {
        var snippet = result.ChunkText.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (snippet.Length > 280)
        {
            snippet = $"{snippet[..280]}...";
        }

        builder.AppendLine($"- Score {result.Score:F4} | {result.SourcePath}#{result.ChunkIndex}");
        builder.AppendLine($"  {snippet}");
    }

    return builder.ToString();
}

static async Task<(string? Payload, McpMessageFraming Framing)> ReadMessageAsync(Stream input)
{
    var firstByteBuffer = new byte[1];
    var firstRead = await input.ReadAsync(firstByteBuffer);
    if (firstRead == 0)
    {
        return (null, McpMessageFraming.Unknown);
    }

    var firstChar = (char)firstByteBuffer[0];
    if (firstChar == '{' || firstChar == '[')
    {
        var payloadBuilder = new StringBuilder();
        payloadBuilder.Append(firstChar);
        var newlineBuffer = new byte[1];

        while (true)
        {
            var read = await input.ReadAsync(newlineBuffer);
            if (read == 0)
            {
                break;
            }

            var ch = (char)newlineBuffer[0];
            if (ch == '\n')
            {
                break;
            }

            if (ch != '\r')
            {
                payloadBuilder.Append(ch);
            }
        }

        return (payloadBuilder.ToString(), McpMessageFraming.NewlineDelimitedJsonRpc);
    }

    var headerBuilder = new StringBuilder();
    headerBuilder.Append(firstChar);
    var headerByte = new byte[1];

    while (true)
    {
        var read = await input.ReadAsync(headerByte);
        if (read == 0)
        {
            return (null, McpMessageFraming.ContentLength);
        }

        headerBuilder.Append((char)headerByte[0]);
        var headerText = headerBuilder.ToString();
        if (headerText.EndsWith("\r\n\r\n", StringComparison.Ordinal) ||
            headerText.EndsWith("\n\n", StringComparison.Ordinal))
        {
            break;
        }
    }

    var headerLines = headerBuilder
        .ToString()
        .Replace("\r\n", "\n", StringComparison.Ordinal)
        .Split('\n', StringSplitOptions.RemoveEmptyEntries);

    var contentLength = 0;
    foreach (var line in headerLines)
    {
        if (!line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        var value = line["Content-Length:".Length..].Trim();
        if (int.TryParse(value, out var parsedLength))
        {
            contentLength = parsedLength;
        }
    }

    if (contentLength <= 0)
    {
        return (null, McpMessageFraming.ContentLength);
    }

    var body = new byte[contentLength];
    var offset = 0;
    while (offset < contentLength)
    {
        var read = await input.ReadAsync(body.AsMemory(offset, contentLength - offset));
        if (read == 0)
        {
            return (null, McpMessageFraming.ContentLength);
        }

        offset += read;
    }

    return (Encoding.UTF8.GetString(body), McpMessageFraming.ContentLength);
}

static async Task WriteResponseAsync(Stream output, JsonNode? id, JsonObject result, JsonSerializerOptions jsonOptions, McpMessageFraming framing)
{
    var response = new JsonObject
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["result"] = result
    };

    await WriteMessageAsync(output, response.ToJsonString(jsonOptions), framing);
}

static async Task WriteErrorResponseAsync(
    Stream output,
    JsonNode? id,
    int code,
    string message,
    McpMessageFraming framing,
    JsonNode? errorData = null)
{
    var errorNode = new JsonObject
    {
        ["code"] = code,
        ["message"] = message
    };

    if (errorData is not null)
    {
        errorNode["data"] = errorData;
    }

    var response = new JsonObject
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["error"] = errorNode
    };

    await WriteMessageAsync(output, response.ToJsonString(), framing);
}

static async Task WriteMessageAsync(Stream output, string json, McpMessageFraming framing)
{
    if (framing == McpMessageFraming.NewlineDelimitedJsonRpc)
    {
        var newlinePayload = Encoding.UTF8.GetBytes($"{json}\n");
        await output.WriteAsync(newlinePayload);
        await output.FlushAsync();
        return;
    }

    var payloadBytes = Encoding.UTF8.GetBytes(json);
    var headerBytes = Encoding.ASCII.GetBytes($"Content-Length: {payloadBytes.Length}\r\n\r\n");
    await output.WriteAsync(headerBytes);
    await output.WriteAsync(payloadBytes);
    await output.FlushAsync();
}

static string? NegotiateProtocolVersion(string? requestedVersion, IReadOnlyList<string> supportedProtocolVersions)
{
    if (!string.IsNullOrWhiteSpace(requestedVersion) &&
        supportedProtocolVersions.Contains(requestedVersion, StringComparer.Ordinal))
    {
        return requestedVersion;
    }

    // Fallback for older clients that omit protocolVersion in initialize.
    if (string.IsNullOrWhiteSpace(requestedVersion))
    {
        return supportedProtocolVersions[^1];
    }

    return supportedProtocolVersions[^1];
}

static string? GetConfigPath(string[] args)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], "--config", StringComparison.OrdinalIgnoreCase))
        {
            return args[i + 1];
        }
    }

    return Environment.GetEnvironmentVariable("NEBULARAG_CONFIG");
}

static bool HasFlag(IEnumerable<string> args, string flag)
{
    return args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));
}

static RagSettings LoadSettings(string? configPath)
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
        var baseConfigPath = ResolveConfigPath("ragsettings.json")
                             ?? throw new FileNotFoundException("Could not locate required config file 'ragsettings.json'.");
        configBuilder.AddJsonFile(baseConfigPath, optional: false, reloadOnChange: false);

        var localConfigPath = ResolveConfigPath("ragsettings.local.json");
        if (localConfigPath is not null)
        {
            configBuilder.AddJsonFile(localConfigPath, optional: true, reloadOnChange: false);
        }
    }

    configBuilder.AddEnvironmentVariables(prefix: "NEBULARAG_");

    return configBuilder.Build().Get<RagSettings>() ?? new RagSettings();
}

static string? ResolveConfigPath(string fileName)
{
    var candidates = new[]
    {
        Path.Combine(Directory.GetCurrentDirectory(), fileName),
        Path.Combine(AppContext.BaseDirectory, fileName),
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "NebulaRAG.Cli", fileName))
    };

    foreach (var candidate in candidates)
    {
        if (File.Exists(candidate))
        {
            return candidate;
        }
    }

    return null;
}

/// <summary>
/// Loads caller-to-runtime path mappings from NEBULARAG_PathMappings.
/// </summary>
/// <remarks>
/// Format: "callerPrefix=runtimePrefix;callerPrefix2=runtimePrefix2".
/// </remarks>
static IReadOnlyList<SourcePathMapping> LoadSourcePathMappings()
{
    var rawValue = Environment.GetEnvironmentVariable(PathMappingsEnvironmentVariable);
    if (string.IsNullOrWhiteSpace(rawValue))
    {
        return [];
    }

    var mappings = new List<SourcePathMapping>();
    var entries = rawValue.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    foreach (var entry in entries)
    {
        var separatorIndex = entry.IndexOf('=');
        if (separatorIndex <= 0 || separatorIndex >= entry.Length - 1)
        {
            continue;
        }

        var callerPrefix = entry[..separatorIndex].Trim();
        var runtimePrefix = entry[(separatorIndex + 1)..].Trim();
        if (callerPrefix.Length == 0 || runtimePrefix.Length == 0)
        {
            continue;
        }

        mappings.Add(new SourcePathMapping(NormalizeCallerPath(callerPrefix), NormalizeRuntimePath(runtimePrefix)));
    }

    return mappings;
}

/// <summary>
/// Resolves a caller-provided source path into a runtime-accessible directory path.
/// </summary>
static string ResolveSourcePathForRuntime(string sourcePath, IReadOnlyList<SourcePathMapping> sourcePathMappings)
{
    var candidatePath = sourcePath.Trim();

    if (!Path.IsPathRooted(candidatePath))
    {
        return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), candidatePath));
    }

    if (Directory.Exists(candidatePath))
    {
        return Path.GetFullPath(candidatePath);
    }

    var mappedPath = TryMapCallerPath(candidatePath, sourcePathMappings);
    return mappedPath ?? candidatePath;
}

/// <summary>
/// Attempts to map a caller path prefix to a runtime path prefix.
/// </summary>
static string? TryMapCallerPath(string callerPath, IReadOnlyList<SourcePathMapping> sourcePathMappings)
{
    var normalizedCallerPath = NormalizeCallerPath(callerPath);

    foreach (var mapping in sourcePathMappings)
    {
        if (!IsPrefixMatch(normalizedCallerPath, mapping.CallerPrefix))
        {
            continue;
        }

        var suffix = normalizedCallerPath[mapping.CallerPrefix.Length..].TrimStart('/', '\\');
        var normalizedSuffix = suffix.Replace('\\', '/');
        if (normalizedSuffix.Length == 0)
        {
            return mapping.RuntimePrefix;
        }

        return $"{mapping.RuntimePrefix}/{normalizedSuffix}";
    }

    return null;
}

/// <summary>
/// Normalizes caller path prefixes for matching.
/// </summary>
static string NormalizeCallerPath(string path)
{
    var normalized = path.Trim();
    return normalized.TrimEnd('/', '\\');
}

/// <summary>
/// Normalizes runtime path prefixes for composition.
/// </summary>
static string NormalizeRuntimePath(string path)
{
    var normalized = path.Trim().Replace('\\', '/');
    return normalized.TrimEnd('/');
}

/// <summary>
/// Checks whether <paramref name="path"/> starts with <paramref name="prefix"/> on a segment boundary.
/// </summary>
static bool IsPrefixMatch(string path, string prefix)
{
    if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    if (path.Length == prefix.Length)
    {
        return true;
    }

    var boundaryChar = path[prefix.Length];
    return boundaryChar == '/' || boundaryChar == '\\';
}

file enum McpMessageFraming
{
    Unknown = 0,
    ContentLength = 1,
    NewlineDelimitedJsonRpc = 2
}

file sealed record SelfTestCase(string Name, string Description, Func<CancellationToken, Task> Run);

file sealed record SourcePathMapping(string CallerPrefix, string RuntimePrefix);
