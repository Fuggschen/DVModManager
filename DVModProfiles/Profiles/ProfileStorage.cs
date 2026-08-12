using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Steamworks;

namespace DVModProfiles.Profiles;

// Persistence backend for per-profile mod settings and legacy associations. Keys are unix-style
// paths.
public interface IStorageBackend
{
    bool TryRead(string key, out string text);
    void Write(string key, string text);
    void Delete(string key);
    IEnumerable<string> ListKeys(string prefix);
}

// Resolves the storage backend once, lazily (after the game has initialized Steamworks)
public static class ProfileStorage
{
    private const string PROFILE_PREFIX = "profiles/";
    private const string ASSOCIATIONS_KEY = "associations.json";

    private static IStorageBackend? backend;

    public static IStorageBackend Backend => backend ??= Choose();

    // Forces the backend to be re-resolved on next access
    public static void Reset() => backend = null;

    // Applies a change to the Steam Cloud opt-in setting, copying existing files from the old
    // backend into the new one so saved mod settings aren't lost when switching.
    public static void SetUseSteamCloud(bool useSteamCloud)
    {
        if (useSteamCloud == Main.Config.useSteamCloud)
        {
            return;
        }

        IStorageBackend from = Backend;
        Main.Config.useSteamCloud = useSteamCloud;
        Reset();
        IStorageBackend to = Backend;
        Migrate(from, to);
    }

    private static void Migrate(IStorageBackend from, IStorageBackend to)
    {
        if (from.GetType() == to.GetType())
        {
            return;
        }

        try
        {
            var keys = from.ListKeys(PROFILE_PREFIX)
                .Concat([ASSOCIATIONS_KEY])
                .Distinct();

            int copied = 0;
            foreach (string key in keys)
            {
                if (from.TryRead(key, out string text))
                {
                    to.Write(key, text);
                    copied++;
                }
            }

            Main.Logger.Log($"Migrated {copied} files from {from.GetType().Name} to {to.GetType().Name}");
        }
        catch (Exception ex)
        {
            Main.Logger.LogException("Backend migration failed", ex);
        }
    }

    private static IStorageBackend Choose()
    {
        if (!Main.Config.useSteamCloud)
        {
            Main.Logger.Log("Steam Cloud disabled in settings; using local file storage only");
            return new LocalBackend();
        }

        try
        {
            bool valid = SteamClient.IsValid;
            bool account = valid && SteamRemoteStorage.IsCloudEnabledForAccount;
            bool app = valid && SteamRemoteStorage.IsCloudEnabledForApp;
            Main.Logger.Log($"Steam Cloud check: SteamClient.IsValid={valid}, " +
                             $"IsCloudEnabledForAccount={account}, IsCloudEnabledForApp={app}");

            if (valid && account && app)
            {
                Main.Logger.Log("Using Steam Cloud storage (Steam handles sync and conflicts)");
                return new CloudBackend();
            }
        }
        catch (Exception ex)
        {
            Main.Logger.LogException("Steam Cloud unavailable; using local storage only", ex);
        }

        Main.Logger.Log("Using local file storage only");
        return new LocalBackend();
    }
}

// Stores files under the mod's folder on disk. Always the source of truth.
public sealed class LocalBackend : IStorageBackend
{
    private static string Root => Main.ModEntry.Path;

    private static string PathFor(string key) =>
        Path.Combine(Root, key.Replace('/', Path.DirectorySeparatorChar));

    public bool TryRead(string key, out string text)
    {
        string path = PathFor(key);
        if (File.Exists(path))
        {
            text = File.ReadAllText(path);
            return true;
        }

        text = "";
        return false;
    }

    public void Write(string key, string text)
    {
        string path = PathFor(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    public void Delete(string key)
    {
        string path = PathFor(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public IEnumerable<string> ListKeys(string prefix)
    {
        string dir = PathFor(prefix);
        if (!Directory.Exists(dir))
        {
            return Enumerable.Empty<string>();
        }

        return Directory.GetFiles(dir, "*.json").Select(f => prefix + Path.GetFileName(f));
    }
}

// Stores files in the app's Steam Cloud quota via ISteamRemoteStorage, namespaced under a prefix
// so they don't collide with any cloud files the game itself writes.
public sealed class CloudBackend : IStorageBackend
{
    private const string NS = "dvmodprofiles/";
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public bool TryRead(string key, out string text)
    {
        if (SteamRemoteStorage.FileExists(NS + key))
        {
            text = Utf8.GetString(SteamRemoteStorage.FileRead(NS + key));
            return true;
        }

        text = "";
        return false;
    }

    public void Write(string key, string text)
    {
        byte[] data = Utf8.GetBytes(text);
        if ((ulong)data.Length > SteamRemoteStorage.QuotaRemainingBytes && !SteamRemoteStorage.FileExists(NS + key))
        {
            Main.Logger.Warning($"Steam Cloud quota nearly full ({SteamRemoteStorage.QuotaRemainingBytes} bytes left); " +
                                 $"write of '{key}' ({data.Length} bytes) may fail.");
        }

        if (!SteamRemoteStorage.FileWrite(NS + key, data))
        {
            Main.Logger.Warning($"SteamRemoteStorage.FileWrite failed for '{key}'");
        }
    }

    public void Delete(string key)
    {
        if (SteamRemoteStorage.FileExists(NS + key))
        {
            SteamRemoteStorage.FileDelete(NS + key);
        }
    }

    public IEnumerable<string> ListKeys(string prefix) =>
        SteamRemoteStorage.Files
            .Where(f => f.StartsWith(NS + prefix, StringComparison.Ordinal))
            .Select(f => f.Substring(NS.Length));
}
