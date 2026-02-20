using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using NebulaRAG.Core.Configuration;
using NebulaRAG.Core.Embeddings;
using NebulaRAG.Core.Models;
using NebulaRAG.Core.Services;
using NebulaRAG.Core.Storage;

const string QueryProjectRagToolName = "query_project_rag";
const string RagHealthCheckToolName = "rag_health_check";
const string RagServerInfoToolName = "rag_server_info";
const string RagIndexStatsToolName = "rag_index_stats";
const string RagRecentSourcesToolName = "rag_recent_sources";

var configPath = GetConfigPath(args);
var settings = LoadSettings(configPath);
var runSelfTests = !HasFlag(args, "--skip-self-test");
var selfTestOnly = HasFlag(args, "--self-test-only");
var store = new PostgresRagStore(settings.Database.BuildConnectionString());
var embeddingGenerator = new HashEmbeddingGenerator();
var queryService = new RagQueryService(store, embeddingGenerator, settings);

if (runSelfTests)
{
    await RunStartupSelfTestsAsync(store, queryService, settings);
}

if (selfTestOnly)
{
    return;
}

await RunServerAsync(queryService, store, settings);
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
        !toolNames.Contains(RagHealthCheckToolName) ||
        !toolNames.Contains(RagServerInfoToolName) ||
        !toolNames.Contains(RagIndexStatsToolName) ||
        !toolNames.Contains(RagRecentSourcesToolName))
    {
        throw new InvalidOperationException("Required Nebula RAG MCP tools are not fully registered.");
    }
}

static async Task RunServerAsync(RagQueryService queryService, PostgresRagStore store, RagSettings settings)
{
    using var input = Console.OpenStandardInput();
    using var output = Console.OpenStandardOutput();
    var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    while (true)
    {
        var payload = await ReadMessageAsync(input);
        if (payload is null)
        {
            return;
        }

        JsonObject request;
        try
        {
            request = JsonNode.Parse(payload)?.AsObject()
                      ?? throw new InvalidOperationException("Invalid JSON request.");
        }
        catch
        {
            await WriteErrorResponseAsync(output, null, -32700, "Parse error");
            continue;
        }

        var method = request["method"]?.GetValue<string>();
        var id = request["id"]?.DeepClone();
        if (string.IsNullOrWhiteSpace(method))
        {
            await WriteErrorResponseAsync(output, id, -32600, "Invalid request");
            continue;
        }

        if (method.StartsWith("notifications/", StringComparison.Ordinal))
        {
            continue;
        }

        switch (method)
        {
            case "initialize":
                await WriteResponseAsync(output, id, new JsonObject
                {
                    ["protocolVersion"] = "2025-06-18",
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
                }, jsonOptions);
                break;

            case "ping":
                await WriteResponseAsync(output, id, new JsonObject(), jsonOptions);
                break;

            case "tools/list":
                await WriteResponseAsync(output, id, BuildToolsList(), jsonOptions);
                break;

            case "tools/call":
                await HandleToolsCallAsync(output, id, request["params"]?.AsObject(), queryService, store, settings, jsonOptions);
                break;

            default:
                await WriteErrorResponseAsync(output, id, -32601, $"Method not found: {method}");
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
            BuildQueryProjectRagToolDefinition(),
            BuildRagHealthCheckToolDefinition(),
            BuildRagServerInfoToolDefinition(),
            BuildRagIndexStatsToolDefinition(),
            BuildRagRecentSourcesToolDefinition()
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

static async Task HandleToolsCallAsync(
    Stream output,
    JsonNode? id,
    JsonObject? parameters,
    RagQueryService queryService,
    PostgresRagStore store,
    RagSettings settings,
    JsonSerializerOptions jsonOptions)
{
    var toolName = parameters?["name"]?.GetValue<string>();
    var arguments = parameters?["arguments"]?.AsObject();

    if (string.Equals(toolName, QueryProjectRagToolName, StringComparison.Ordinal))
    {
        await HandleQueryProjectRagToolAsync(output, id, arguments, queryService, settings, jsonOptions);
        return;
    }

    if (string.Equals(toolName, RagHealthCheckToolName, StringComparison.Ordinal))
    {
        await HandleRagHealthCheckToolAsync(output, id, arguments, queryService, store, settings, jsonOptions);
        return;
    }

    if (string.Equals(toolName, RagServerInfoToolName, StringComparison.Ordinal))
    {
        await HandleRagServerInfoToolAsync(output, id, settings, jsonOptions);
        return;
    }

    if (string.Equals(toolName, RagIndexStatsToolName, StringComparison.Ordinal))
    {
        await HandleRagIndexStatsToolAsync(output, id, store, jsonOptions);
        return;
    }

    if (string.Equals(toolName, RagRecentSourcesToolName, StringComparison.Ordinal))
    {
        await HandleRagRecentSourcesToolAsync(output, id, arguments, store, jsonOptions);
        return;
    }

    await WriteErrorResponseAsync(output, id, -32602, "Unknown tool name.");
}

static async Task HandleQueryProjectRagToolAsync(
    Stream output,
    JsonNode? id,
    JsonObject? arguments,
    RagQueryService queryService,
    RagSettings settings,
    JsonSerializerOptions jsonOptions)
{
    var text = arguments?["text"]?.GetValue<string>();
    if (string.IsNullOrWhiteSpace(text))
    {
        await WriteErrorResponseAsync(output, id, -32602, "Missing required argument: text");
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

        var rawResults = await queryService.QueryAsync(text, queryLimit);
        var filteredResults = rawResults
            .Where(r => sourcePathContains is null || r.SourcePath.Contains(sourcePathContains, StringComparison.OrdinalIgnoreCase))
            .Where(r => minScore is null || r.Score >= minScore.Value)
            .Take(limit)
            .ToList();

        var textResult = FormatResults(filteredResults);
        var structuredResult = BuildQueryStructuredResult(text, limit, sourcePathContains, minScore, filteredResults);

        await WriteResponseAsync(output, id, BuildToolResult(textResult, structuredResult), jsonOptions);
    }
    catch (Exception ex)
    {
        await WriteResponseAsync(output, id, BuildToolResult($"RAG query failed: {ex.Message}", isError: true), jsonOptions);
    }
}

static async Task HandleRagHealthCheckToolAsync(
    Stream output,
    JsonNode? id,
    JsonObject? arguments,
    RagQueryService queryService,
    PostgresRagStore store,
    RagSettings settings,
    JsonSerializerOptions jsonOptions)
{
    var probeQuery = arguments?["smokeQuery"]?.GetValue<string>();
    if (string.IsNullOrWhiteSpace(probeQuery))
    {
        probeQuery = "nebula rag health probe";
    }

    try
    {
        await store.InitializeSchemaAsync(settings.Ingestion.VectorDimensions);
        var results = await queryService.QueryAsync(probeQuery, 1);
        var structuredResult = new JsonObject
        {
            ["status"] = "healthy",
            ["probeQuery"] = probeQuery,
            ["vectorDimensions"] = settings.Ingestion.VectorDimensions,
            ["defaultTopK"] = settings.Retrieval.DefaultTopK,
            ["retrievalProbeCount"] = results.Count
        };

        await WriteResponseAsync(output, id, BuildToolResult("Nebula RAG MCP health check passed.", structuredResult), jsonOptions);
    }
    catch (Exception ex)
    {
        await WriteResponseAsync(output, id, BuildToolResult($"Nebula RAG MCP health check failed: {ex.Message}", isError: true), jsonOptions);
    }
}

static async Task HandleRagServerInfoToolAsync(Stream output, JsonNode? id, RagSettings settings, JsonSerializerOptions jsonOptions)
{
    var toolCount = BuildToolsList()["tools"]?.AsArray()?.Count ?? 0;
    var structuredResult = new JsonObject
    {
        ["serverName"] = "Nebula RAG MCP",
        ["serverVersion"] = "1.0.0",
        ["databaseHost"] = settings.Database.Host,
        ["databasePort"] = settings.Database.Port,
        ["databaseName"] = settings.Database.Database,
        ["vectorDimensions"] = settings.Ingestion.VectorDimensions,
        ["defaultTopK"] = settings.Retrieval.DefaultTopK,
        ["toolCount"] = toolCount
    };

    await WriteResponseAsync(output, id, BuildToolResult("Nebula RAG MCP server information.", structuredResult), jsonOptions);
}

static async Task HandleRagIndexStatsToolAsync(Stream output, JsonNode? id, PostgresRagStore store, JsonSerializerOptions jsonOptions)
{
    try
    {
        var stats = await store.GetIndexStatsAsync();
        var structuredResult = new JsonObject
        {
            ["documentCount"] = stats.DocumentCount,
            ["chunkCount"] = stats.ChunkCount,
            ["latestIndexedAtUtc"] = stats.LatestIndexedAtUtc?.ToUniversalTime().ToString("O")
        };

        await WriteResponseAsync(output, id, BuildToolResult("Nebula RAG index statistics retrieved.", structuredResult), jsonOptions);
    }
    catch (Exception ex)
    {
        await WriteResponseAsync(output, id, BuildToolResult($"Failed to retrieve index statistics: {ex.Message}", isError: true), jsonOptions);
    }
}

static async Task HandleRagRecentSourcesToolAsync(
    Stream output,
    JsonNode? id,
    JsonObject? arguments,
    PostgresRagStore store,
    JsonSerializerOptions jsonOptions)
{
    var limit = 10;
    var limitNode = arguments?["limit"];
    if (limitNode is not null && int.TryParse(limitNode.ToString(), out var parsedLimit) && parsedLimit > 0)
    {
        limit = Math.Min(parsedLimit, 50);
    }

    try
    {
        var recentDocuments = await store.GetRecentDocumentsAsync(limit);
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

        await WriteResponseAsync(output, id, BuildToolResult("Nebula RAG recent sources retrieved.", structuredResult), jsonOptions);
    }
    catch (Exception ex)
    {
        await WriteResponseAsync(output, id, BuildToolResult($"Failed to retrieve recent sources: {ex.Message}", isError: true), jsonOptions);
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

static async Task<string?> ReadMessageAsync(Stream input)
{
    var singleByte = new byte[1];

    while (true)
    {
        var headerBuilder = new StringBuilder();

        while (true)
        {
            var read = await input.ReadAsync(singleByte);
            if (read == 0)
            {
                return null;
            }

            headerBuilder.Append((char)singleByte[0]);
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
            continue;
        }

        var body = new byte[contentLength];
        var offset = 0;
        while (offset < contentLength)
        {
            var read = await input.ReadAsync(body.AsMemory(offset, contentLength - offset));
            if (read == 0)
            {
                return null;
            }

            offset += read;
        }

        return Encoding.UTF8.GetString(body);
    }
}

static async Task WriteResponseAsync(Stream output, JsonNode? id, JsonObject result, JsonSerializerOptions jsonOptions)
{
    var response = new JsonObject
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["result"] = result
    };

    await WriteMessageAsync(output, response.ToJsonString(jsonOptions));
}

static async Task WriteErrorResponseAsync(Stream output, JsonNode? id, int code, string message)
{
    var response = new JsonObject
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["error"] = new JsonObject
        {
            ["code"] = code,
            ["message"] = message
        }
    };

    await WriteMessageAsync(output, response.ToJsonString());
}

static async Task WriteMessageAsync(Stream output, string json)
{
    var payloadBytes = Encoding.UTF8.GetBytes(json);
    var headerBytes = Encoding.ASCII.GetBytes($"Content-Length: {payloadBytes.Length}\r\n\r\n");
    await output.WriteAsync(headerBytes);
    await output.WriteAsync(payloadBytes);
    await output.FlushAsync();
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

file sealed record SelfTestCase(string Name, string Description, Func<CancellationToken, Task> Run);
