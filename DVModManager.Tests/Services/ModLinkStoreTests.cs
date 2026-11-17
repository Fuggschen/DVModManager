using DVModManager.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DVModManager.Tests.Services;

public class ModLinkStoreTests
{
    private static ModLinkStore NewStore() => new(NullLogger<ModLinkStore>.Instance);

    [Fact]
    public async Task SaveAndLoad_RoundTripsCaseInsensitively()
    {
        var storage = Path.Combine(Path.GetTempPath(), "Links_" + Guid.NewGuid());
        try
        {
            var store = NewStore();
            await store.LoadAsync(storage);
            store.Set("ModA", " https://www.nexusmods.com/derailvalley/mods/1 ", null);
            store.Set("ModB", null, "https://github.com/a/b");
            await store.SaveAsync();

            var reloaded = NewStore();
            await reloaded.LoadAsync(storage);

            Assert.Equal(new ModLinks("https://www.nexusmods.com/derailvalley/mods/1", null), reloaded.Get("moda"));
            Assert.Equal("https://github.com/a/b", reloaded.Get("MODB")!.RepositoryUrl);
            Assert.True(File.Exists(Path.Combine(storage, ModLinkStore.DirectoryName, ModLinkStore.FileName)));
        }
        finally
        {
            if (Directory.Exists(storage)) Directory.Delete(storage, true);
        }
    }

    [Fact]
    public async Task Set_BlankLinks_RemovesEntry()
    {
        var store = NewStore();
        await store.LoadAsync(Path.Combine(Path.GetTempPath(), "Links_" + Guid.NewGuid()));
        store.Set("ModA", "https://www.nexusmods.com/derailvalley/mods/1", null);

        store.Set("ModA", " ", "");

        Assert.Null(store.Get("ModA"));
    }

    [Fact]
    public async Task Load_MissingOrCorruptFile_IsEmpty()
    {
        var storage = Path.Combine(Path.GetTempPath(), "Links_" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(storage, ModLinkStore.DirectoryName));
        File.WriteAllText(Path.Combine(storage, ModLinkStore.DirectoryName, ModLinkStore.FileName), "{ broken");
        try
        {
            var store = NewStore();
            await store.LoadAsync(storage);
            Assert.Null(store.Get("anything"));

            var empty = NewStore();
            await empty.LoadAsync(Path.Combine(storage, "missing"));
            Assert.Null(empty.Get("anything"));
        }
        finally
        {
            Directory.Delete(storage, true);
        }
    }
}
