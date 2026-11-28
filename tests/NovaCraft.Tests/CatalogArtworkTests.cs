using System.Drawing;
using System.Text.Json.Nodes;
using NovaCraft;

namespace NovaCraft.Tests;

public sealed class CatalogArtworkTests
{
    [Fact]
    public void PrefersTheOfficialProjectIcon()
    {
        var project = JsonNode.Parse("""
        {
          "icon_url": "https://cdn.modrinth.com/data/ABC/icon.png",
          "featured_gallery": "https://cdn.modrinth.com/data/ABC/featured.png",
          "gallery": ["https://cdn.modrinth.com/data/ABC/gallery.png"]
        }
        """)!;

        Assert.Equal("https://cdn.modrinth.com/data/ABC/icon.png", MainForm.SelectProjectArtworkUrl(project));
    }

    [Fact]
    public void UsesFeaturedGalleryWhenProjectIconIsMissing()
    {
        var project = JsonNode.Parse("""
        {
          "icon_url": null,
          "featured_gallery": "https://cdn.modrinth.com/data/ABC/featured.png",
          "gallery": ["https://cdn.modrinth.com/data/ABC/gallery.png"]
        }
        """)!;

        Assert.Equal("https://cdn.modrinth.com/data/ABC/featured.png", MainForm.SelectProjectArtworkUrl(project));
    }

    [Fact]
    public void UsesFirstSafeGalleryImageAsLastArtworkFallback()
    {
        var project = JsonNode.Parse("""
        {
          "gallery": [
            "https://example.invalid/screenshot.png",
            "https://cdn.modrinth.com/data/ABC/gallery.png"
          ]
        }
        """)!;

        Assert.Equal("https://cdn.modrinth.com/data/ABC/gallery.png", MainForm.SelectProjectArtworkUrl(project));
    }

    [Theory]
    [InlineData("http://cdn.modrinth.com/data/ABC/icon.png")]
    [InlineData("https://cdn.modrinth.com.evil.invalid/data/ABC/icon.png")]
    [InlineData("https://user@cdn.modrinth.com/data/ABC/icon.png")]
    [InlineData("https://cdn.modrinth.com:444/data/ABC/icon.png")]
    public void RejectsUntrustedArtworkAddresses(string address)
    {
        var project = new JsonObject { ["icon_url"] = address };
        Assert.Null(MainForm.SelectProjectArtworkUrl(project));
    }

    [Fact]
    public void FallbackProjectArtworkIsAFullVoxelEmblem()
    {
        using var image = MainForm.CreateProjectFallbackIcon("Sodium Extra", "mod", 96);
        Assert.Equal(96, image.Width);
        Assert.Equal(96, image.Height);

        var sampledColors = new HashSet<int>();
        for (var y = 0; y < image.Height; y += 4)
        {
            for (var x = 0; x < image.Width; x += 4)
                sampledColors.Add(image.GetPixel(x, y).ToArgb());
        }

        Assert.True(sampledColors.Count > 24, $"Expected a detailed voxel fallback icon, got {sampledColors.Count} sampled colors.");
    }
}
