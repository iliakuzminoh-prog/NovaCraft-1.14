using System.IO.Compression;
using System.Text;
using NovaCraft.Services;

namespace NovaCraft.Tests;

public sealed class MinecraftPackOverrideTests
{
    [Fact]
    public async Task ExtractsOverrideAndClosesTempFileBeforeMove()
    {
        var archivePath = CreateArchive(
            ("overrides/config/novacraft/test.properties", "quality=verified"));
        var destination = CreateTempDirectory();

        try
        {
            using (var file = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Read))
            {
                await MinecraftPackImportService.ApplyOverridesAsync(
                    archive,
                    destination,
                    "overrides/",
                    CancellationToken.None);
            }

            var installed = Path.Combine(destination, "config", "novacraft", "test.properties");
            Assert.Equal("quality=verified", await File.ReadAllTextAsync(installed));
            Assert.Empty(Directory.GetFiles(destination, "*.novacraft-import", SearchOption.AllDirectories));
        }
        finally
        {
            File.Delete(archivePath);
            Directory.Delete(destination, recursive: true);
        }
    }

    [Fact]
    public async Task RejectsDuplicateOverridePaths()
    {
        var archivePath = CreateArchive(
            ("overrides/config/test.cfg", "first"),
            ("overrides/config/test.cfg", "second"));
        var destination = CreateTempDirectory();

        try
        {
            using var file = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var archive = new ZipArchive(file, ZipArchiveMode.Read);

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                MinecraftPackImportService.ApplyOverridesAsync(
                    archive,
                    destination,
                    "overrides/",
                    CancellationToken.None));

            Assert.False(File.Exists(Path.Combine(destination, "config", "test.cfg")));
        }
        finally
        {
            File.Delete(archivePath);
            Directory.Delete(destination, recursive: true);
        }
    }

    [Fact]
    public async Task RejectsPathTraversalInOverrideArchive()
    {
        var archivePath = CreateArchive(("overrides/../outside.cfg", "unsafe"));
        var destination = CreateTempDirectory();

        try
        {
            using var file = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var archive = new ZipArchive(file, ZipArchiveMode.Read);

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                MinecraftPackImportService.ApplyOverridesAsync(
                    archive,
                    destination,
                    "overrides/",
                    CancellationToken.None));

            Assert.Empty(Directory.GetFiles(destination, "*", SearchOption.AllDirectories));
        }
        finally
        {
            File.Delete(archivePath);
            Directory.Delete(destination, recursive: true);
        }
    }

    private static string CreateArchive(params (string Name, string Content)[] entries)
    {
        var path = Path.Combine(Path.GetTempPath(), "novacraft-overrides-" + Guid.NewGuid().ToString("N") + ".zip");
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            var entry = archive.CreateEntry(name);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(content);
        }
        return path;
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "novacraft-overrides-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
