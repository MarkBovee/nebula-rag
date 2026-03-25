using System.Text.Json.Nodes;

namespace NebulaRAG.Core.Services;

/// <summary>
/// Handles the nebula_setup MCP tool: install-hooks, uninstall-hooks, status.
/// </summary>
public sealed class NebulaSetupToolHandler
{
    private readonly HookInstallService _hookInstallService;
    private readonly string? _localHealthUrl;

    /// <summary>
    /// Initializes a new instance of the <see cref="NebulaSetupToolHandler"/> class.
    /// </summary>
    /// <param name="hookInstallService">Hook install service for performing setup operations.</param>
    /// <param name="localHealthUrl">Local health check URL, built via <c>RagSettings.BuildLocalHealthUrl()</c>.</param>
    public NebulaSetupToolHandler(HookInstallService hookInstallService, string? localHealthUrl = null)
    {
        _hookInstallService = hookInstallService;
        _localHealthUrl = localHealthUrl;
    }

    /// <summary>Dispatches the nebula_setup action to the appropriate handler.</summary>
    /// <param name="arguments">Tool arguments.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>MCP tool result payload.</returns>
    public async Task<JsonObject> HandleAsync(JsonObject? arguments, CancellationToken cancellationToken)
    {
        var action = arguments?["action"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(action))
        {
            return BuildError("action is required and must be one of: install-hooks, uninstall-hooks, status.");
        }

        return action switch
        {
            "install-hooks" => await HandleInstallAsync(arguments!, GetDryRun(arguments), cancellationToken),
            "uninstall-hooks" => await HandleUninstallAsync(arguments!, GetDryRun(arguments), cancellationToken),
            "status" => await HandleStatusAsync(arguments, cancellationToken),
            _ => BuildError("Unsupported action. Use: install-hooks, uninstall-hooks, status.")
        };
    }

    private async Task<JsonObject> HandleInstallAsync(JsonObject args, bool dryRun, CancellationToken ct)
    {
        var client = args["client"]?.GetValue<string>() ?? "claude";
        var projectPath = GetProjectPath(args);
        var result = await _hookInstallService.InstallHooksAsync(client, dryRun, projectSettingsPathOverride: projectPath);
        return BuildResult(result.Success, result.Message, result.Diff);
    }

    private async Task<JsonObject> HandleUninstallAsync(JsonObject args, bool dryRun, CancellationToken ct)
    {
        var client = args["client"]?.GetValue<string>() ?? "claude";
        var projectPath = GetProjectPath(args);
        var result = await _hookInstallService.UninstallHooksAsync(client, dryRun, projectSettingsPathOverride: projectPath);
        return BuildResult(result.Success, result.Message, result.Diff);
    }

    private async Task<JsonObject> HandleStatusAsync(JsonObject? args, CancellationToken ct)
    {
        var projectPath = GetProjectPath(args);
        var statuses = await _hookInstallService.GetStatusAsync(_localHealthUrl, projectSettingsPathOverride: projectPath, cancellationToken: ct);
        var arr = new JsonArray(statuses.Select(s => (JsonNode)new JsonObject
        {
            ["client"] = s.Client,
            ["settingsFileExists"] = s.SettingsFileExists,
            ["hookInstalled"] = s.HookInstalled,
            ["endpointReachable"] = s.EndpointReachable,
            ["endpointWarning"] = s.EndpointWarning
        }).ToArray());
        return new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = arr.ToJsonString() })
        };
    }

    private static bool GetDryRun(JsonObject? args) =>
        args?["dry_run"]?.GetValue<bool>() ?? false;

    /// <summary>
    /// Resolves the project-level settings path override from args.
    /// If <c>project_path</c> is provided and non-empty, returns
    /// <c>{project_path}/.claude/settings.json</c>; otherwise returns null
    /// so HookInstallService falls back to its own cwd resolution.
    /// </summary>
    private static string? GetProjectPath(JsonObject? args)
    {
        var raw = args?["project_path"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return Path.Combine(raw.Trim(), ".claude", "settings.json");
    }

    private static JsonObject BuildResult(bool success, string message, string? diff) =>
        new()
        {
            ["content"] = new JsonArray(new JsonObject
            {
                ["type"] = "text",
                ["text"] = $"{(success ? "✓" : "✗")} {message}" + (diff is not null ? $"\n{diff}" : "")
            }),
            ["isError"] = !success
        };

    private static JsonObject BuildError(string message) =>
        new()
        {
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = message }),
            ["isError"] = true
        };
}
