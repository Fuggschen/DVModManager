using DVModManager.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DVModManager.Tests.Helpers;

public class DoorstopInstallerTests : IDisposable
{
    private readonly string _workDir = Path.Combine(Path.GetTempPath(), "Doorstop_" + Guid.NewGuid().ToString("N"));

    public DoorstopInstallerTests()
    {
        Directory.CreateDirectory(_workDir);
        Directory.CreateDirectory(PayloadDir);
        File.WriteAllBytes(Path.Combine(PayloadDir, "winhttp_x64.dll"), "proxy64"u8.ToArray());
        File.WriteAllBytes(Path.Combine(PayloadDir, "winhttp_x86.dll"), "proxy32"u8.ToArray());
    }

    private string PayloadDir => Path.Combine(_workDir, "payload");

    public void Dispose() => TestData.DeleteDirectory(_workDir);

    /// <summary>Crafts a minimal PE image with the given machine type.</summary>
    private static byte[] CreatePeStub(ushort machine)
    {
        var bytes = new byte[256];
        bytes[0x3c] = 0x80;                    // e_lfanew = 0x80
        bytes[0x80] = (byte)'P';
        bytes[0x81] = (byte)'E';
        bytes[0x84] = (byte)(machine & 0xFF);  // machine (low byte)
        bytes[0x85] = (byte)(machine >> 8);
        return bytes;
    }

    [Fact]
    public void ReadPeIs64Bit_ReadsMachineType()
    {
        var x64 = Path.Combine(_workDir, "x64.bin");
        var x86 = Path.Combine(_workDir, "x86.bin");
        var junk = Path.Combine(_workDir, "junk.bin");
        File.WriteAllBytes(x64, CreatePeStub(0x8664));
        File.WriteAllBytes(x86, CreatePeStub(0x14c));
        File.WriteAllBytes(junk, "not a pe"u8.ToArray());

        Assert.True(DoorstopInstaller.ReadPeIs64Bit(x64));
        Assert.False(DoorstopInstaller.ReadPeIs64Bit(x86));
        Assert.Null(DoorstopInstaller.ReadPeIs64Bit(junk));
        Assert.Null(DoorstopInstaller.ReadPeIs64Bit(Path.Combine(_workDir, "missing.bin")));
    }

    [Fact]
    public void Install_SelectsProxyByGameBitness_AndWritesUmmConfig()
    {
        var gamePath = TestData.CreateGameDirectory(_workDir);
        // 32-bit game exe → the x86 proxy must be picked.
        File.WriteAllBytes(Path.Combine(gamePath, "DerailValley.exe"), CreatePeStub(0x14c));

        DoorstopInstaller.Install(gamePath, PayloadDir, @"DerailValley_Data\Managed\UnityModManager\UnityModManager.dll",
            logger: NullLogger.Instance);

        Assert.Equal("proxy32", File.ReadAllText(DoorstopInstaller.GetDoorstopPath(gamePath)));
        var ini = File.ReadAllText(DoorstopInstaller.GetConfigPath(gamePath));
        Assert.Contains("[General]", ini);
        Assert.Contains("enabled = true", ini);
        Assert.Contains(@"target_assembly = DerailValley_Data\Managed\UnityModManager\UnityModManager.dll", ini);
    }

    [Fact]
    public void Install_MissingProxyInPayload_Throws()
    {
        var gamePath = TestData.CreateGameDirectory(_workDir);
        var emptyPayload = Path.Combine(_workDir, "empty");
        Directory.CreateDirectory(emptyPayload);

        Assert.Throws<InvalidOperationException>(() =>
            DoorstopInstaller.Install(gamePath, emptyPayload, "loader.dll", is64Bit: true));
    }

    [Fact]
    public void Remove_DeletesBothFiles()
    {
        var gamePath = TestData.CreateGameDirectory(_workDir);
        DoorstopInstaller.Install(gamePath, PayloadDir, "loader.dll", is64Bit: true);
        Assert.True(DoorstopInstaller.IsInstalled(gamePath));

        DoorstopInstaller.Remove(gamePath);

        Assert.False(Directory.Exists(gamePath) && DoorstopInstaller.IsInstalled(gamePath));
        Assert.False(File.Exists(DoorstopInstaller.GetDoorstopPath(gamePath)));
        Assert.False(File.Exists(DoorstopInstaller.GetConfigPath(gamePath)));
    }
}
