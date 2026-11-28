using NovaCraft.Services;

namespace NovaCraft.Tests;

public sealed class InstalledModServiceTests
{
    [Fact]
    public void ListsOnlyJarFilesAndMarksDisabledOnes()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var mods = Path.Combine(root, "mods");
            Directory.CreateDirectory(mods);
            File.WriteAllText(Path.Combine(mods, "sodium.jar"), "enabled-data");
            File.WriteAllText(Path.Combine(mods, "iris.jar.disabled"), "disabled-data");
            File.WriteAllText(Path.Combine(mods, "readme.txt"), "not-a-mod");

            var entries = InstalledModService.GetInstalledMods(root);

            Assert.Equal(2, entries.Count);
            Assert.Contains(entries, entry => entry.FileName == "sodium.jar" && entry.Enabled);
            Assert.Contains(entries, entry => entry.FileName == "iris.jar.disabled" && !entry.Enabled);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void DisablesAndReEnablesModWithoutDeletingItsContents()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var mods = Path.Combine(root, "mods");
            Directory.CreateDirectory(mods);
            var enabledPath = Path.Combine(mods, "sodium.jar");
            File.WriteAllText(enabledPath, "same-jar-bytes");

            var disabled = InstalledModService.SetEnabled(root, "sodium.jar", enabled: false);
            var disabledPath = Path.Combine(mods, "sodium.jar.disabled");

            Assert.False(disabled.Enabled);
            Assert.False(File.Exists(enabledPath));
            Assert.Equal("same-jar-bytes", File.ReadAllText(disabledPath));

            var enabled = InstalledModService.SetEnabled(root, "sodium.jar.disabled", enabled: true);

            Assert.True(enabled.Enabled);
            Assert.True(File.Exists(enabledPath));
            Assert.False(File.Exists(disabledPath));
            Assert.Equal("same-jar-bytes", File.ReadAllText(enabledPath));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RefusesToOverwriteAnExistingJarWhenReEnabling()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var mods = Path.Combine(root, "mods");
            Directory.CreateDirectory(mods);
            File.WriteAllText(Path.Combine(mods, "iris.jar.disabled"), "disabled-data");
            File.WriteAllText(Path.Combine(mods, "iris.jar"), "existing-data");

            Assert.Throws<IOException>(() =>
                InstalledModService.SetEnabled(root, "iris.jar.disabled", enabled: true));

            Assert.Equal("disabled-data", File.ReadAllText(Path.Combine(mods, "iris.jar.disabled")));
            Assert.Equal("existing-data", File.ReadAllText(Path.Combine(mods, "iris.jar")));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Theory]
    [InlineData("..\\outside.jar")]
    [InlineData("folder\\sodium.jar")]
    [InlineData("C:\\temp\\sodium.jar")]
    [InlineData("notes.txt")]
    public void RejectsPathsAndNonJarNames(string fileName)
    {
        var root = CreateTemporaryDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "mods"));
            Assert.Throws<InvalidDataException>(() =>
                InstalledModService.SetEnabled(root, fileName, enabled: false));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "novacraft-installed-mods-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }
}
