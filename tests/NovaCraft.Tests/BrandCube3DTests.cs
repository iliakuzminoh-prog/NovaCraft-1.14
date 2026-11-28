using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using NovaCraft.UI;

namespace NovaCraft.Tests;

public sealed class BrandCube3DTests
{
    [Fact]
    public void RotatesAndRendersAVisibleThreeDimensionalMark()
    {
        const int previewSize = 160;
        using var cube = new BrandCube3D { Size = new Size(previewSize, previewSize) };
        using var initial = new Bitmap(previewSize, previewSize);
        using var rotated = new Bitmap(previewSize, previewSize);

        using (var graphics = Graphics.FromImage(initial))
            cube.RenderPreview(graphics);

        cube.AdvanceFrame(0.7f);

        using (var graphics = Graphics.FromImage(rotated))
            cube.RenderPreview(graphics);

        var distinctColors = new HashSet<int>();
        var changedPixels = 0;
        for (var y = 0; y < previewSize; y++)
        {
            for (var x = 0; x < previewSize; x++)
            {
                var before = initial.GetPixel(x, y).ToArgb();
                var after = rotated.GetPixel(x, y).ToArgb();
                distinctColors.Add(after);
                if (before != after)
                    changedPixels++;
            }
        }

        var previewPath = Path.Combine(AppContext.BaseDirectory, "NovaCraft-Brand-Preview.png");
        rotated.Save(previewPath, ImageFormat.Png);

        Assert.True(distinctColors.Count > 10, $"Expected shaded cube faces, got {distinctColors.Count} colors.");
        Assert.True(changedPixels > 100, $"Expected rotation to change the cube projection, got {changedPixels} pixels.");
        Assert.Equal(AccessibleRole.Graphic, cube.AccessibleRole);
    }

    [Fact]
    public void IgnoresInvalidRotationDeltas()
    {
        using var cube = new BrandCube3D { Size = new Size(38, 38) };
        cube.AdvanceFrame(float.NaN);
        cube.AdvanceFrame(float.PositiveInfinity);
        cube.AdvanceFrame(-1f);
    }
}
