using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NebulaRAG.Core.Chunking;
using NebulaRAG.Core.Configuration;
using NebulaRAG.Core.Embeddings;
using NebulaRAG.Core.Mcp;
using NebulaRAG.Core.Services;
using NebulaRAG.Core.Storage;
using Serilog;
using Serilog.Formatting.Compact;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    // Stdio MCP servers must keep stdout clean for protocol frames.
    .WriteTo.Console(new CompactJsonFormatter(), standardErrorFromLevel: Serilog.Events.LogEventLevel.Verbose)
    .CreateLogger();

var configPath = GetConfigPath(args);
var settings = LoadSettings(configPath);
settings.Validate();

var store = new PostgresRagStore(settings.Database.BuildConnectionString());
var embeddingGenerator = new HashEmbeddingGenerator();
var chunker = new TextChunker();

var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddSerilog();
    builder.SetMinimumLevel(LogLevel.Information);
});

var queryService = new RagQueryService(store, embeddingGenerator, settings, loggerFactory.CreateLogger<RagQueryService>());
var managementService = new RagManagementService(store, embeddingGenerator, settings, loggerFactory.CreateLogger<RagManagementService>());
var sourcesManifestService = new RagSourcesManifestService(store, settings, loggerFactory.CreateLogger<RagSourcesManifestService>());
var indexer = new RagIndexer(store, chunker, embeddingGenerator, settings, loggerFactory.CreateLogger<RagIndexer>());
var handler = new McpTransportHandler(
    queryService,
    managementService,
    sourcesManifestService,
    store,
    chunker,
    embeddingGenerator,
    indexer,
    settings,
    new HttpClient(),
    loggerFactory.CreateLogger<McpTransportHandler>());

await RunServerAsync(handler);

/// <summary>
/// Runs the stdio MCP server loop and delegates JSON-RPC handling to the shared transport handler.
/// </summary>
/// <param name="handler">Shared MCP transport handler.</param>
static async Task RunServerAsync(McpTransportHandler handler)
{
    using var input = Console.OpenStandardInput();
    using var output = Console.OpenStandardOutput();
    var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    while (true)
    {
        var (payload, framing) = await ReadMessageAsync(input);
        if (payload is null)
        {
            return;
        }

        JsonObject request;
        try
        {
            request = JsonNode.Parse(payload)?.AsObject() ?? throw new InvalidOperationException("Invalid JSON request.");
        }
        catch
        {
            await WriteErrorEnvelopeAsync(output, null, -32700, "Parse error", framing);
            continue;
        }

        JsonObject response;
        try
        {
            response = await handler.HandleAsync(request, CancellationToken.None);
        }
        catch (Exception ex)
        {
            response = BuildErrorEnvelope(request["id"]?.DeepClone(), -32603, $"Internal error: {ex.Message}");
        }

        await WriteMessageAsync(output, response.ToJsonString(jsonOptions), framing);
    }
}

/// <summary>
/// Reads one MCP message from stdin in either content-length or newline-delimited framing.
/// </summary>
/// <param name="input">Input stream.</param>
/// <returns>Payload and detected framing mode.</returns>
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
        if (headerText.EndsWith("\r\n\r\n", StringComparison.Ordinal) || headerText.EndsWith("\n\n", StringComparison.Ordinal))
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

/// <summary>
/// Writes one MCP message using the selected framing mode.
/// </summary>
/// <param name="output">Output stream.</param>
/// <param name="json">JSON payload.</param>
/// <param name="framing">Detected framing mode.</param>
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

/// <summary>
/// Writes a JSON-RPC error envelope for parse/transport failures.
/// </summary>
/// <param name="output">Output stream.</param>
/// <param name="id">Request id.</param>
/// <param name="code">JSON-RPC error code.</param>
/// <param name="message">JSON-RPC error message.</param>
/// <param name="framing">Detected framing mode.</param>
static async Task WriteErrorEnvelopeAsync(Stream output, JsonNode? id, int code, string message, McpMessageFraming framing)
{
    var payload = BuildErrorEnvelope(id, code, message).ToJsonString();
    await WriteMessageAsync(output, payload, framing);
}

/// <summary>
/// Builds a JSON-RPC error envelope node.
/// </summary>
/// <param name="id">Request id.</param>
/// <param name="code">JSON-RPC error code.</param>
/// <param name="message">JSON-RPC error message.</param>
/// <returns>Error envelope object.</returns>
static JsonObject BuildErrorEnvelope(JsonNode? id, int code, string message)
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
/// Resolves explicit config path argument if provided.
/// </summary>
/// <param name="args">Raw process arguments.</param>
/// <returns>Config path or null.</returns>
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

/// <summary>
/// Loads runtime settings for the MCP stdio host.
/// </summary>
/// <param name="configPath">Optional explicit config path.</param>
/// <returns>Configured settings instance.</returns>
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

/// <summary>
/// Resolves a config file from common runtime locations.
/// </summary>
/// <param name="fileName">Config file name.</param>
/// <returns>First matching path or null.</returns>
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

file enum McpMessageFraming
{
    Unknown = 0,
    ContentLength = 1,
    NewlineDelimitedJsonRpc = 2
}
