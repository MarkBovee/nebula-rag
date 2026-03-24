using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using NebulaRAG.Core.Models;

namespace NebulaRAG.Core.Services;

/// <summary>
/// Installs, uninstalls, and reports the Nebula MCP Stop hook in supported AI client settings files.
/// </summary>
public sealed class HookInstallService
{
    private const string HookCommand = "nebula-rag memory sync";
    private const string HookMarker = "nebula-rag";

    private readonly ILogger<HookInstallService> _logger;

    public HookInstallService(ILogger<HookInstallService>? logger = null)
    {
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<HookInstallService>.Instance;
    }

    /// <summary>
    /// Installs the Nebula Stop hook into the target client's settings file.
    /// </summary>
    /// <param name="client">Target client identifier: <c>claude</c> or <c>copilot</c>.</param>
    /// <param name="dryRun">If <c>true</c>, returns the diff without writing the file.</param>
    /// <param name="settingsPathOverride">Override settings file path (used in tests).</param>
    public async Task<HookOperationResult> InstallHooksAsync(
        string client, bool dryRun = false, string? settingsPathOverride = null)
    {
        var path = settingsPathOverride ?? ResolveSettingsPath(client);
        if (path is null)
            return new HookOperationResult(false, client, null, $"Unsupported client: {client}");

        var json = File.Exists(path) ? await File.ReadAllTextAsync(path) : "{}";
        var root = JsonNode.Parse(json)?.AsObject() ?? new JsonObject();

        if (IsHookPresent(root))
            return new HookOperationResult(true, client, null, "Hook already installed (no change).");

        InjectHook(root);
        var output = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        var diff = $"+ Added Stop hook: {HookCommand}";

        if (dryRun)
            return new HookOperationResult(true, client, diff, "Dry run — no file written.");

        await AtomicWriteAsync(path, output);
        _logger.LogInformation("Installed Nebula Stop hook in {Path}", path);
        return new HookOperationResult(true, client, diff, $"Hook installed in {path}");
    }

    /// <summary>
    /// Removes the Nebula Stop hook from the target client's settings file.
    /// </summary>
    /// <param name="client">Target client identifier: <c>claude</c> or <c>copilot</c>.</param>
    /// <param name="dryRun">If <c>true</c>, returns the diff without writing the file.</param>
    /// <param name="settingsPathOverride">Override settings file path (used in tests).</param>
    public async Task<HookOperationResult> UninstallHooksAsync(
        string client, bool dryRun = false, string? settingsPathOverride = null)
    {
        var path = settingsPathOverride ?? ResolveSettingsPath(client);
        if (path is null)
            return new HookOperationResult(false, client, null, $"Unsupported client: {client}");

        if (!File.Exists(path))
            return new HookOperationResult(true, client, null, "Settings file not found — nothing to remove.");

        var json = await File.ReadAllTextAsync(path);
        var root = JsonNode.Parse(json)?.AsObject() ?? new JsonObject();

        if (!IsHookPresent(root))
            return new HookOperationResult(true, client, null, "Hook not present — nothing to remove.");

        RemoveHook(root);
        var output = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        var diff = $"- Removed Stop hook: {HookCommand}";

        if (dryRun)
            return new HookOperationResult(true, client, diff, "Dry run — no file written.");

        await AtomicWriteAsync(path, output);
        _logger.LogInformation("Removed Nebula Stop hook from {Path}", path);
        return new HookOperationResult(true, client, diff, $"Hook removed from {path}");
    }

    /// <summary>
    /// Returns status for all supported clients (always checks both claude and copilot).
    /// </summary>
    /// <param name="nebulaEndpoint">Optional health check URL override.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<HookStatusResult>> GetStatusAsync(
        string? nebulaEndpoint = null, CancellationToken cancellationToken = default)
    {
        var clients = new[] { "claude", "copilot" };
        var results = new List<HookStatusResult>();

        foreach (var client in clients)
        {
            var path = ResolveSettingsPath(client);
            var exists = path is not null && File.Exists(path);
            bool hookInstalled = false;

            if (exists)
            {
                var json = await File.ReadAllTextAsync(path!, cancellationToken);
                var root = JsonNode.Parse(json)?.AsObject();
                hookInstalled = root is not null && IsHookPresent(root);
            }

            (bool reachable, string? warning) = await CheckEndpointAsync(
                nebulaEndpoint ?? "http://localhost:5001/health", cancellationToken);

            results.Add(new HookStatusResult(client, exists, hookInstalled, reachable, warning));
        }

        return results;
    }

    // --- Internals ---

    private static bool IsHookPresent(JsonObject root)
    {
        var hooksNode = root["hooks"]?.AsObject();
        if (hooksNode is null) return false;
        var stopArr = hooksNode["Stop"]?.AsArray();
        if (stopArr is null) return false;
        return stopArr.Any(entry =>
            entry?["hooks"]?.AsArray()
                   ?.Any(h => h?["command"]?.GetValue<string>()?.Contains(HookMarker) == true) == true);
    }

    private static void InjectHook(JsonObject root)
    {
        var hooks = root["hooks"]?.AsObject() ?? new JsonObject();
        var stopArr = hooks["Stop"]?.AsArray() ?? new JsonArray();

        var hookEntry = new JsonObject
        {
            ["matcher"] = "",
            ["hooks"] = new JsonArray
            {
                new JsonObject { ["type"] = "command", ["command"] = HookCommand }
            }
        };
        stopArr.Add(hookEntry);
        hooks["Stop"] = stopArr;
        root["hooks"] = hooks;
    }

    private static void RemoveHook(JsonObject root)
    {
        var stopArr = root["hooks"]?["Stop"]?.AsArray();
        if (stopArr is null) return;
        var toRemove = stopArr
            .Where(entry =>
                entry?["hooks"]?.AsArray()
                    ?.Any(h => h?["command"]?.GetValue<string>()?.Contains(HookMarker) == true) == true)
            .ToList();
        foreach (var item in toRemove) stopArr.Remove(item);
    }

    private static async Task AtomicWriteAsync(string path, string content)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        var tmp = path + ".nebula.tmp";
        await File.WriteAllTextAsync(tmp, content);
        File.Move(tmp, path, overwrite: true);
    }

    private static async Task<(bool reachable, string? warning)> CheckEndpointAsync(
        string url, CancellationToken cancellationToken)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var resp = await http.GetAsync(url, cancellationToken);
            return (resp.IsSuccessStatusCode, null);
        }
        catch (Exception ex)
        {
            return (false, $"Endpoint unreachable: {ex.Message}");
        }
    }

    /// <summary>Resolves the settings file path for the given client identifier.</summary>
    public static string? ResolveSettingsPath(string client) => client.ToLowerInvariant() switch
    {
        "claude" => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude", "settings.json"),
        "copilot" => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "GitHub Copilot", "settings.json")
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config", "github-copilot", "settings.json"),
        _ => null
    };
}
