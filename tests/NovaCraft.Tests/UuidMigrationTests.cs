using NovaCraft.Services;

namespace NovaCraft.Tests;

public sealed class UuidMigrationTests
{
    // new Guid(MD5("OfflinePlayer:NovaPlayer")).ToString(), the value earlier builds stored.
    // Computed independently with Python: uuid.UUID(bytes_le=md5(...)).
    const string LegacyNovaPlayer = "4dad07a8-2fba-9e0b-2f4f-7e553fc44e6e";

    [Fact]
    public void LegacyGuidUuidMatchesWhatEarlierBuildsStored()
    {
        Assert.Equal(LegacyNovaPlayer, OfflineProfileRules.LegacyGuidUuid("NovaPlayer"));
    }

    [Fact]
    public void LegacyUuidIsFlaggedForReplacement()
    {
        Assert.True(OfflineProfileRules.IsLegacyOrInvalidUuid("NovaPlayer", LegacyNovaPlayer));
        Assert.True(OfflineProfileRules.IsLegacyOrInvalidUuid("NovaPlayer", LegacyNovaPlayer.ToUpperInvariant()));
    }

    [Fact]
    public void CanonicalUuidIsKept()
    {
        var canonical = OfflineProfileRules.OfflineUuid("NovaPlayer");
        Assert.False(OfflineProfileRules.IsLegacyOrInvalidUuid("NovaPlayer", canonical));
    }

    [Fact]
    public void NameBasedUuidOfAnotherNicknameIsKeptAfterRenameWithKeepUuid()
    {
        var oldUuid = OfflineProfileRules.OfflineUuid("OldNick");
        Assert.False(OfflineProfileRules.IsLegacyOrInvalidUuid("NewNick", oldUuid));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-uuid")]
    [InlineData("a807ad4dba2f3b9eaf4f7e553fc44e6e")] // no dashes
    [InlineData("3f2504e0-4f89-41d3-9a0c-0305e82c3301")] // random v4, the old default value
    public void MissingOrNonNameBasedUuidIsFlagged(string? uuid)
    {
        Assert.True(OfflineProfileRules.IsLegacyOrInvalidUuid("NovaPlayer", uuid));
    }

    [Fact]
    public void ControlValueFromJavaStillMatches()
    {
        Assert.Equal("a807ad4d-ba2f-3b9e-af4f-7e553fc44e6e", OfflineProfileRules.OfflineUuid("NovaPlayer"));
    }

    [Theory]
    [InlineData("a807ad4d-ba2f-3b9e-af4f-7e553fc44e6e", true)]
    [InlineData("a807ad4d-ba2f-4b9e-af4f-7e553fc44e6e", false)] // version 4
    [InlineData("a807ad4d-ba2f-3b9e-cf4f-7e553fc44e6e", false)] // variant c
    [InlineData("A807AD4D-BA2F-3B9E-AF4F-7E553FC44E6E", true)]
    public void HasNameBasedUuidFormatChecksVersionAndVariant(string uuid, bool expected)
    {
        Assert.Equal(expected, OfflineProfileRules.HasNameBasedUuidFormat(uuid));
    }
}
