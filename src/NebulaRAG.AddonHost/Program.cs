using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http.Json;
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

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();
ConfigureOpenTelemetry(builder.Services);
builder.Services.AddControllers();
builder.Services.AddSingleton<HttpClient>();

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
var chunker = new TextChunker();
var embeddingGenerator = new HashEmbeddingGenerator();
var queryService = new RagQueryService(store, embeddingGenerator, settings, loggerFactory.CreateLogger<RagQueryService>());
var managementService = new RagManagementService(store, loggerFactory.CreateLogger<RagManagementService>());
var sourcesManifestService = new RagSourcesManifestService(store, settings, loggerFactory.CreateLogger<RagSourcesManifestService>());
var indexer = new RagIndexer(store, chunker, embeddingGenerator, settings, loggerFactory.CreateLogger<RagIndexer>());

builder.Services.AddSingleton(settings);
builder.Services.AddSingleton(queryService);
builder.Services.AddSingleton(managementService);
builder.Services.AddSingleton(sourcesManifestService);
builder.Services.AddSingleton(store);
builder.Services.AddSingleton(chunker);
builder.Services.AddSingleton<IEmbeddingGenerator>(embeddingGenerator);
builder.Services.AddSingleton(indexer);
builder.Services.AddSingleton<DashboardSnapshotService>();
builder.Services.AddSingleton<IRuntimeTelemetrySink>(serviceProvider => serviceProvider.GetRequiredService<DashboardSnapshotService>());
builder.Services.AddSingleton<McpTransportHandler>();

await store.InitializeSchemaAsync(settings.Ingestion.VectorDimensions);

var app = builder.Build();
Log.Information("NebulaRAG add-on ignition sequence started.");

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

app.UseDefaultFiles();
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
app.MapControllers();

app.MapPost("/mcp", async (JsonObject request, McpTransportHandler handler, CancellationToken cancellationToken) =>
{
    var response = await handler.HandleAsync(request, cancellationToken);
    return Results.Json(response);
});

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
