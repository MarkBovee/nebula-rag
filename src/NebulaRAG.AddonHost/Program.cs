using System.Text.Json.Nodes;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using NebulaRAG.Core.Mcp;
using NebulaRAG.AddonHost.Services;
using NebulaRAG.Core.Chunking;
using NebulaRAG.Core.Configuration;
using NebulaRAG.Core.Embeddings;
using NebulaRAG.Core.Services;
using NebulaRAG.Core.Storage;
using Serilog;
using Serilog.Events;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

var addonDotEnvResult = DotEnvLoader.LoadStandardDotEnv();
if (addonDotEnvResult.FoundFile)
{
    Log.Information("Loaded .env from {Path}. Applied {LoadedCount} keys ({SkippedCount} skipped due to existing values).", addonDotEnvResult.Path, addonDotEnvResult.LoadedCount, addonDotEnvResult.SkippedCount);
}

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseStaticWebAssets();
builder.Host.UseSerilog();
ConfigureOpenTelemetry(builder.Services);
builder.Services.AddControllers();
builder.Services.AddHttpContextAccessor();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddSingleton<HttpClient>();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.Configure<JsonOptions>(options =>
{
    options.SerializerOptions.WriteIndented = true;
});

var settings = LoadSettings();
settings.Validate();
var pathBase = NormalizePathBase(Environment.GetEnvironmentVariable("NEBULARAG_PathBase"));

var loggerFactory = LoggerFactory.Create(loggingBuilder =>
{
    loggingBuilder.AddSerilog();
    loggingBuilder.SetMinimumLevel(LogLevel.Information);
});

var store = new PostgresRagStore(settings.Database.BuildConnectionString());
var planStore = new PostgresPlanStore(settings.Database.BuildConnectionString());
var projectStore = new PostgresProjectStore(settings.Database.BuildConnectionString());
var chunker = new TextChunker();
var embeddingGenerator = new HashEmbeddingGenerator();
var queryService = new RagQueryService(store, embeddingGenerator, settings, loggerFactory.CreateLogger<RagQueryService>());
var managementService = new RagManagementService(store, chunker, embeddingGenerator, settings, loggerFactory.CreateLogger<RagManagementService>());
var sourcesManifestService = new RagSourcesManifestService(store, settings, loggerFactory.CreateLogger<RagSourcesManifestService>());
var indexer = new RagIndexer(store, chunker, embeddingGenerator, settings, loggerFactory.CreateLogger<RagIndexer>());

builder.Services.AddSingleton(settings);
builder.Services.AddSingleton(queryService);
builder.Services.AddSingleton(managementService);
builder.Services.AddSingleton(sourcesManifestService);
builder.Services.AddSingleton(store);
builder.Services.AddSingleton(planStore);
builder.Services.AddSingleton(projectStore);
builder.Services.AddSingleton(chunker);
builder.Services.AddSingleton<IEmbeddingGenerator>(embeddingGenerator);
builder.Services.AddSingleton(indexer);
builder.Services.AddSingleton<DashboardSnapshotService>();
builder.Services.AddSingleton<RagOperationsService>();
builder.Services.AddSingleton<MemoryScopeResolver>();
builder.Services.AddSingleton<IRuntimeTelemetrySink>(serviceProvider => serviceProvider.GetRequiredService<DashboardSnapshotService>());
builder.Services.AddSingleton<McpTransportHandler>();

await store.InitializeSchemaAsync(settings.Ingestion.VectorDimensions);
await planStore.InitializeSchemaAsync();

var app = builder.Build();
Microsoft.Extensions.Logging.ILogger appLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("NebulaRAG.AddonHost.McpTransport");
Log.Information("NebulaRAG add-on ignition sequence started.");

app.UseForwardedHeaders();
app.Use((httpContext, next) =>
{
    ApplyExternalPathBase(httpContext, pathBase);
    return next();
});

if (!string.IsNullOrEmpty(pathBase))
{
    Log.Information("Navigation corridor locked to path base {PathBase}", pathBase);
    app.UsePathBase(pathBase);
}

app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate = "Orbit {RequestMethod} {RequestPath} => {StatusCode} in {Elapsed:0.0000} ms";
    options.GetLevel = (httpContext, elapsed, exception) =>
        exception is not null || httpContext.Response.StatusCode >= 500
            ? LogEventLevel.Error
            : LogEventLevel.Information;
});

app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        if (!IsHtmlEntryPoint(context))
        {
            return;
        }

        // Ensure dashboard shell pages always resolve the latest hashed asset manifest.
        context.Context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
        context.Context.Response.Headers["Pragma"] = "no-cache";
        context.Context.Response.Headers["Expires"] = "0";
    }
});

app.Use(async (httpContext, next) =>
{
    if (!IsMcpTransportRequest(httpContext.Request.Path, pathBase))
    {
        await next();
        return;
    }

    var mcpTransportHandler = httpContext.RequestServices.GetRequiredService<McpTransportHandler>();
    var mcpResponse = await HandleMcpTransportRequestAsync(httpContext.Request, mcpTransportHandler, appLogger, httpContext.RequestAborted);
    await mcpResponse.ExecuteAsync(httpContext);
});

app.UseAntiforgery();
app.MapStaticAssets();
app.MapControllers();
app.MapRazorComponents<NebulaRAG.AddonHost.Components.App>().AddInteractiveServerRenderMode();

Log.Information(
    "NebulaRAG flight deck online. Dashboard: {DashboardPath} | MCP: {McpPath}",
    PrefixPath(pathBase, "/dashboard/"),
    PrefixPath(pathBase, "/mcp"));

app.Run();

/// <summary>
/// Prefixes an application route with the configured path base.
/// </summary>
/// <param name="pathBase">Configured ASP.NET path base.</param>
/// <param name="route">Route path starting with '/'.</param>
/// <returns>Combined route including path base when configured.</returns>
static string PrefixPath(string pathBase, string route)
{
    if (string.IsNullOrWhiteSpace(pathBase))
    {
        return route;
    }

    return $"{pathBase}{route}";
}

/// <summary>
/// Normalizes a path base value from environment configuration.
/// Ensures the value starts with '/' and does not end with '/'.
/// Returns empty string when no path base is configured.
/// </summary>
/// <param name="rawPathBase">Raw path base value.</param>
/// <returns>Normalized path base suitable for ASP.NET Core UsePathBase.</returns>
static string NormalizePathBase(string? rawPathBase)
{
    if (string.IsNullOrWhiteSpace(rawPathBase))
    {
        return string.Empty;
    }

    var trimmed = rawPathBase.Trim();
    if (trimmed == "/")
    {
        return string.Empty;
    }

    if (!trimmed.StartsWith("/", StringComparison.Ordinal))
    {
        trimmed = $"/{trimmed}";
    }

    return trimmed.TrimEnd('/');
}

/// <summary>
/// Applies the externally visible path base from Home Assistant ingress or proxy headers.
/// </summary>
/// <param name="httpContext">Current HTTP context.</param>
/// <param name="configuredPathBase">Configured application path base fallback.</param>
static void ApplyExternalPathBase(HttpContext httpContext, string configuredPathBase)
{
    var externalPathBase = ResolveExternalPathBase(httpContext.Request, configuredPathBase);
    if (string.IsNullOrWhiteSpace(externalPathBase))
    {
        return;
    }

    httpContext.Request.PathBase = new PathString(externalPathBase);
}

/// <summary>
/// Resolves the externally visible base path for URL generation under ingress and reverse proxies.
/// </summary>
/// <param name="request">Current HTTP request.</param>
/// <param name="configuredPathBase">Configured application path base fallback.</param>
/// <returns>Normalized external path base or an empty string when none applies.</returns>
static string ResolveExternalPathBase(HttpRequest request, string configuredPathBase)
{
    var ingressPath = NormalizePathBase(request.Headers["X-Ingress-Path"].ToString());
    if (!string.IsNullOrWhiteSpace(ingressPath))
    {
        return ingressPath;
    }

    var forwardedPrefix = NormalizePathBase(request.Headers["X-Forwarded-Prefix"].ToString());
    if (!string.IsNullOrWhiteSpace(forwardedPrefix))
    {
        return forwardedPrefix;
    }

    var requestPathBase = NormalizePathBase(request.PathBase.Value);
    if (!string.IsNullOrWhiteSpace(requestPathBase))
    {
        return requestPathBase;
    }

    return configuredPathBase;
}

/// <summary>
/// Determines whether the static file response is for an HTML entry page.
/// </summary>
/// <param name="context">Static file response context.</param>
/// <returns>True when the requested file is an index HTML page.</returns>
static bool IsHtmlEntryPoint(StaticFileResponseContext context)
{
    var fileName = Path.GetFileName(context.File.Name);
    return string.Equals(fileName, "index.html", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Loads runtime settings from container mount path and environment variables.
/// </summary>
/// <returns>Configured RagSettings instance with database and retrieval defaults.</returns>
static RagSettings LoadSettings()
{
    var configBuilder = new ConfigurationBuilder();
    configBuilder.AddJsonFile("/app/ragsettings.json", optional: true, reloadOnChange: false);
    configBuilder.AddEnvironmentVariables(prefix: "NEBULARAG_");
    return configBuilder.Build().Get<RagSettings>() ?? new RagSettings();
}

/// <summary>
/// Configures OpenTelemetry tracing and metrics with optional OTLP export.
/// </summary>
/// <param name="services">Service collection used by the web host.</param>
static void ConfigureOpenTelemetry(IServiceCollection services)
{
    var otlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
    var hasOtlpEndpoint = !string.IsNullOrWhiteSpace(otlpEndpoint);

    services.AddOpenTelemetry()
        .ConfigureResource(resourceBuilder =>
        {
            resourceBuilder.AddService(serviceName: "NebulaRAG.AddonHost", serviceVersion: "0.2.22");
        })
        .WithTracing(traceBuilder =>
        {
            traceBuilder
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation();

            if (hasOtlpEndpoint)
            {
                traceBuilder.AddOtlpExporter();
            }
        })
        .WithMetrics(metricBuilder =>
        {
            metricBuilder
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation();

            if (hasOtlpEndpoint)
            {
                metricBuilder.AddOtlpExporter();
            }
        });
}

/// <summary>
/// Handles MCP transport requests with explicit GET/POST behavior for streamable HTTP compatibility.
/// </summary>
/// <param name="request">Incoming HTTP request.</param>
/// <param name="handler">MCP transport handler.</param>
/// <param name="logger">Application logger for diagnostics.</param>
/// <param name="cancellationToken">Cancellation token.</param>
/// <returns>HTTP result containing JSON-RPC response payload and status.</returns>
static async Task<IResult> HandleMcpTransportRequestAsync(HttpRequest request, McpTransportHandler handler, Microsoft.Extensions.Logging.ILogger logger, CancellationToken cancellationToken)
{
    if (HttpMethods.IsGet(request.Method))
    {
        // Streamable HTTP spec allows either SSE stream or 405 when SSE is not offered.
        return Results.Json(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = null,
            ["error"] = new JsonObject
            {
                ["code"] = -32600,
                ["message"] = "Use HTTP POST for MCP JSON-RPC requests."
            }
        }, statusCode: StatusCodes.Status405MethodNotAllowed);
    }

    if (!HttpMethods.IsPost(request.Method))
    {
        return Results.Json(BuildJsonRpcError(null, -32600, $"Unsupported MCP HTTP method: {request.Method}"), statusCode: StatusCodes.Status405MethodNotAllowed);
    }

    var requestBody = await ReadRequestBodyAsync(request, cancellationToken);
    if (string.IsNullOrWhiteSpace(requestBody))
    {
        var sanitizedContentType = SanitizeLogValue(request.ContentType, 128);
        var sanitizedUserAgent = SanitizeLogValue(request.Headers.UserAgent.ToString(), 256);
        logger.LogWarning("Received empty MCP POST payload. ContentType={ContentType}; UserAgent={UserAgent}", sanitizedContentType, sanitizedUserAgent);
        return Results.Json(BuildJsonRpcError(null, -32600, "Empty MCP request payload."));
    }

    var parseOutcome = TryParseMcpPayload(requestBody);
    if (!parseOutcome.IsSuccess)
    {
        var sanitizedContentType = SanitizeLogValue(request.ContentType, 128);
        var sanitizedUserAgent = SanitizeLogValue(request.Headers.UserAgent.ToString(), 256);
        var sanitizedBodyPreview = SanitizeLogValue(requestBody.Length > 200 ? requestBody[..200] : requestBody, 200);
        logger.LogWarning(
            "Failed to parse MCP POST payload. ContentType={ContentType}; UserAgent={UserAgent}; BodyPreview={BodyPreview}",
            sanitizedContentType,
            sanitizedUserAgent,
            sanitizedBodyPreview);

        return Results.Json(BuildJsonRpcError(null, -32700, "Invalid JSON payload."));
    }

    if (parseOutcome.SingleRequest is not null)
    {
        var singleResponse = await handler.HandleAsync(parseOutcome.SingleRequest, cancellationToken);
        return Results.Json(singleResponse);
    }

    if (parseOutcome.BatchRequests.Count > 0)
    {
        var batchResponses = new JsonArray();
        foreach (var batchRequest in parseOutcome.BatchRequests)
        {
            var batchResponse = await handler.HandleAsync(batchRequest, cancellationToken);
            batchResponses.Add(batchResponse);
        }

        return Results.Json(batchResponses);
    }

    return Results.Json(BuildJsonRpcError(null, -32600, "MCP request must be a JSON object or array."));
}

/// <summary>
/// Determines whether an incoming request path targets the MCP endpoint.
/// </summary>
/// <param name="requestPath">Current request path.</param>
/// <param name="configuredPathBase">Configured application path base.</param>
/// <returns>True when the request should be handled by MCP transport logic.</returns>
static bool IsMcpTransportRequest(PathString requestPath, string configuredPathBase)
{
    var pathValue = requestPath.Value ?? string.Empty;
    if (string.Equals(pathValue, "/mcp", StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    if (string.IsNullOrWhiteSpace(configuredPathBase))
    {
        return false;
    }

    return string.Equals(pathValue, $"{configuredPathBase}/mcp", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Reads the full MCP request body as UTF-8 text.
/// </summary>
/// <param name="request">Incoming HTTP request.</param>
/// <param name="cancellationToken">Cancellation token.</param>
/// <returns>Raw request body text.</returns>
static async Task<string> ReadRequestBodyAsync(HttpRequest request, CancellationToken cancellationToken)
{
    using var reader = new StreamReader(request.Body);
    return await reader.ReadToEndAsync(cancellationToken);
}

/// <summary>
/// Sanitizes untrusted request values before writing them to structured logs.
/// Replaces control characters to prevent log-forging and truncates long inputs.
/// </summary>
/// <param name="value">Untrusted value to sanitize.</param>
/// <param name="maxLength">Maximum retained character count.</param>
/// <returns>Sanitized log-safe value.</returns>
static string SanitizeLogValue(string? value, int maxLength)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return string.Empty;
    }

    var normalizedValue = value.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();
    if (normalizedValue.Length <= maxLength)
    {
        return normalizedValue;
    }

    return normalizedValue[..maxLength];
}

/// <summary>
/// Attempts to parse a raw MCP payload as a single JSON-RPC object or batch array.
/// </summary>
/// <param name="requestBody">Raw HTTP body content.</param>
/// <returns>Parse outcome with either a single request, batch requests, or failure details.</returns>
static McpPayloadParseOutcome TryParseMcpPayload(string requestBody)
{
    try
    {
        var payload = JsonNode.Parse(requestBody);
        if (payload is JsonObject singleRequest)
        {
            return McpPayloadParseOutcome.SuccessSingle(singleRequest);
        }

        if (payload is JsonArray batchRequests)
        {
            var normalizedBatchRequests = new List<JsonObject>();
            foreach (var item in batchRequests)
            {
                if (item is JsonObject batchRequest)
                {
                    normalizedBatchRequests.Add(batchRequest);
                }
            }

            return McpPayloadParseOutcome.SuccessBatch(normalizedBatchRequests);
        }

        return McpPayloadParseOutcome.Failure();
    }
    catch (JsonException)
    {
        return McpPayloadParseOutcome.Failure();
    }
}

/// <summary>
/// Builds a JSON-RPC error response envelope.
/// </summary>
/// <param name="id">Request id when available.</param>
/// <param name="code">JSON-RPC error code.</param>
/// <param name="message">Human-readable error message.</param>
/// <returns>JSON-RPC error object.</returns>
static JsonObject BuildJsonRpcError(JsonNode? id, int code, string message)
{
    return new JsonObject
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id?.DeepClone(),
        ["error"] = new JsonObject
        {
            ["code"] = code,
            ["message"] = message
        }
    };
}

/// <summary>
/// Represents MCP request payload parse results for single and batch requests.
/// </summary>
/// <param name="isSuccess">Whether payload parsing succeeded.</param>
/// <param name="singleRequest">Parsed single request object when present.</param>
/// <param name="batchRequests">Parsed batch requests when present.</param>
readonly record struct McpPayloadParseOutcome(bool IsSuccess, JsonObject? SingleRequest, IReadOnlyList<JsonObject> BatchRequests)
{
    /// <summary>
    /// Creates a successful parse outcome for one request object.
    /// </summary>
    /// <param name="request">Parsed request object.</param>
    /// <returns>Single-request parse outcome.</returns>
    public static McpPayloadParseOutcome SuccessSingle(JsonObject request)
    {
        return new McpPayloadParseOutcome(true, request, Array.Empty<JsonObject>());
    }

    /// <summary>
    /// Creates a successful parse outcome for a request batch.
    /// </summary>
    /// <param name="requests">Parsed batch request objects.</param>
    /// <returns>Batch parse outcome.</returns>
    public static McpPayloadParseOutcome SuccessBatch(IReadOnlyList<JsonObject> requests)
    {
        return new McpPayloadParseOutcome(true, null, requests);
    }

    /// <summary>
    /// Creates a failed parse outcome.
    /// </summary>
    /// <returns>Failed parse outcome.</returns>
    public static McpPayloadParseOutcome Failure()
    {
        return new McpPayloadParseOutcome(false, null, Array.Empty<JsonObject>());
    }
}
