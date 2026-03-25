using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using NebulaRAG.Core.Models;

namespace NebulaRAG.Core.Services;

/// <summary>
/// Installs, uninstalls, and reports the Nebula hooks in supported AI client settings files.
/// For Claude Code this covers both the user-level Stop hook and the project-level balanced hooks
/// (SessionStart, PreToolUse, PostToolUseFailure, StopFailure).
/// </summary>
public sealed class HookInstallService
{
    private const string StopHookCommand = "nebula-rag memory sync";
    private const string HookMarker = "nebula-rag";
    private const string BalancedHookScript = "bash .github/nebula/hooks/Invoke-NebulaAgentHook.sh";

    private readonly ILogger<HookInstallService> _logger;

    /// <summary>
    /// Balanced project hook definitions for Claude Code (SessionStart, PreToolUse, PostToolUseFailure, StopFailure).
    /// Each entry is (eventName, matcher, --event arg, statusMessage).
    /// </summary>
    private static readonly (string Event, string Matcher, string HookEvent, string StatusMessage)[] BalancedHooks =
    [
        ("SessionStart",       "startup|resume|compact",    "SessionStart",       "Loading Nebula hook context"),
        ("PreToolUse",         "Bash",                      "PreToolUse",         "Checking Nebula shell guardrails"),
        ("PostToolUseFailure", @"mcp__nebula-rag__.*|Bash", "PostToolUseFailure", "Capturing Nebula failure details"),
        ("StopFailure",        ".*",                        "StopFailure",        "Capturing Claude failure details"),
    ];

    public HookInstallService(ILogger<HookInstallService>? logger = null)
    {
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<HookInstallService>.Instance;
    }

    /// <summary>
    /// Installs the Nebula hooks into the target client's settings file(s).
    /// For <c>claude</c>: writes the Stop hook to the user-level settings and the balanced hooks
    /// to the project-level <c>.claude/settings.json</c> in the current working directory.
    /// For <c>copilot</c>: writes the Stop hook to the Copilot user settings file.
    /// </summary>
    /// <param name="client">Target client identifier: <c>claude</c> or <c>copilot</c>.</param>
    /// <param name="dryRun">If <c>true</c>, returns the diff without writing any file.</param>
    /// <param name="settingsPathOverride">Override user-level settings file path (used in tests).</param>
    /// <param name="projectSettingsPathOverride">Override project-level settings file path (used in tests).</param>
    public async Task<HookOperationResult> InstallHooksAsync(
        string client, bool dryRun = false,
        string? settingsPathOverride = null,
        string? projectSettingsPathOverride = null)
    {
        var userPath = settingsPathOverride ?? ResolveSettingsPath(client);
        if (userPath is null)
            return new HookOperationResult(false, client, null, $"Unsupported client: {client}");

        var diffs = new List<string>();

        // --- User-level Stop hook ---
        var userJson = File.Exists(userPath) ? await File.ReadAllTextAsync(userPath) : "{}";
        var userRoot = JsonNode.Parse(userJson)?.AsObject() ?? new JsonObject();

        if (!IsStopHookPresent(userRoot))
        {
            InjectStopHook(userRoot);
            diffs.Add($"+ Added Stop hook in {userPath}: {StopHookCommand}");
            if (!dryRun)
            {
                await AtomicWriteAsync(userPath, userRoot.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                _logger.LogInformation("Installed Nebula Stop hook in {Path}", userPath);
            }
        }

        // --- Project-level balanced hooks (Claude Code only) ---
        if (client.Equals("claude", StringComparison.OrdinalIgnoreCase))
        {
            var projectPath = projectSettingsPathOverride ?? ResolveProjectSettingsPath();
            var projectJson = File.Exists(projectPath) ? await File.ReadAllTextAsync(projectPath) : "{}";
            var projectRoot = JsonNode.Parse(projectJson)?.AsObject() ?? new JsonObject();

            var injected = InjectBalancedHooks(projectRoot);
            if (injected.Count > 0)
            {
                foreach (var ev in injected)
                    diffs.Add($"+ Added {ev} hook in {projectPath}");
                if (!dryRun)
                {
                    await AtomicWriteAsync(projectPath, projectRoot.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                    _logger.LogInformation("Installed Nebula balanced hooks in {Path}", projectPath);
                }
            }
        }

        if (diffs.Count == 0)
            return new HookOperationResult(true, client, null, "All hooks already installed (no change).");

        var diffText = string.Join("\n", diffs);
        if (dryRun)
            return new HookOperationResult(true, client, diffText, "Dry run — no files written.");

        return new HookOperationResult(true, client, diffText, "Hooks installed.");
    }

    /// <summary>
    /// Removes all Nebula hooks from the target client's settings file(s).
    /// </summary>
    /// <param name="client">Target client identifier: <c>claude</c> or <c>copilot</c>.</param>
    /// <param name="dryRun">If <c>true</c>, returns the diff without writing any file.</param>
    /// <param name="settingsPathOverride">Override user-level settings file path (used in tests).</param>
    /// <param name="projectSettingsPathOverride">Override project-level settings file path (used in tests).</param>
    public async Task<HookOperationResult> UninstallHooksAsync(
        string client, bool dryRun = false,
        string? settingsPathOverride = null,
        string? projectSettingsPathOverride = null)
    {
        var userPath = settingsPathOverride ?? ResolveSettingsPath(client);
        if (userPath is null)
            return new HookOperationResult(false, client, null, $"Unsupported client: {client}");

        var diffs = new List<string>();

        // --- User-level Stop hook ---
        if (File.Exists(userPath))
        {
            var userJson = await File.ReadAllTextAsync(userPath);
            var userRoot = JsonNode.Parse(userJson)?.AsObject() ?? new JsonObject();
            if (IsStopHookPresent(userRoot))
            {
                RemoveStopHook(userRoot);
                diffs.Add($"- Removed Stop hook from {userPath}");
                if (!dryRun)
                {
                    await AtomicWriteAsync(userPath, userRoot.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                    _logger.LogInformation("Removed Nebula Stop hook from {Path}", userPath);
                }
            }
        }

        // --- Project-level balanced hooks (Claude Code only) ---
        if (client.Equals("claude", StringComparison.OrdinalIgnoreCase))
        {
            var projectPath = projectSettingsPathOverride ?? ResolveProjectSettingsPath();
            if (File.Exists(projectPath))
            {
                var projectJson = await File.ReadAllTextAsync(projectPath);
                var projectRoot = JsonNode.Parse(projectJson)?.AsObject() ?? new JsonObject();
                var removed = RemoveBalancedHooks(projectRoot);
                if (removed.Count > 0)
                {
                    foreach (var ev in removed)
                        diffs.Add($"- Removed {ev} hook from {projectPath}");
                    if (!dryRun)
                    {
                        await AtomicWriteAsync(projectPath, projectRoot.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                        _logger.LogInformation("Removed Nebula balanced hooks from {Path}", projectPath);
                    }
                }
            }
        }

        if (diffs.Count == 0)
            return new HookOperationResult(true, client, null, "No Nebula hooks found — nothing to remove.");

        var diffText = string.Join("\n", diffs);
        if (dryRun)
            return new HookOperationResult(true, client, diffText, "Dry run — no files written.");

        return new HookOperationResult(true, client, diffText, "Hooks removed.");
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
                hookInstalled = root is not null && IsStopHookPresent(root);
            }

            (bool reachable, string? warning) = await CheckEndpointAsync(
                nebulaEndpoint ?? "http://localhost:5001/health", cancellationToken);

            results.Add(new HookStatusResult(client, exists, hookInstalled, reachable, warning));
        }

        return results;
    }

    // --- Internals ---

    private static bool IsStopHookPresent(JsonObject root)
    {
        var hooksNode = root["hooks"]?.AsObject();
        if (hooksNode is null) return false;
        var stopArr = hooksNode["Stop"]?.AsArray();
        if (stopArr is null) return false;
        return stopArr.Any(entry =>
            entry?["hooks"]?.AsArray()
                   ?.Any(h => h?["command"]?.GetValue<string>()?.Contains(HookMarker) == true) == true);
    }

    private static void InjectStopHook(JsonObject root)
    {
        var hooks = root["hooks"]?.AsObject() ?? new JsonObject();
        var stopArr = hooks["Stop"]?.AsArray() ?? new JsonArray();
        stopArr.Add(new JsonObject
        {
            ["matcher"] = "",
            ["hooks"] = new JsonArray
            {
                new JsonObject { ["type"] = "command", ["command"] = StopHookCommand }
            }
        });
        hooks["Stop"] = stopArr;
        root["hooks"] = hooks;
    }

    private static void RemoveStopHook(JsonObject root)
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

    /// <summary>
    /// Injects balanced project hooks that are not yet present. Returns the list of injected event names.
    /// Existing non-Nebula groups are preserved; existing Nebula groups for an event are replaced.
    /// </summary>
    private static List<string> InjectBalancedHooks(JsonObject root)
    {
        var hooks = root["hooks"]?.AsObject() ?? new JsonObject();
        var injected = new List<string>();

        foreach (var (eventName, matcher, hookEvent, statusMessage) in BalancedHooks)
        {
            // Check if a Nebula balanced hook is already present for this event
            var existing = hooks[eventName]?.AsArray();
            bool alreadyPresent = existing is not null && existing.Any(entry =>
                entry?["hooks"]?.AsArray()
                    ?.Any(h => h?["command"]?.GetValue<string>()?.Contains(BalancedHookScript) == true) == true);

            if (alreadyPresent) continue;

            // Preserve non-Nebula groups, replace Nebula group
            var preserved = new JsonArray();
            if (existing is not null)
            {
                foreach (var group in existing)
                {
                    bool isNebula = group?["hooks"]?.AsArray()
                        ?.Any(h => h?["command"]?.GetValue<string>()?.Contains(HookMarker) == true) == true;
                    if (!isNebula && group is not null)
                        preserved.Add(group.DeepClone());
                }
            }

            preserved.Add(new JsonObject
            {
                ["matcher"] = matcher,
                ["hooks"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "command",
                        ["command"] = $"{BalancedHookScript} --agent Claude --event {hookEvent}",
                        ["timeout"] = 10,
                        ["statusMessage"] = statusMessage
                    }
                }
            });

            hooks[eventName] = preserved;
            injected.Add(eventName);
        }

        root["hooks"] = hooks;
        return injected;
    }

    /// <summary>
    /// Removes balanced project hooks that were installed by Nebula. Returns the list of removed event names.
    /// </summary>
    private static List<string> RemoveBalancedHooks(JsonObject root)
    {
        var hooks = root["hooks"]?.AsObject();
        if (hooks is null) return [];
        var removed = new List<string>();

        foreach (var (eventName, _, _, _) in BalancedHooks)
        {
            var arr = hooks[eventName]?.AsArray();
            if (arr is null) continue;

            var toRemove = arr.Where(entry =>
                entry?["hooks"]?.AsArray()
                    ?.Any(h => h?["command"]?.GetValue<string>()?.Contains(BalancedHookScript) == true) == true)
                .ToList();

            if (toRemove.Count == 0) continue;
            foreach (var item in toRemove) arr.Remove(item);
            removed.Add(eventName);
        }

        return removed;
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
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            // MCP endpoints only accept POST; send a JSON-RPC ping to verify reachability.
            var body = new StringContent(
                "{\"jsonrpc\":\"2.0\",\"id\":\"health\",\"method\":\"ping\"}",
                System.Text.Encoding.UTF8,
                "application/json");
            var resp = await http.PostAsync(url, body, cancellationToken);
            // Any HTTP response (including 4xx) means the server is reachable.
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"Endpoint unreachable: {ex.Message}");
        }
    }

    /// <summary>Resolves the user-level settings file path for the given client identifier.</summary>
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

    /// <summary>
    /// Resolves the project-level Claude Code settings path (<c>.claude/settings.json</c>)
    /// relative to the current working directory.
    /// </summary>
    public static string ResolveProjectSettingsPath() =>
        Path.Combine(Directory.GetCurrentDirectory(), ".claude", "settings.json");
}
