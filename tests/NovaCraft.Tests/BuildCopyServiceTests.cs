using System.IO.Compression;
using NovaCraft.Services;

namespace NovaCraft.Tests;

public sealed class BuildCopyServiceTests
{
    [Fact]
    public void CopiesOnlySelectedContentAndLeavesSourceUntouched()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var source = Path.Combine(root, "source");
            var destination = Path.Combine(root, "copy");
            WriteFile(source, "mods", "example.jar", "mod-data");
            WriteFile(source, "config", "example.json", "config-data");
            WriteFile(source, "saves", "MyWorld", "level.dat", "world-data");
            WriteFile(source, "resourcepacks", "pack.zip", "pack-data");
            File.WriteAllText(Path.Combine(source, "options.txt"), "options-data");
            File.WriteAllText(Path.Combine(source, "servers.dat"), "server-data");

            var result = BuildCopyService.CopySelectedContent(
                source,
                destination,
                new BuildCopyOptions
                {
                    IncludeMods = true,
                    IncludeConfig = false,
                    IncludeWorlds = true,
                    IncludeGameSettings = true,
                    IncludeResourcePacks = false,
                    IncludeShaderPacks = false,
                    IncludeScreenshots = false
                });

            Assert.Equal(4, result.FilesCopied);
            Assert.True(File.Exists(Path.Combine(destination, "mods", "example.jar")));
            Assert.True(File.Exists(Path.Combine(destination, "saves", "MyWorld", "level.dat")));
            Assert.True(File.Exists(Path.Combine(destination, "options.txt")));
            Assert.True(File.Exists(Path.Combine(destination, "servers.dat")));
            Assert.False(Directory.Exists(Path.Combine(destination, "config")));
            Assert.False(Directory.Exists(Path.Combine(destination, "resourcepacks")));
            Assert.Equal("mod-data", File.ReadAllText(Path.Combine(source, "mods", "example.jar")));
            Assert.Equal("world-data", File.ReadAllText(Path.Combine(source, "saves", "MyWorld", "level.dat")));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RejectsDestinationInsideSourceBeforeCreatingIt()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var source = Path.Combine(root, "source");
            Directory.CreateDirectory(source);
            WriteFile(source, "mods", "example.jar", "mod-data");
            var destination = Path.Combine(source, "nested-copy");

            Assert.Throws<InvalidOperationException>(() =>
                BuildCopyService.CopySelectedContent(source, destination, new BuildCopyOptions()));

            Assert.False(Directory.Exists(destination));
            Assert.Equal("mod-data", File.ReadAllText(Path.Combine(source, "mods", "example.jar")));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void DoesNotOverwriteAnExistingDestination()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var source = Path.Combine(root, "source");
            var destination = Path.Combine(root, "destination");
            WriteFile(source, "mods", "example.jar", "mod-data");
            Directory.CreateDirectory(destination);
            File.WriteAllText(Path.Combine(destination, "keep.txt"), "keep");

            Assert.Throws<IOException>(() =>
                BuildCopyService.CopySelectedContent(source, destination, new BuildCopyOptions()));

            Assert.Equal("keep", File.ReadAllText(Path.Combine(destination, "keep.txt")));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void CreatesWorldBackupWithoutChangingTheOriginalWorlds()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var gameDirectory = Path.Combine(root, "instance");
            var worldFile = Path.Combine(gameDirectory, "saves", "World One", "level.dat");
            Directory.CreateDirectory(Path.GetDirectoryName(worldFile)!);
            File.WriteAllText(worldFile, "world-data");

            var backup = BuildCopyService.CreateWorldBackup(gameDirectory);

            Assert.True(File.Exists(backup.ArchivePath));
            Assert.Equal(1, backup.FilesCopied);
            using var archive = ZipFile.OpenRead(backup.ArchivePath);
            var entry = Assert.Single(archive.Entries);
            Assert.Equal("saves/World One/level.dat", entry.FullName);
            using var reader = new StreamReader(entry.Open());
            Assert.Equal("world-data", reader.ReadToEnd());
            Assert.Equal("world-data", File.ReadAllText(worldFile));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RejectsAnEmptyWorldFolder()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "saves"));
            Assert.Throws<InvalidOperationException>(() => BuildCopyService.CreateWorldBackup(root));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "novacraft-copy-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WriteFile(string root, string directory, string fileName, string content)
    {
        var file = Path.Combine(root, directory, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, content);
    }

    private static void WriteFile(string root, string directory, string subDirectory, string fileName, string content)
    {
        var file = Path.Combine(root, directory, subDirectory, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, content);
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
