using DVModManager.Models;
using DVModManager.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DVModManager.Tests.Services;

public class UmmPayloadServiceTests : IDisposable
{
    private readonly string _workDir = Path.Combine(Path.GetTempPath(), "Payload_" + Guid.NewGuid().ToString("N"));

    public UmmPayloadServiceTests() => Directory.CreateDirectory(_workDir);

    public void Dispose() => TestData.DeleteDirectory(_workDir);

    private UmmPayloadService CreateService(StubHttpMessageHandler? handler = null)
    {
        var settings = new SettingsService(NullLogger<SettingsService>.Instance);
        settings.Settings.StoragePath = Path.Combine(_workDir, "storage");
        return new UmmPayloadService(
            new UmmReleaseFeedService(new StubHttpClientFactory(handler ?? new StubHttpMessageHandler()),
                NullLogger<UmmReleaseFeedService>.Instance),
            settings,
            new StubHttpClientFactory(handler ?? new StubHttpMessageHandler()),
            NullLogger<UmmPayloadService>.Instance);
    }

    [Fact]
    public void ImportFromZip_FullPackage_ExtractsAllRequiredFiles()
    {
        var zip = TestData.CreateUmmZip(_workDir);
        var service = CreateService();

        var payload = service.ImportFromZip(zip);

        Assert.NotNull(payload);
        Assert.Equal("0.33.2", payload!.Version);
        Assert.True(payload.HasDoorstopProxies);
        foreach (var rel in UmmPayloadService.RequiredFiles)
        {
            var path = Path.Combine(payload.Directory, rel.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), $"missing extracted file: {rel}");
        }
        // The two 0Harmony.dll locations must not collide.
        Assert.Equal("harmony-root", File.ReadAllText(Path.Combine(payload.Directory, "0Harmony.dll")));
        Assert.Equal("harmony-2.2",
            File.ReadAllText(Path.Combine(payload.Directory, "Harmony", "2.2", "0Harmony.dll")));
    }

    [Fact]
    public void ImportFromZip_NestedRootFolder_StillResolves()
    {
        var zip = TestData.CreateUmmZip(_workDir, rootPrefix: "UnityModManager");
        var service = CreateService();

        var payload = service.ImportFromZip(zip);

        Assert.NotNull(payload);
        Assert.True(File.Exists(Path.Combine(payload!.Directory, "UnityModManager.dll")));
        Assert.True(File.Exists(Path.Combine(payload.Directory, "Harmony", "1.2", "0Harmony12.dll")));
    }

    [Fact]
    public void ImportFromZip_PatchOnlyPackage_IsRejected()
    {
        // UMM's legacy feed serves a 3-file patch zip — it must never pass
        // validation for a fresh install.
        var zipPath = Path.Combine(_workDir, "patch.zip");
        using (var stream = File.Create(zipPath))
        using (var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create))
        {
            foreach (var name in new[] { "0Harmony12.dll", "UnityModManager.exe", "UnityModManagerConfig.xml" })
            {
                var entry = zip.CreateEntry(name);
                using var es = entry.Open();
                es.Write("x"u8.ToArray());
            }
        }

        Assert.Null(CreateService().ImportFromZip(zipPath));
    }

    [Fact]
    public void ImportFromZip_ForeignLoaderAssembly_IsRejected()
    {
        var zip = TestData.CreateUmmZip(_workDir, includeLoaderAssembly: false);

        Assert.Null(CreateService().ImportFromZip(zip));
    }

    [Fact]
    public void ImportFromZip_ParsesDerailValleyGameInfo()
    {
        var zip = TestData.CreateUmmZip(_workDir);
        var payload = CreateService().ImportFromZip(zip);

        Assert.NotNull(payload);
        Assert.Equal("[UnityEngine.CoreModule.dll]UnityEngine.Display.cctor:After",
            payload!.GameInfo.EntryPoint);
        Assert.Equal("0.27.0", payload.GameInfo.MinimalManagerVersion);
    }

    [Fact]
    public async Task GetPayloadAsync_DownloadsFromFeed_AndReusesCache()
    {
        var zipBytes = File.ReadAllBytes(TestData.CreateUmmZip(_workDir, version: "0.33.2"));
        var handler = new StubHttpMessageHandler();
        handler.Map(UmmReleaseFeedService.FeedUrls[0],
            """{ "Releases": [ { "Id": "UnityModManager", "Version": "0.33.2", "DownloadUrl": "https://example.invalid/umm_update.zip" } ] }""");
        handler.MapNotFound(UmmReleaseFeedService.FeedUrls[1]);
        handler.MapBytes("https://example.invalid/umm_update.zip", zipBytes);

        var service = CreateService(handler);

        var payload = await service.GetPayloadAsync();
        Assert.NotNull(payload);
        Assert.Equal("0.33.2", payload!.Version);

        // Second call must hit the cache even with the feed dead.
        handler.MapNotFound(UmmReleaseFeedService.FeedUrls[0]);
        var cached = await service.GetPayloadAsync();
        Assert.NotNull(cached);
        Assert.Equal(payload.Directory, cached!.Directory);
    }

    [Fact]
    public async Task GetPayloadAsync_OfflineWithoutCache_ReturnsNull()
    {
        var service = CreateService(new StubHttpMessageHandler()); // everything 404s

        Assert.Null(await service.GetPayloadAsync());
    }
}
