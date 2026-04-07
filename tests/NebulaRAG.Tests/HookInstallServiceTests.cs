using NebulaRAG.Core.Services;

namespace NebulaRAG.Tests;

/// <summary>
/// Unit tests for HookInstallService using temp files as settings.
/// </summary>
public sealed class HookInstallServiceTests
{
    private static readonly string NebulaHookCommand = "nebula-rag sync-repo-knowledge --source . --project-id repo-knowledge";
    private static readonly string NebulaHookScript = "Invoke-NebulaAgentHook.sh";

    private static string WriteSettings(string json)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, json);
        return path;
    }

    private static string EmptyTempFile()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, "{}");
        return path;
    }

    [Fact]
    public async Task InstallHooks_WritesStopHook_IntoEmptySettings()
    {
        var userPath = WriteSettings("{}");
        var projectPath = EmptyTempFile();
        var svc = new HookInstallService();
        var result = await svc.InstallHooksAsync("claude", dryRun: false,
            settingsPathOverride: userPath, projectSettingsPathOverride: projectPath);

        Assert.True(result.Success);
        var content = await File.ReadAllTextAsync(userPath);
        Assert.Contains(NebulaHookCommand, content);
        File.Delete(userPath);
        File.Delete(projectPath);
    }

    [Fact]
    public async Task InstallHooks_WritesBalancedHooks_IntoProjectSettings()
    {
        var userPath = WriteSettings("{}");
        var projectPath = EmptyTempFile();
        var svc = new HookInstallService();
        await svc.InstallHooksAsync("claude", dryRun: false,
            settingsPathOverride: userPath, projectSettingsPathOverride: projectPath);

        var content = await File.ReadAllTextAsync(projectPath);
        Assert.Contains(NebulaHookScript, content);
        Assert.Contains("SessionStart", content);
        Assert.Contains("PreToolUse", content);
        Assert.Contains("PostToolUseFailure", content);
        Assert.Contains("StopFailure", content);
        File.Delete(userPath);
        File.Delete(projectPath);
    }

    [Fact]
    public async Task InstallHooks_IsIdempotent()
    {
        var userPath = WriteSettings("{}");
        var projectPath = EmptyTempFile();
        var svc = new HookInstallService();
        await svc.InstallHooksAsync("claude", dryRun: false,
            settingsPathOverride: userPath, projectSettingsPathOverride: projectPath);
        await svc.InstallHooksAsync("claude", dryRun: false,
            settingsPathOverride: userPath, projectSettingsPathOverride: projectPath);

        var userContent = await File.ReadAllTextAsync(userPath);
        Assert.Equal(1, CountOccurrences(userContent, NebulaHookCommand));

        var projectContent = await File.ReadAllTextAsync(projectPath);
        // Each balanced hook event key should appear exactly once as a JSON property
        Assert.Equal(1, CountOccurrences(projectContent, "\"SessionStart\""));
        Assert.Equal(1, CountOccurrences(projectContent, "\"PreToolUse\""));
        Assert.Equal(1, CountOccurrences(projectContent, "\"PostToolUseFailure\""));
        File.Delete(userPath);
        File.Delete(projectPath);
    }

    [Fact]
    public async Task InstallHooks_DryRun_DoesNotWriteFile()
    {
        var userPath = WriteSettings("{}");
        var projectPath = EmptyTempFile();
        var originalUser = await File.ReadAllTextAsync(userPath);
        var originalProject = await File.ReadAllTextAsync(projectPath);
        var svc = new HookInstallService();
        var result = await svc.InstallHooksAsync("claude", dryRun: true,
            settingsPathOverride: userPath, projectSettingsPathOverride: projectPath);

        Assert.True(result.Success);
        Assert.NotNull(result.Diff);
        Assert.Contains(NebulaHookCommand, result.Diff);
        Assert.Equal(originalUser, await File.ReadAllTextAsync(userPath));
        Assert.Equal(originalProject, await File.ReadAllTextAsync(projectPath));
        File.Delete(userPath);
        File.Delete(projectPath);
    }

    [Fact]
    public async Task UninstallHooks_RemovesStopAndBalancedHooks()
    {
        var userPath = WriteSettings("{}");
        var projectPath = EmptyTempFile();
        var svc = new HookInstallService();
        await svc.InstallHooksAsync("claude", dryRun: false,
            settingsPathOverride: userPath, projectSettingsPathOverride: projectPath);
        var result = await svc.UninstallHooksAsync("claude", dryRun: false,
            settingsPathOverride: userPath, projectSettingsPathOverride: projectPath);

        Assert.True(result.Success);
        Assert.DoesNotContain(NebulaHookCommand, await File.ReadAllTextAsync(userPath));
        Assert.DoesNotContain(NebulaHookScript, await File.ReadAllTextAsync(projectPath));
        File.Delete(userPath);
        File.Delete(projectPath);
    }

    [Fact]
    public async Task UninstallHooks_DryRun_DoesNotWriteFile()
    {
        var userPath = WriteSettings("{}");
        var projectPath = EmptyTempFile();
        var svc = new HookInstallService();
        await svc.InstallHooksAsync("claude", dryRun: false,
            settingsPathOverride: userPath, projectSettingsPathOverride: projectPath);
        var beforeUser = await File.ReadAllTextAsync(userPath);
        var beforeProject = await File.ReadAllTextAsync(projectPath);
        var result = await svc.UninstallHooksAsync("claude", dryRun: true,
            settingsPathOverride: userPath, projectSettingsPathOverride: projectPath);

        Assert.True(result.Success);
        Assert.Equal(beforeUser, await File.ReadAllTextAsync(userPath));
        Assert.Equal(beforeProject, await File.ReadAllTextAsync(projectPath));
        File.Delete(userPath);
        File.Delete(projectPath);
    }

    [Fact]
    public async Task InstallHooks_Copilot_DoesNotWriteProjectHooks()
    {
        var userPath = WriteSettings("{}");
        var svc = new HookInstallService();
        var result = await svc.InstallHooksAsync("copilot", dryRun: false,
            settingsPathOverride: userPath);

        Assert.True(result.Success);
        var content = await File.ReadAllTextAsync(userPath);
        Assert.Contains(NebulaHookCommand, content);
        Assert.DoesNotContain(NebulaHookScript, content);
        File.Delete(userPath);
    }

    [Fact]
    public void ResolveSettingsPath_Claude_ReturnsExpectedPath()
    {
        var path = HookInstallService.ResolveSettingsPath("claude");
        Assert.NotNull(path);
        Assert.EndsWith(Path.Combine(".claude", "settings.json"), path);
    }

    [Fact]
    public void ResolveSettingsPath_Copilot_ReturnsExpectedPath()
    {
        var path = HookInstallService.ResolveSettingsPath("copilot");
        Assert.NotNull(path);
        Assert.Contains("copilot", path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveSettingsPath_UnknownClient_ReturnsNull()
    {
        var path = HookInstallService.ResolveSettingsPath("unknown-client");
        Assert.Null(path);
    }

    [Fact]
    public void ResolveProjectSettingsPath_ReturnsClaudeSettingsInCwd()
    {
        var path = HookInstallService.ResolveProjectSettingsPath();
        Assert.EndsWith(Path.Combine(".claude", "settings.json"), path);
    }

    [Theory]
    [InlineData("http://192.168.1.135:8099/nebula/mcp", "http://192.168.1.135:8099/nebula/api/health")]
    [InlineData("http://192.168.1.135:8099/nebula/mcp/", "http://192.168.1.135:8099/nebula/api/health")]
    [InlineData("http://192.168.1.135:8099/nebula", "http://192.168.1.135:8099/nebula/api/health")]
    [InlineData("http://localhost:8099/mcp", "http://localhost:8099/api/health")]
    [InlineData(null, "http://localhost:5001/api/health")]
    [InlineData("", "http://localhost:5001/api/health")]
    public void ResolveHealthUrl_DerivesCorrectHealthEndpoint(string? mcpUrl, string expected)
    {
        Assert.Equal(expected, HookInstallService.ResolveHealthUrl(mcpUrl));
    }

    private static int CountOccurrences(string text, string pattern) =>
        (text.Length - text.Replace(pattern, "").Length) / pattern.Length;
}
