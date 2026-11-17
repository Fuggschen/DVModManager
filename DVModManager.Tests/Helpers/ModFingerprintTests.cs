using DVModManager.Helpers;
using Xunit;

namespace DVModManager.Tests.Helpers;

public class ModFingerprintTests
{
    [Fact]
    public void Compute_TakesThreeLargestFilesAndSkipsConfigFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), "Fp_" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(dir, "sub"));
        File.WriteAllBytes(Path.Combine(dir, "Mod.dll"), new byte[300]);
        File.WriteAllBytes(Path.Combine(dir, "sub", "ccl_bundle"), new byte[500]);
        File.WriteAllBytes(Path.Combine(dir, "small.png"), new byte[10]);
        File.WriteAllBytes(Path.Combine(dir, "tiny.wav"), new byte[5]);
        File.WriteAllBytes(Path.Combine(dir, "Settings.xml"), new byte[9000]);
        File.WriteAllBytes(Path.Combine(dir, "shaders.cache"), new byte[9000]);
        File.WriteAllBytes(Path.Combine(dir, "placeholder.json"), new byte[9000]);

        try
        {
            var files = ModFingerprint.Compute(dir);

            Assert.Equal(
                [new FingerprintFile("ccl_bundle", 500), new FingerprintFile("Mod.dll", 300), new FingerprintFile("small.png", 10)],
                files);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Compute_MissingFolder_IsEmpty()
    {
        Assert.Empty(ModFingerprint.Compute(Path.Combine(Path.GetTempPath(), "Missing_" + Guid.NewGuid())));
    }

    [Fact]
    public void EncodeDecode_RoundTripsAndIsOpaque()
    {
        IReadOnlyList<FingerprintFile> files = [new("ccl_bundle", 22448805), new("Class 750 Ä.png", 12)];

        var encoded = ModFingerprint.Encode(files);

        Assert.NotNull(encoded);
        Assert.DoesNotContain("ccl_bundle", encoded);
        Assert.DoesNotContain("=", encoded);
        Assert.Equal(files, ModFingerprint.Decode(encoded));
    }

    [Fact]
    public void Encode_NoFiles_IsNull()
    {
        Assert.Null(ModFingerprint.Encode([]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not base64 !!")]
    [InlineData("djI")]            // "v2": unknown version
    [InlineData("djEKYWJjCW5hbWU")] // "v1\nabc\tname": size is not a number
    public void Decode_BadInput_IsEmpty(string? value)
    {
        Assert.Empty(ModFingerprint.Decode(value));
    }
}
