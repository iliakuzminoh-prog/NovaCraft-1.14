using NovaCraft.Services;

namespace NovaCraft.Tests;

public sealed class OfflineProfileRulesTests
{
    [Fact]
    public void OfflineUuidMatchesJavaNameUuidFromBytes()
    {
        var uuid = OfflineProfileRules.OfflineUuid("NovaPlayer");
        Assert.Equal("a807ad4d-ba2f-3b9e-af4f-7e553fc44e6e", uuid);
    }

    [Fact]
    public void OfflineUuidHasVersionAndVariantBits()
    {
        var uuid = OfflineProfileRules.OfflineUuid("Test_Player");
        Assert.Equal(36, uuid.Length);
        Assert.Equal('3', uuid[14]);
        Assert.Contains(char.ToLowerInvariant(uuid[19]), new[] { '8', '9', 'a', 'b' });
    }

    [Fact]
    public void OfflineUuidIsDeterministic()
    {
        Assert.Equal(
            OfflineProfileRules.OfflineUuid("Player_123"),
            OfflineProfileRules.OfflineUuid("Player_123"));
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("русский")]
    [InlineData("player-name")]
    [InlineData("player name")]
    public void NickValidatorRejectsInvalidNames(string name)
    {
        Assert.False(NickValidator.Validate(name).ok);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("Player_123")]
    [InlineData("A")]
    public void NickValidatorAcceptsValidNamesExceptTooShort(string name)
    {
        var result = NickValidator.Validate(name);
        Assert.Equal(name.Length >= 3, result.ok);
    }
    [Fact]
    public void LaunchUsernameComesFromValidatedProfileValue()
    {
        const string activeProfileUsername = "ProfileOwner";
        Assert.Equal(activeProfileUsername,
            OfflineLaunchProfileRules.GetValidatedUsernameForLaunch(activeProfileUsername));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bad name")]
    [InlineData("name-with-dash")]
    public void LaunchUsernameRejectsInvalidProfileValue(string? profileUsername)
    {
        Assert.Throws<ArgumentException>(() =>
            OfflineLaunchProfileRules.GetValidatedUsernameForLaunch(profileUsername));
    }

}
