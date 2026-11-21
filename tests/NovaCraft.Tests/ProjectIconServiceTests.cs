using System.Drawing.Imaging;
using NovaCraft.Services;

namespace NovaCraft.Tests;

public sealed class ProjectIconServiceTests
{
    // Крошечные WebP (lossless и lossy 1x1): именно такие форматы GDI+ не открывает.
    const string LosslessWebpBase64 = "UklGRhoAAABXRUJQVlA4TA0AAAAvAAAAEAcQERGIiP4HAA==";
    const string LossyWebpBase64 = "UklGRiIAAABXRUJQVlA4IBYAAAAwAQCdASoBAAEADsD+JaQAA3AAAAAA";

    static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    [Theory]
    [InlineData(LosslessWebpBase64)]
    [InlineData(LossyWebpBase64)]
    public void WebpIsConvertedToPngThatGdiCanRead(string base64)
    {
        var png = ProjectIconService.ConvertToPng(Convert.FromBase64String(base64));

        Assert.True(png.Length > PngSignature.Length);
        Assert.Equal(PngSignature, png.Take(PngSignature.Length).ToArray());

        using var bitmap = ProjectIconService.DecodeWithGdi(png);
        Assert.NotNull(bitmap);
        Assert.Equal(1, bitmap!.Width);
        Assert.Equal(1, bitmap.Height);
    }

    [Fact]
    public void PngIsDecodedDirectlyByGdi()
    {
        using var source = new Bitmap(8, 8);
        using var stream = new MemoryStream();
        source.Save(stream, ImageFormat.Png);

        using var bitmap = ProjectIconService.DecodeWithGdi(stream.ToArray());

        Assert.NotNull(bitmap);
        Assert.Equal(8, bitmap!.Width);
    }

    [Fact]
    public void GarbageIsRejectedWithoutCrashing()
    {
        Assert.Null(ProjectIconService.DecodeWithGdi(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));
        Assert.Null(ProjectIconService.DecodeWithGdi(Array.Empty<byte>()));
        Assert.ThrowsAny<Exception>(() => ProjectIconService.ConvertToPng(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));
    }

    [Fact]
    public void CachePathIsStablePerUrlAndDiffersBetweenUrls()
    {
        var a1 = ProjectIconService.CachePathFor(new Uri("https://cdn.modrinth.com/data/aaa/icon.webp"));
        var a2 = ProjectIconService.CachePathFor(new Uri("https://cdn.modrinth.com/data/aaa/icon.webp"));
        var b = ProjectIconService.CachePathFor(new Uri("https://cdn.modrinth.com/data/bbb/icon.webp"));

        Assert.Equal(a1, a2);
        Assert.NotEqual(a1, b);
    }

    [Theory]
    [InlineData("Sodium")]
    [InlineData("Мод на русском")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("!!!")]
    public void PlaceholderIsAlwaysProduced(string? title)
    {
        using var bitmap = ProjectIconPlaceholder.Create(title, 64);

        Assert.Equal(64, bitmap.Width);
        Assert.Equal(64, bitmap.Height);
    }
}
