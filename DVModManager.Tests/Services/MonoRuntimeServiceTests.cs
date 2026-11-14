using System.Security.Cryptography;
using System.Text;
using DVModManager.Models;
using DVModManager.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DVModManager.Tests.Services;

public class MonoRuntimeServiceTests : IDisposable
{
    private const string FeedUrl = "https://example.test/repository.json";
    private const string StandardUrl = "https://example.test/standard/mono-2.0-bdwgc.dll";
    private const string DebugUrl = "https://example.test/debug/mono-2.0-bdwgc.dll";

    private static readonly byte[] StockBytes = "stock-runtime"u8.ToArray();
    private static readonly byte[] StandardBytes = "patched-runtime"u8.ToArray();
    private static readonly byte[] DebugBytes = "patched-runtime-debug"u8.ToArray();

    private readonly string _workDir = Path.Combine(Path.GetTempPath(), "MonoRuntime_" + Guid.NewGuid().ToString("N"));
    private readonly string _gameDir;
    private readonly StubHttpMessageHandler _http = new();

    public MonoRuntimeServiceTests()
    {
        _gameDir = Path.Combine(_workDir, "game");
        Directory.CreateDirectory(MonoRuntimeService.GetRuntimeDirectory(_gameDir));
        Directory.CreateDirectory(Path.Combine(_gameDir, "DerailValley_Data"));
        WriteUnityVersion(MonoRuntimeService.SupportedUnityVersion);
        File.WriteAllBytes(RuntimePath, StockBytes);
        MapFeed(("GcRootsFix", "0.0.1", StandardUrl), ("GcRootsFixDebug", "0.0.1", DebugUrl));
        _http.MapBytes(StandardUrl, StandardBytes);
        _http.MapBytes(DebugUrl, DebugBytes);
    }

    public void Dispose() => TestData.DeleteDirectory(_workDir);

    private string RuntimePath => MonoRuntimeService.GetRuntimePath(_gameDir);
    private string BackupPath => MonoRuntimeService.GetBackupPath(_gameDir);

    private static string Sha256(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    private void WriteUnityVersion(string version)
    {
        // Serialized-file header layout: binary fields, then the version string.
        var header = new byte[20].Concat(Encoding.ASCII.GetBytes(version)).Concat(new byte[] { 0, 0, 0 }).ToArray();
        File.WriteAllBytes(Path.Combine(_gameDir, "DerailValley_Data", "globalgamemanagers"), header);
    }

    private void MapFeed(params (string Id, string Version, string Url)[] releases) =>
        MapFeedWithSha(releases.Select(r => (r.Id, r.Version, r.Url, (string?)null)).ToArray());

    private void MapFeedWithSha(params (string Id, string Version, string Url, string? Sha)[] releases)
    {
        var items = releases.Select(r => r.Sha == null
            ? $$"""{"Id":"{{r.Id}}","Version":"{{r.Version}}","DownloadUrl":"{{r.Url}}"}"""
            : $$"""{"Id":"{{r.Id}}","Version":"{{r.Version}}","DownloadUrl":"{{r.Url}}","Sha256Sum":"{{r.Sha}}"}""");
        _http.Map(FeedUrl, $$"""{"Releases":[{{string.Join(",", items)}}]}""");
    }

    private MonoRuntimeService CreateService()
    {
        var settings = TestData.CreateSettingsService(_workDir);
        settings.Settings.StoragePath = Path.Combine(_workDir, "storage");
        return new MonoRuntimeService(settings, new StubHttpClientFactory(_http),
            NullLogger<MonoRuntimeService>.Instance, FeedUrl, [Sha256(StockBytes)]);
    }

    [Fact]
    public void ParseFeed_ReadsAuthorFeed()
    {
        const string json = """
            {
              "Releases": [
                {
                  "Id": "GcRootsFix",
                  "Version": "0.0.1",
                  "DownloadUrl": "https://github.com/geluk/mono/releases/download/2019.4.40f1-gc-roots-fix.1/mono-2.0-bdwgc.dll"
                },
                {
                  "Id": "GcRootsFixDebug",
                  "Version": "0.0.1",
                  "DownloadUrl": "https://github.com/geluk/mono/releases/download/2019.4.40f1-gc-roots-fix.1/mono-2.0-bdwgc.dll"
                }
              ]
            }
            """;

        var releases = MonoRuntimeService.ParseFeed(json);

        Assert.Equal(["GcRootsFix", "GcRootsFixDebug"], releases.Select(r => r.Id));
        Assert.All(releases, r => Assert.Equal("0.0.1", r.Version));
    }

    [Fact]
    public void ParseFeed_ReadsSha256SumLowercase()
    {
        const string json = """
            {"Releases":[
              {"Id":"GcRootsFix","Version":"0.0.1","DownloadUrl":"https://x.test/a","Sha256Sum":" D6422C41F36A "},
              {"Id":"GcRootsFixDebug","Version":"0.0.1","DownloadUrl":"https://x.test/b"}
            ]}
            """;

        var releases = MonoRuntimeService.ParseFeed(json);

        Assert.Equal("d6422c41f36a", releases[0].Sha256);
        Assert.Null(releases[1].Sha256);
    }

    [Fact]
    public async Task InstallPatch_MatchingSha256Sum_Installs()
    {
        MapFeedWithSha(("GcRootsFix", "0.0.1", StandardUrl, Sha256(StandardBytes)));

        Assert.True(await CreateService().InstallPatchAsync(_gameDir, MonoRuntimeChannel.Standard));

        Assert.Equal(StandardBytes, File.ReadAllBytes(RuntimePath));
    }

    [Fact]
    public async Task InstallPatch_Sha256SumMismatch_LeavesGameUntouched()
    {
        MapFeedWithSha(("GcRootsFix", "0.0.1", StandardUrl, new string('0', 64)));

        Assert.False(await CreateService().InstallPatchAsync(_gameDir, MonoRuntimeChannel.Standard));

        Assert.Equal(StockBytes, File.ReadAllBytes(RuntimePath));
        Assert.False(File.Exists(BackupPath));
    }

    [Fact]
    public async Task InstallPatch_CacheNotMatchingSha256Sum_DownloadsAgain()
    {
        Assert.True(await CreateService().InstallPatchAsync(_gameDir, MonoRuntimeChannel.Standard));
        await CreateService().RestoreOriginalAsync(_gameDir);
        // The author replaced the file and published its hash.
        var fixedBytes = "patched-runtime-fixed"u8.ToArray();
        _http.MapBytes(StandardUrl, fixedBytes);
        MapFeedWithSha(("GcRootsFix", "0.0.1", StandardUrl, Sha256(fixedBytes)));

        Assert.True(await CreateService().InstallPatchAsync(_gameDir, MonoRuntimeChannel.Standard));

        Assert.Equal(fixedBytes, File.ReadAllBytes(RuntimePath));
    }

    [Fact]
    public async Task GetStatus_ManualInstall_IdentifiedByFeedSha256Sum()
    {
        File.WriteAllBytes(RuntimePath, DebugBytes);
        MapFeedWithSha(
            ("GcRootsFix", "0.0.1", StandardUrl, Sha256(StandardBytes)),
            ("GcRootsFixDebug", "0.0.3", DebugUrl, Sha256(DebugBytes)));
        var service = CreateService();
        Assert.Null(service.GetStatus(_gameDir, MonoRuntimeChannel.Standard).PatchId);

        await service.GetLatestReleaseAsync(MonoRuntimeChannel.Standard);

        var status = service.GetStatus(_gameDir, MonoRuntimeChannel.Standard);
        Assert.Equal(MonoRuntimeKind.Modified, status.Kind);
        Assert.Equal("GcRootsFixDebug", status.PatchId);
        Assert.Equal("0.0.3", status.PatchVersion);
    }

    [Theory]
    [InlineData(MonoRuntimeChannel.Standard, StandardUrl)]
    [InlineData(MonoRuntimeChannel.Debug, DebugUrl)]
    public async Task GetLatestRelease_PicksChannelEntry(MonoRuntimeChannel channel, string url)
    {
        var release = await CreateService().GetLatestReleaseAsync(channel);

        Assert.Equal(url, release?.DownloadUrl);
    }

    [Fact]
    public async Task GetLatestRelease_PicksHighestVersion()
    {
        MapFeed(
            ("GcRootsFix", "0.0.2", "https://example.test/0.0.2"),
            ("GcRootsFix", "0.0.10", "https://example.test/0.0.10"),
            ("GcRootsFixDebug", "0.1.0", "https://example.test/debug"));

        var release = await CreateService().GetLatestReleaseAsync(MonoRuntimeChannel.Standard);

        Assert.Equal("0.0.10", release?.Version);
    }

    [Fact]
    public async Task GetLatestRelease_FeedOffline_ReturnsNull()
    {
        _http.MapNotFound(FeedUrl);

        Assert.Null(await CreateService().GetLatestReleaseAsync(MonoRuntimeChannel.Standard));
    }

    [Fact]
    public void GetStatus_FreshGame_ReadsUnityVersion()
    {
        var status = CreateService().GetStatus(_gameDir, MonoRuntimeChannel.Standard);

        Assert.Equal(MonoRuntimeKind.Stock, status.Kind);
        Assert.Equal(MonoRuntimeService.SupportedUnityVersion, status.UnityVersion);
        Assert.True(status.UnityVersionSupported);
        Assert.False(status.BackupPresent);
    }

    [Fact]
    public void GetStatus_NoRuntimeDll_IsNotFound()
    {
        File.Delete(RuntimePath);

        Assert.Equal(MonoRuntimeKind.NotFound, CreateService().GetStatus(_gameDir, MonoRuntimeChannel.Standard).Kind);
    }

    [Theory]
    [InlineData(MonoRuntimeChannel.Standard, "GcRootsFix")]
    [InlineData(MonoRuntimeChannel.Debug, "GcRootsFixDebug")]
    public async Task InstallPatch_BacksUpOriginalAndInstallsChannel(MonoRuntimeChannel channel, string id)
    {
        var service = CreateService();

        Assert.True(await service.InstallPatchAsync(_gameDir, channel));

        Assert.Equal(channel == MonoRuntimeChannel.Debug ? DebugBytes : StandardBytes, File.ReadAllBytes(RuntimePath));
        Assert.Equal(StockBytes, File.ReadAllBytes(BackupPath));
        var status = service.GetStatus(_gameDir, channel);
        Assert.Equal(MonoRuntimeKind.Modified, status.Kind);
        Assert.Equal(id, status.PatchId);
        Assert.Equal("0.0.1", status.PatchVersion);
    }

    [Fact]
    public async Task InstallPatch_SwitchChannel_ReplacesPatchAndKeepsBackup()
    {
        var service = CreateService();
        Assert.True(await service.InstallPatchAsync(_gameDir, MonoRuntimeChannel.Standard));

        Assert.True(await service.InstallPatchAsync(_gameDir, MonoRuntimeChannel.Debug));

        Assert.Equal(DebugBytes, File.ReadAllBytes(RuntimePath));
        Assert.Equal(StockBytes, File.ReadAllBytes(BackupPath));
    }

    [Fact]
    public async Task GetStatus_SameFileInBothChannels_ReportsSelectedChannel()
    {
        // The author's feed currently points both channels at the same DLL.
        _http.MapBytes(DebugUrl, StandardBytes);
        var service = CreateService();
        Assert.True(await service.InstallPatchAsync(_gameDir, MonoRuntimeChannel.Standard));
        Assert.True(await service.InstallPatchAsync(_gameDir, MonoRuntimeChannel.Debug));

        Assert.Equal("GcRootsFix", service.GetStatus(_gameDir, MonoRuntimeChannel.Standard).PatchId);
        Assert.Equal("GcRootsFixDebug", service.GetStatus(_gameDir, MonoRuntimeChannel.Debug).PatchId);
    }

    [Fact]
    public async Task InstallPatch_Twice_KeepsOriginalBackup()
    {
        var service = CreateService();

        Assert.True(await service.InstallPatchAsync(_gameDir, MonoRuntimeChannel.Standard));
        Assert.True(await service.InstallPatchAsync(_gameDir, MonoRuntimeChannel.Standard));

        Assert.Equal(StockBytes, File.ReadAllBytes(BackupPath));
    }

    [Fact]
    public async Task InstallPatch_OtherUnityVersion_LeavesGameUntouched()
    {
        WriteUnityVersion("2020.3.1f1");

        Assert.False(await CreateService().InstallPatchAsync(_gameDir, MonoRuntimeChannel.Standard));

        Assert.Equal(StockBytes, File.ReadAllBytes(RuntimePath));
        Assert.False(File.Exists(BackupPath));
    }

    [Fact]
    public async Task InstallPatch_DownloadFails_LeavesGameUntouched()
    {
        _http.MapNotFound(StandardUrl);

        Assert.False(await CreateService().InstallPatchAsync(_gameDir, MonoRuntimeChannel.Standard));

        Assert.Equal(StockBytes, File.ReadAllBytes(RuntimePath));
        Assert.False(File.Exists(BackupPath));
    }

    [Fact]
    public async Task InstallPatch_UnknownRuntimeWithBackup_KeepsExistingBackup()
    {
        File.WriteAllBytes(BackupPath, StockBytes);
        File.WriteAllBytes(RuntimePath, "other-build"u8.ToArray());

        Assert.True(await CreateService().InstallPatchAsync(_gameDir, MonoRuntimeChannel.Standard));

        Assert.Equal(StockBytes, File.ReadAllBytes(BackupPath));
    }

    [Fact]
    public async Task InstallPatch_ModifiedRuntimeWithoutBackup_CreatesNoBackup()
    {
        var other = "other-build"u8.ToArray();
        File.WriteAllBytes(RuntimePath, other);
        var service = CreateService();
        Assert.Equal(MonoRuntimeKind.Modified, service.GetStatus(_gameDir, MonoRuntimeChannel.Standard).Kind);

        Assert.True(await service.InstallPatchAsync(_gameDir, MonoRuntimeChannel.Standard));

        Assert.Equal(StandardBytes, File.ReadAllBytes(RuntimePath));
        Assert.False(File.Exists(BackupPath));
    }

    [Fact]
    public async Task GetStatus_FileReleasedUnderSeveralVersions_ReportsHighest()
    {
        var service = CreateService();
        Assert.True(await service.InstallPatchAsync(_gameDir, MonoRuntimeChannel.Standard));
        // Same file re-released as 0.0.10 (sorts below 0.0.2 as text).
        MapFeed(("GcRootsFix", "0.0.10", "https://example.test/0.0.10"));
        _http.MapBytes("https://example.test/0.0.10", StandardBytes);
        Assert.True(await service.InstallPatchAsync(_gameDir, MonoRuntimeChannel.Standard));
        MapFeed(("GcRootsFix", "0.0.2", "https://example.test/0.0.2"));
        _http.MapBytes("https://example.test/0.0.2", StandardBytes);

        // 0.0.2 is not downloaded: the installed file matches 0.0.1 and 0.0.10.
        Assert.Equal("0.0.10", service.GetStatus(_gameDir, MonoRuntimeChannel.Standard).PatchVersion);
    }

    [Fact]
    public async Task RestoreOriginal_ModifiedBackup_IsRefused()
    {
        File.WriteAllBytes(RuntimePath, StandardBytes);
        File.WriteAllBytes(BackupPath, "not-stock"u8.ToArray());

        Assert.False(await CreateService().RestoreOriginalAsync(_gameDir));

        Assert.Equal(StandardBytes, File.ReadAllBytes(RuntimePath));
    }

    [Fact]
    public async Task InstallPatch_UsesCacheWithoutDownloading()
    {
        Assert.True(await CreateService().InstallPatchAsync(_gameDir, MonoRuntimeChannel.Standard));
        await CreateService().RestoreOriginalAsync(_gameDir);
        _http.MapNotFound(StandardUrl);

        Assert.True(await CreateService().InstallPatchAsync(_gameDir, MonoRuntimeChannel.Standard));
        Assert.Equal(StandardBytes, File.ReadAllBytes(RuntimePath));
    }

    [Fact]
    public async Task RestoreOriginal_PutsBackupBackAndRemovesIt()
    {
        var service = CreateService();
        await service.InstallPatchAsync(_gameDir, MonoRuntimeChannel.Standard);

        Assert.True(await service.RestoreOriginalAsync(_gameDir));

        Assert.Equal(StockBytes, File.ReadAllBytes(RuntimePath));
        Assert.False(File.Exists(BackupPath));
    }

    [Fact]
    public async Task RestoreOriginal_AfterUnityUpgrade_KeepsNewRuntime()
    {
        await CreateService().InstallPatchAsync(_gameDir, MonoRuntimeChannel.Standard);
        // Game update: new Unity version and its own runtime, stale backup left behind.
        WriteUnityVersion("6000.0.58f2");
        var unity6Runtime = "unity6-runtime"u8.ToArray();
        File.WriteAllBytes(RuntimePath, unity6Runtime);

        var service = CreateService();
        Assert.False(await service.RestoreOriginalAsync(_gameDir));

        Assert.Equal(unity6Runtime, File.ReadAllBytes(RuntimePath));
        var status = service.GetStatus(_gameDir, MonoRuntimeChannel.Standard);
        Assert.Equal("6000.0.58f2", status.UnityVersion);
        Assert.False(status.UnityVersionSupported);
    }

    [Fact]
    public async Task RestoreOriginal_NoBackup_Fails()
    {
        File.WriteAllBytes(RuntimePath, StandardBytes);

        Assert.False(await CreateService().RestoreOriginalAsync(_gameDir));
        Assert.Equal(StandardBytes, File.ReadAllBytes(RuntimePath));
    }
}
