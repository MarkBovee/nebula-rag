using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NebulaRAG.Core.Chunking;
using NebulaRAG.Core.Configuration;
using NebulaRAG.Core.Embeddings;
using NebulaRAG.Core.Mcp;
using NebulaRAG.Core.Services;
using NebulaRAG.Core.Storage;

namespace NebulaRAG.Tests;

/// <summary>
/// Verifies MCP JSON-RPC transport contract behavior for baseline protocol methods.
/// </summary>
public sealed class McpTransportHandlerContractTests
{
    /// <summary>
    /// Ensures initialize returns protocol metadata required by MCP clients.
    /// </summary>
    [Fact]
    public async Task HandleAsync_Initialize_ReturnsProtocolAndServerInfo()
    {
        var transportHandler = CreateTransportHandler();
        var request = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 1,
            ["method"] = "initialize",
            ["params"] = new JsonObject
            {
                ["protocolVersion"] = "2025-11-25",
                ["capabilities"] = new JsonObject(),
                ["clientInfo"] = new JsonObject
                {
                    ["name"] = "transport-test",
                    ["version"] = "1.0.0"
                }
            }
        };

        var response = await transportHandler.HandleAsync(request, CancellationToken.None);

        Assert.Equal("2.0", response["jsonrpc"]?.GetValue<string>());
        Assert.Equal("2025-11-25", response["result"]?["protocolVersion"]?.GetValue<string>());
        Assert.Equal("Nebula RAG", response["result"]?["serverInfo"]?["name"]?.GetValue<string>());
        Assert.Equal("0.2.0", response["result"]?["serverInfo"]?["version"]?.GetValue<string>());
        Assert.False(response["result"]?["capabilities"]?["tools"]?["listChanged"]?.GetValue<bool>());
    }

    /// <summary>
    /// Ensures ping returns an empty result object for liveness checks.
    /// </summary>
    [Fact]
    public async Task HandleAsync_Ping_ReturnsEmptyResultObject()
    {
        var transportHandler = CreateTransportHandler();
        var request = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 9,
            ["method"] = "ping"
        };

        var response = await transportHandler.HandleAsync(request, CancellationToken.None);

        Assert.Equal("2.0", response["jsonrpc"]?.GetValue<string>());
        Assert.NotNull(response["result"]);
        Assert.Empty(response["result"]?.AsObject() ?? []);
    }

    /// <summary>
    /// Ensures unknown methods return JSON-RPC method not found errors.
    /// </summary>
    [Fact]
    public async Task HandleAsync_UnknownMethod_ReturnsMethodNotFound()
    {
        var transportHandler = CreateTransportHandler();
        var request = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 3,
            ["method"] = "unknown/method"
        };

        var response = await transportHandler.HandleAsync(request, CancellationToken.None);

        Assert.Equal(-32601, response["error"]?["code"]?.GetValue<int>());
        Assert.Contains("Method not found", response["error"]?["message"]?.GetValue<string>());
    }

    /// <summary>
    /// Ensures missing method fields return JSON-RPC invalid request errors.
    /// </summary>
    [Fact]
    public async Task HandleAsync_MissingMethod_ReturnsInvalidRequest()
    {
        var transportHandler = CreateTransportHandler();
        var request = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 4
        };

        var response = await transportHandler.HandleAsync(request, CancellationToken.None);

        Assert.Equal(-32600, response["error"]?["code"]?.GetValue<int>());
        Assert.Equal("Invalid request", response["error"]?["message"]?.GetValue<string>());
    }

    /// <summary>
    /// Ensures notification methods are acknowledged with an empty result payload.
    /// </summary>
    [Fact]
    public async Task HandleAsync_NotificationMethod_ReturnsEmptyResult()
    {
        var transportHandler = CreateTransportHandler();
        var request = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "notifications/initialized"
        };

        var response = await transportHandler.HandleAsync(request, CancellationToken.None);

        Assert.NotNull(response["result"]);
        Assert.Empty(response["result"]?.AsObject() ?? []);
    }

    /// <summary>
    /// Ensures tools/call requests without a tool name return JSON-RPC invalid params errors.
    /// </summary>
    [Fact]
    public async Task HandleAsync_ToolsCallMissingName_ReturnsInvalidParams()
    {
        var transportHandler = CreateTransportHandler();
        var request = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 6,
            ["method"] = "tools/call",
            ["params"] = new JsonObject()
        };

        var response = await transportHandler.HandleAsync(request, CancellationToken.None);

        Assert.Equal(-32602, response["error"]?["code"]?.GetValue<int>());
        Assert.Equal("Missing tool name", response["error"]?["message"]?.GetValue<string>());
    }

    /// <summary>
    /// Ensures tools/list advertises baseline transport tool capabilities.
    /// </summary>
    [Fact]
    public async Task HandleAsync_ToolsList_ContainsCoreTools()
    {
        var transportHandler = CreateTransportHandler();
        var request = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 7,
            ["method"] = "tools/list"
        };

        var response = await transportHandler.HandleAsync(request, CancellationToken.None);
        var tools = response["result"]?["tools"]?.AsArray() ?? [];
        var toolNames = tools
            .Select(static tool => tool?["name"]?.GetValue<string>())
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("query_project_rag", toolNames);
        Assert.Contains("rag_health_check", toolNames);
        Assert.Contains("memory_recall", toolNames);
        Assert.Contains("create_plan", toolNames);
    }

    /// <summary>
    /// Creates a transport handler with local, non-networked dependencies for protocol contract tests.
    /// </summary>
    /// <returns>Configured transport handler.</returns>
    private static McpTransportHandler CreateTransportHandler()
    {
        var settings = new RagSettings
        {
            Database = new DatabaseSettings
            {
                Host = "localhost",
                Port = 5432,
                Database = "nebula",
                Username = "postgres",
                Password = "test-password",
                SslMode = "Prefer"
            }
        };

        var store = new PostgresRagStore(settings.Database.BuildConnectionString());
        var embeddingGenerator = new HashEmbeddingGenerator();
        var chunker = new TextChunker();
        var queryService = new RagQueryService(store, embeddingGenerator, settings, NullLogger<RagQueryService>.Instance);
        var managementService = new RagManagementService(store, embeddingGenerator, settings, NullLogger<RagManagementService>.Instance);
        var sourcesManifestService = new RagSourcesManifestService(store, settings, NullLogger<RagSourcesManifestService>.Instance);
        var indexer = new RagIndexer(store, chunker, embeddingGenerator, settings, NullLogger<RagIndexer>.Instance);

        return new McpTransportHandler(
            queryService,
            managementService,
            sourcesManifestService,
            store,
            chunker,
            embeddingGenerator,
            indexer,
            settings,
            new HttpClient(),
            NullLogger<McpTransportHandler>.Instance);
    }
}
