using NebulaRAG.Core.Services;

namespace NebulaRAG.Tests;

/// <summary>
/// Unit tests for HookInstallService using temp files as settings.
/// </summary>
public sealed class HookInstallServiceTests
{
    private static readonly string NebulaHookCommand = "nebula-rag memory sync";

    private static string WriteSettings(string json)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public async Task InstallHooks_WritesHookEntry_IntoEmptySettings()
    {
        var path = WriteSettings("{}");
        var svc = new HookInstallService();
        var result = await svc.InstallHooksAsync("claude", dryRun: false, settingsPathOverride: path);

        Assert.True(result.Success);
        var content = await File.ReadAllTextAsync(path);
        Assert.Contains(NebulaHookCommand, content);
        File.Delete(path);
    }

    [Fact]
    public async Task InstallHooks_IsIdempotent()
    {
        var path = WriteSettings("{}");
        var svc = new HookInstallService();
        await svc.InstallHooksAsync("claude", dryRun: false, settingsPathOverride: path);
        await svc.InstallHooksAsync("claude", dryRun: false, settingsPathOverride: path);

        var content = await File.ReadAllTextAsync(path);
        var count = CountOccurrences(content, NebulaHookCommand);
        Assert.Equal(1, count);
        File.Delete(path);
    }

    [Fact]
    public async Task InstallHooks_DryRun_DoesNotWriteFile()
    {
        var path = WriteSettings("{}");
        var original = await File.ReadAllTextAsync(path);
        var svc = new HookInstallService();
        var result = await svc.InstallHooksAsync("claude", dryRun: true, settingsPathOverride: path);

        Assert.True(result.Success);
        Assert.NotNull(result.Diff);
        Assert.Contains(NebulaHookCommand, result.Diff);
        Assert.Equal(original, await File.ReadAllTextAsync(path));
        File.Delete(path);
    }

    [Fact]
    public async Task UninstallHooks_RemovesHookEntry()
    {
        var path = WriteSettings("{}");
        var svc = new HookInstallService();
        await svc.InstallHooksAsync("claude", dryRun: false, settingsPathOverride: path);
        var result = await svc.UninstallHooksAsync("claude", dryRun: false, settingsPathOverride: path);

        Assert.True(result.Success);
        var content = await File.ReadAllTextAsync(path);
        Assert.DoesNotContain(NebulaHookCommand, content);
        File.Delete(path);
    }

    [Fact]
    public async Task UninstallHooks_DryRun_DoesNotWriteFile()
    {
        var path = WriteSettings("{}");
        var svc = new HookInstallService();
        await svc.InstallHooksAsync("claude", dryRun: false, settingsPathOverride: path);
        var beforeUninstall = await File.ReadAllTextAsync(path);
        var result = await svc.UninstallHooksAsync("claude", dryRun: true, settingsPathOverride: path);

        Assert.True(result.Success);
        Assert.Equal(beforeUninstall, await File.ReadAllTextAsync(path));
        File.Delete(path);
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

    private static int CountOccurrences(string text, string pattern) =>
        (text.Length - text.Replace(pattern, "").Length) / pattern.Length;
}
