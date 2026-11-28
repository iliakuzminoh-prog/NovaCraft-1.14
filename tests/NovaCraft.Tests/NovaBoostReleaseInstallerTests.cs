using NovaCraft.Services;

namespace NovaCraft.Tests;

public sealed class NovaBoostReleaseInstallerTests
{
    private const string ValidManifest = """
    {
      "schemaVersion": 1,
      "id": "novaboost",
      "version": "0.1.0",
      "name": "NovaBoost",
      "environment": "client",
      "depends": {
        "fabricloader": ">=0.20.1",
        "minecraft": "~1.21.11",
        "java": ">=21",
        "fabric-api": "*"
      }
    }
    """;

    [Fact]
    public void AcceptsOnlyTheExpectedClientModMetadata()
    {
        Assert.Equal("0.1.0", NovaBoostReleaseInstaller.ValidateManifest(ValidManifest));
    }

    [Fact]
    public void RejectsWrongModId()
    {
        var manifest = ValidManifest.Replace("novaboost", "differentmod", StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => NovaBoostReleaseInstaller.ValidateManifest(manifest));
    }

    [Fact]
    public void RejectsWrongMinecraftVersion()
    {
        var manifest = ValidManifest.Replace("~1.21.11", "~1.21.10", StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => NovaBoostReleaseInstaller.ValidateManifest(manifest));
    }

    [Fact]
    public void RejectsServerOrUniversalJar()
    {
        var manifest = ValidManifest.Replace("\"environment\": \"client\"", "\"environment\": \"*\"", StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => NovaBoostReleaseInstaller.ValidateManifest(manifest));
    }

    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    [InlineData("0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF  NovaBoost.jar")]
    public void ParsesSha256Sidecar(string value)
    {
        Assert.Equal(
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            NovaBoostReleaseInstaller.ParseSha256(value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("0123456789abcdef")]
    public void RejectsInvalidSha256Sidecar(string value)
    {
        Assert.Throws<InvalidDataException>(() => NovaBoostReleaseInstaller.ParseSha256(value));
    }
}
