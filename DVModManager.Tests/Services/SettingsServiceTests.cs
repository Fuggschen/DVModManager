using DVModManager.Services;
using Xunit;

namespace DVModManager.Tests.Services;

public class SettingsServiceTests : IDisposable
{
    private readonly string _workDir = Path.Combine(Path.GetTempPath(), "SettingsSvc_" + Guid.NewGuid().ToString("N"));
    private string SettingsPath => Path.Combine(_workDir, "settings.json");

    public void Dispose() => TestData.DeleteDirectory(_workDir);

    private string[] BackupFiles() =>
        Directory.Exists(_workDir) ? Directory.GetFiles(_workDir, "settings.json.*.bak") : [];

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTripsSettings()
    {
        var service = TestData.CreateSettingsService(_workDir);
        service.Settings.GamePath = @"C:\Games\Derail Valley";
        await service.SaveAsync();

        var reloaded = TestData.CreateSettingsService(_workDir);
        await reloaded.LoadAsync();

        Assert.Equal(@"C:\Games\Derail Valley", reloaded.Settings.GamePath);
    }

    [Fact]
    public async Task SaveAsync_LeavesNoTempFileBehind()
    {
        var service = TestData.CreateSettingsService(_workDir);
        await service.SaveAsync();
        await service.SaveAsync();

        Assert.True(File.Exists(SettingsPath));
        Assert.False(File.Exists(SettingsPath + ".tmp"));
    }

    [Fact]
    public async Task SaveAsync_ConcurrentCalls_AllSucceed()
    {
        var service = TestData.CreateSettingsService(_workDir);
        service.Settings.GamePath = "/game";

        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => service.SaveAsync()));

        var reloaded = TestData.CreateSettingsService(_workDir);
        await reloaded.LoadAsync();
        Assert.Equal("/game", reloaded.Settings.GamePath);
    }

    [Fact]
    public async Task LoadAsync_CorruptFile_BacksItUpAndUsesDefaults()
    {
        Directory.CreateDirectory(_workDir);
        const string corrupt = "{ \"GamePath\": \"/game\", ";
        await File.WriteAllTextAsync(SettingsPath, corrupt);

        var service = TestData.CreateSettingsService(_workDir);
        await service.LoadAsync();

        Assert.Null(service.Settings.GamePath);
        var backup = Assert.Single(BackupFiles());
        Assert.Equal(corrupt, await File.ReadAllTextAsync(backup));
    }

    [Fact]
    public async Task SaveAsync_AfterCorruptLoad_KeepsTheBackup()
    {
        Directory.CreateDirectory(_workDir);
        const string corrupt = "not json";
        await File.WriteAllTextAsync(SettingsPath, corrupt);

        var service = TestData.CreateSettingsService(_workDir);
        await service.LoadAsync();
        await service.SaveAsync();

        Assert.NotEqual(corrupt, await File.ReadAllTextAsync(SettingsPath));
        Assert.Equal(corrupt, await File.ReadAllTextAsync(Assert.Single(BackupFiles())));
    }

    [Fact]
    public async Task SaveAsync_AfterLoadOfLockedFile_DoesNotOverwriteIt()
    {
        // Exclusive file locks are only enforced on Windows
        if (!OperatingSystem.IsWindows()) return;

        Directory.CreateDirectory(_workDir);
        const string original = "{ \"GamePath\": \"/game\" }";
        await File.WriteAllTextAsync(SettingsPath, original);

        var service = TestData.CreateSettingsService(_workDir);
        using (new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            // Neither readable nor copyable while locked
            await service.LoadAsync();
        }

        await service.SaveAsync();

        Assert.Equal(original, await File.ReadAllTextAsync(SettingsPath));
        Assert.Empty(BackupFiles());
    }
}
