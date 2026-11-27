using NovaCraft.Services;

namespace NovaCraft.Tests;

public sealed class MusicPlaylistRulesTests
{
    [Theory]
    [InlineData("song.mp3")]
    [InlineData("song.MP3")]
    [InlineData("background.wav")]
    [InlineData("background.WAV")]
    public void AcceptsMp3AndWav(string fileName)
    {
        Assert.True(MusicPlaylistRules.IsSupported(fileName));
        Assert.EndsWith(fileName, MusicPlaylistRules.NormalizePath(Path.Combine(Path.GetTempPath(), fileName)), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("song.ogg")]
    [InlineData("song.exe")]
    [InlineData("song.mp4")]
    [InlineData("")]
    public void RejectsUnsupportedExtensions(string fileName)
    {
        Assert.False(MusicPlaylistRules.IsSupported(fileName));
        Assert.Throws<InvalidDataException>(() => MusicPlaylistRules.NormalizePath(Path.Combine(Path.GetTempPath(), fileName)));
    }

    [Fact]
    public void NormalizesPlaylistAndRemovesDuplicatesCaseInsensitively()
    {
        var path = Path.Combine(Path.GetTempPath(), "NovaCraft-" + Guid.NewGuid().ToString("N") + ".mp3");
        var normalized = MusicPlaylistRules.Normalize(new[] { path, path.ToUpperInvariant(), "bad.ogg", "", path });

        Assert.Single(normalized);
        Assert.Equal(Path.GetFullPath(path), normalized[0]);
    }

    [Fact]
    public void RequiresFileToExistBeforePlayback()
    {
        var path = Path.Combine(Path.GetTempPath(), "missing-" + Guid.NewGuid().ToString("N") + ".wav");
        Assert.Throws<FileNotFoundException>(() => MusicPlaylistRules.ValidateExistingFile(path));
    }
}
