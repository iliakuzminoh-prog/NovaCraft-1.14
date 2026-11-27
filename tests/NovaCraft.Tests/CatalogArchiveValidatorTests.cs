using System.IO.Compression;
using System.Text;
using NovaCraft.Services;

namespace NovaCraft.Tests;

public sealed class CatalogArchiveValidatorTests
{
    [Fact]
    public void AcceptsValidResourcePack()
    {
        var path = CreateArchive(("pack.mcmeta", Metadata), ("assets/minecraft/lang/en_us.json", "{}"));
        try { CatalogArchiveValidator.Validate(path, "resourcepack"); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AcceptsValidDataPack()
    {
        var path = CreateArchive(("pack.mcmeta", Metadata), ("data/example/functions/load.mcfunction", "say hello"));
        try { CatalogArchiveValidator.Validate(path, "datapack"); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AcceptsShaderPackWithRootShadersFolder()
    {
        var path = CreateArchive(("shaders/program/test.vsh", "// shader"));
        try { CatalogArchiveValidator.Validate(path, "shader"); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RejectsArchiveWithoutRequiredResourcePackLayout()
    {
        var path = CreateArchive(("pack.mcmeta", Metadata), ("textures/gui/icon.png", "not-a-real-image"));
        try { Assert.Throws<InvalidDataException>(() => CatalogArchiveValidator.Validate(path, "resourcepack")); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RejectsArchiveWithoutRequiredDataPackLayout()
    {
        var path = CreateArchive(("pack.mcmeta", Metadata), ("assets/example/file.txt", "wrong folder"));
        try { Assert.Throws<InvalidDataException>(() => CatalogArchiveValidator.Validate(path, "datapack")); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RejectsPathTraversal()
    {
        var path = CreateArchive(("pack.mcmeta", Metadata), ("data/../outside.txt", "unsafe"));
        try { Assert.Throws<InvalidDataException>(() => CatalogArchiveValidator.Validate(path, "datapack")); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RejectsInvalidMetadata()
    {
        var path = CreateArchive(("pack.mcmeta", "{\"wrong\":true}"), ("data/example/file.txt", "x"));
        try { Assert.Throws<InvalidDataException>(() => CatalogArchiveValidator.Validate(path, "datapack")); }
        finally { File.Delete(path); }
    }

    private const string Metadata = "{\"pack\":{\"pack_format\":34,\"description\":\"NovaCraft test\"}}";

    private static string CreateArchive(params (string Name, string Content)[] entries)
    {
        var path = Path.Combine(Path.GetTempPath(), "novacraft-" + Guid.NewGuid().ToString("N") + ".zip");
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
