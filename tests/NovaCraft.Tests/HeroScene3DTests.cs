using System.Windows.Forms;
using System.Drawing;
using System.Drawing.Imaging;
using NovaCraft.UI;

namespace NovaCraft.Tests;

public sealed class HeroScene3DTests
{
    [Theory]
    [InlineData(960, 270)]
    [InlineData(620, 250)]
    [InlineData(520, 320)]
    public void RendersAnimatedHeroAtMultipleAspectRatios(int width, int height)
    {
        using var hero = new HeroScene3D
        {
            Size = new Size(width, height),
            GameVersion = "1.21.1",
            ProfileName = "TEST PROFILE"
        };
        hero.CreateControl();

        using var frame = new Bitmap(width, height);
        hero.DrawToBitmap(frame, new Rectangle(0, 0, width, height));

        if (width == 960 && height == 270)
        {
            var previewPath = Path.Combine(AppContext.BaseDirectory, "NovaCraft-Hero-Preview.png");
            frame.Save(previewPath, ImageFormat.Png);
        }

        var sampledColors = new HashSet<int>();
        for (var y = 0; y < height; y += 7)
        {
            for (var x = 0; x < width; x += 7)
                sampledColors.Add(frame.GetPixel(x, y).ToArgb());
        }

        Assert.True(sampledColors.Count > 30, $"Expected a detailed rendered scene, got {sampledColors.Count} sampled colors.");
        Assert.NotEqual(frame.GetPixel(4, 4), frame.GetPixel(width - 5, height - 5));
    }

    [Fact]
    public void AnimationStepChangesRenderedFrame()
    {
        using var hero = new HeroScene3D { Size = new Size(800, 240) };
        hero.CreateControl();

        using var before = new Bitmap(800, 240);
        using var after = new Bitmap(800, 240);
        hero.DrawToBitmap(before, new Rectangle(0, 0, before.Width, before.Height));

        hero.AdvanceFrame(0.25f);
        hero.DrawToBitmap(after, new Rectangle(0, 0, after.Width, after.Height));

        var changedSamples = 0;
        for (var y = 0; y < before.Height; y += 4)
        {
            for (var x = 0; x < before.Width; x += 4)
            {
                if (before.GetPixel(x, y).ToArgb() != after.GetPixel(x, y).ToArgb())
                    changedSamples++;
            }
        }

        Assert.True(changedSamples > 20, $"Expected a visibly different animated frame, got {changedSamples} changed samples.");
    }

    [Fact]
    public void HeroExposesKeyboardAccessibleLaunchAction()
    {
        using var hero = new HeroScene3D { Size = new Size(800, 260) };
        Assert.Equal(AccessibleRole.PushButton, hero.AccessibleRole);
        Assert.True(hero.TabStop);
        Assert.False(string.IsNullOrWhiteSpace(hero.AccessibleDescription));
    }
}
