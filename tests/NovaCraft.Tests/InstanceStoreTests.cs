using NovaCraft.Services;

namespace NovaCraft.Tests;

public sealed class InstanceStoreTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "NovaCraftTests", Guid.NewGuid().ToString("N"));

    public InstanceStoreTests() => Directory.CreateDirectory(root);

    [Fact]
    public void DifferentLoadersGetDifferentGameFolders()
    {
        var store = new InstanceStore(root);
        var fabric = new InstanceInfo
        {
            Id = "fabric-1-21-1",
            Name = "Fabric",
            MinecraftVersion = "1.21.1",
            Loader = "fabric"
        };
        var forge = new InstanceInfo
        {
            Id = "forge-1-21-1",
            Name = "Forge",
            MinecraftVersion = "1.21.1",
            Loader = "forge"
        };

        store.Save(fabric);
        store.Save(forge);

        var fabricMods = Path.Combine(store.GameDir(fabric), "mods");
        var forgeMods = Path.Combine(store.GameDir(forge), "mods");

        Assert.NotEqual(
            Path.GetFullPath(fabricMods),
            Path.GetFullPath(forgeMods));
        Assert.DoesNotContain(
            Path.DirectorySeparatorChar + "forge-" ,
            Path.GetFullPath(fabricMods),
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            Path.DirectorySeparatorChar + "fabric-",
            Path.GetFullPath(forgeMods),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SharedStorageIsIdenticalAcrossInstancesButGameFoldersAreNot()
    {
        var store = new InstanceStore(root);
        var first = new InstanceInfo
        {
            Id = "fabric-one",
            Name = "First",
            MinecraftVersion = "1.21.1",
            Loader = "fabric",
            UsesSharedStorage = true
        };
        var second = new InstanceInfo
        {
            Id = "forge-one",
            Name = "Second",
            MinecraftVersion = "1.21.1",
            Loader = "forge",
            UsesSharedStorage = true
        };

        var firstPath = store.CreateMinecraftPath(first);
        var secondPath = store.CreateMinecraftPath(second);

        Assert.NotEqual(first.BasePath, second.BasePath);
        Assert.Equal(firstPath.Library, secondPath.Library);
        Assert.Equal(firstPath.Versions, secondPath.Versions);
        Assert.Equal(firstPath.Assets, secondPath.Assets);
        Assert.Equal(firstPath.Runtime, secondPath.Runtime);
    }

    [Fact]
    public void LegacyMigrationAdoptsWithoutMovingOrCopyingUserFiles()
    {
        var store = new InstanceStore(root);
        var legacy = Path.Combine(root, "legacy-game");
        Directory.CreateDirectory(legacy);
        var userFile = Path.Combine(legacy, "options.txt");
        File.WriteAllText(userFile, "keep-me");

        var info = store.MigrateLegacy(legacy, "1.21.1", "vanilla");

        Assert.NotNull(info);
        Assert.False(info!.UsesSharedStorage);
        Assert.Equal(Path.GetFullPath(legacy), Path.GetFullPath(info.CustomGameDir!));
        Assert.True(File.Exists(userFile));
        Assert.Equal("keep-me", File.ReadAllText(userFile));
        Assert.Empty(Directory.GetDirectories(store.InstancesDir, "*", SearchOption.AllDirectories)
            .Where(path => !string.Equals(Path.GetFullPath(path), Path.GetFullPath(legacy), StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void InvalidInstanceIdsCannotEscapeInstancesDirectory()
    {
        var store = new InstanceStore(root);

        Assert.False(InstanceStore.IsValidId("../escape"));
        Assert.False(InstanceStore.IsValidId(".."));
        Assert.False(InstanceStore.IsValidId("CON"));
        Assert.Throws<ArgumentException>(() => store.InstanceDir("../escape"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
        catch
        {
            // Best-effort cleanup only; test assertions are independent of cleanup.
        }
    }
}
