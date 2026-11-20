using NovaCraft.Builds;

namespace NovaCraft.Tests;

public sealed class BuildProfileStoreTests
{
    [Fact]
    public void Duplicate_CreatesNewId_AndPreservesBuildSettings()
    {
        using var temp = new TemporaryDirectory();
        var store = new BuildProfileStore(Path.Combine(temp.Path, "builds.json"));
        var builds = new List<BuildProfile>
        {
            new()
            {
                Id = "main",
                Name = "Основная",
                MinecraftVersion = "1.21.1",
                Loader = "fabric",
                GameDirectory = Path.Combine(temp.Path, "main"),
                MaximumRamMb = 4096,
                FpsBoost = true,
                Favorite = true
            }
        };

        var copy = store.Duplicate(builds, "main");

        Assert.NotEqual("main", copy.Id);
        Assert.Equal("Основная — копия", copy.Name);
        Assert.Equal("1.21.1", copy.MinecraftVersion);
        Assert.Equal("fabric", copy.Loader);
        Assert.Equal(builds[0].GameDirectory, copy.GameDirectory);
        Assert.Equal(4096, copy.MaximumRamMb);
        Assert.True(copy.FpsBoost);
        Assert.False(copy.Favorite);
    }

    [Fact]
    public void SaveAndLoad_PreservesProfiles()
    {
        using var temp = new TemporaryDirectory();
        var store = new BuildProfileStore(Path.Combine(temp.Path, "builds.json"));
        var source = new[]
        {
            new BuildProfile
            {
                Id = "fabric-1211",
                Name = "Fabric 1.21.1",
                MinecraftVersion = "1.21.1",
                Loader = "fabric",
                GameDirectory = Path.Combine(temp.Path, "fabric"),
                MaximumRamMb = 3072
            }
        };

        store.Save(source);
        var loaded = store.Load();

        var build = Assert.Single(loaded);
        Assert.Equal("fabric-1211", build.Id);
        Assert.Equal("Fabric 1.21.1", build.Name);
        Assert.Equal("fabric", build.Loader);
        Assert.Equal(3072, build.MaximumRamMb);
    }

    [Fact]
    public void Add_RejectsDuplicateIds()
    {
        using var temp = new TemporaryDirectory();
        var store = new BuildProfileStore(Path.Combine(temp.Path, "builds.json"));
        var builds = new List<BuildProfile>
        {
            new() { Id = "same", Name = "A", MinecraftVersion = "1.21.1" }
        };

        Assert.Throws<InvalidOperationException>(() =>
            store.Add(builds, new BuildProfile
            {
                Id = "same",
                Name = "B",
                MinecraftVersion = "1.21.1"
            }));
    }

    [Fact]
    public void InvalidLoader_IsRejected()
    {
        using var temp = new TemporaryDirectory();
        var store = new BuildProfileStore(Path.Combine(temp.Path, "builds.json"));
        var builds = new List<BuildProfile>();

        Assert.Throws<ArgumentException>(() =>
            store.Add(builds, new BuildProfile
            {
                Id = "bad",
                Name = "Bad",
                MinecraftVersion = "1.21.1",
                Loader = "unknown"
            }));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "NovaCraftTests", Guid.NewGuid().ToString("N"));

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try { Directory.Delete(Path, true); } catch { }
        }
    }
}
