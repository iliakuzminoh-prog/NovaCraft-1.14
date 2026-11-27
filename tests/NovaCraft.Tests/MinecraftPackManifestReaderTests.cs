using System.IO.Compression;
using System.Text;
using NovaCraft.Services;

namespace NovaCraft.Tests;

public sealed class MinecraftPackManifestReaderTests
{
    [Fact]
    public void ReadsModrinthManifestAndExactForgeVersion()
    {
        var path = CreateArchive(
            ("modrinth.index.json",
             """
             {
               "formatVersion": 1,
               "game": "minecraft",
               "versionId": "pack-version-123",
               "name": "Forge Test Pack",
               "summary": "Test",
               "dependencies": {
                 "minecraft": "1.20.1",
                 "forge": "47.2.0"
               },
               "files": [
                 {
                   "path": "mods/example.jar",
                   "hashes": {
                     "sha512": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
                   },
                   "downloads": [
                     "https://example.invalid/file.jar",
                     "https://cdn.modrinth.com/data/example.jar"
                   ],
                   "fileSize": 1234
                 }
               ]
             }
             """));
        try
        {
            var pack = MinecraftPackManifestReader.Read(path);
            Assert.Equal("Forge Test Pack", pack.Name);
            Assert.Equal("1.20.1", pack.MinecraftVersion);
            Assert.Equal("forge", pack.Loader);
            Assert.Equal("47.2.0", pack.LoaderVersion);
            Assert.Equal("modrinth", pack.Platform);
            Assert.Single(pack.Files);
            Assert.Equal("https://cdn.modrinth.com/data/example.jar", pack.Files[0].DownloadUrl);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ReadsCurseForgeManifestAndLoaderVersion()
    {
        var path = CreateArchive(
            ("manifest.json",
             """
             {
               "manifestType": "minecraftModpack",
               "manifestVersion": 1,
               "name": "CF Pack",
               "version": "2.0",
               "author": "NovaCraft Tests",
               "overrides": "overrides",
               "minecraft": {
                 "version": "1.20.1",
                 "modLoaders": [
                   { "id": "forge-47.2.0", "primary": true }
                 ]
               },
               "files": [
                 { "projectID": 1234, "fileID": 5678, "required": true },
                 { "projectID": 9876, "fileID": 5432, "required": false }
               ]
             }
             """));
        try
        {
            var pack = MinecraftPackManifestReader.Read(path);
            Assert.Equal("CF Pack", pack.Name);
            Assert.Equal("1.20.1", pack.MinecraftVersion);
            Assert.Equal("forge", pack.Loader);
            Assert.Equal("47.2.0", pack.LoaderVersion);
            Assert.Equal("curseforge", pack.Platform);
            Assert.Single(pack.Files);
            Assert.Equal(1234, pack.Files[0].CurseForgeProjectId);
            Assert.Equal(5678, pack.Files[0].CurseForgeFileId);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("../outside.jar")]
    [InlineData("mods/../../outside.jar")]
    [InlineData("C:/Windows/system.ini")]
    [InlineData("/absolute/file.jar")]
    [InlineData("mods\\outside.jar")]
    public void RejectsUnsafeRelativePaths(string path)
    {
        Assert.Throws<InvalidDataException>(() =>
            MinecraftPackManifestReader.SafeDestination(Path.GetTempPath(), path));
    }

    [Fact]
    public void RejectsNonMinecraftArchive()
    {
        var path = CreateArchive(("manifest.json", """{"manifestType":"otherGame"}"""));
        try
        {
            Assert.Throws<InvalidDataException>(() => MinecraftPackManifestReader.Read(path));
        }
        finally { File.Delete(path); }
    }

    private static string CreateArchive(params (string Name, string Content)[] entries)
    {
        var path = Path.Combine(Path.GetTempPath(), "novacraft-pack-" + Guid.NewGuid().ToString("N") + ".zip");
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
}
