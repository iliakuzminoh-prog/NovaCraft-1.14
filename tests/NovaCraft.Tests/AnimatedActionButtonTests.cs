using System.Drawing;
using System.Windows.Forms;
using NovaCraft.UI;

namespace NovaCraft.Tests;

public sealed class AnimatedActionButtonTests
{
    [Theory]
    [InlineData(220, 42)]
    [InlineData(164, 38)]
    [InlineData(290, 54)]
    public void RendersAtCommonActionButtonSizes(int width, int height)
    {
        using var button = new AnimatedActionButton
        {
            Size = new Size(width, height),
            Text = "ИГРАТЬ",
            BackColor = AppTheme.Accent,
            ForeColor = Color.White
        };
        button.CreateControl();

        using var frame = new Bitmap(width, height);
        button.DrawToBitmap(frame, new Rectangle(0, 0, width, height));

        var colors = new HashSet<int>();
        for (var y = 0; y < height; y += 3)
        {
            for (var x = 0; x < width; x += 3)
                colors.Add(frame.GetPixel(x, y).ToArgb());
        }

        Assert.True(colors.Count > 10, $"Expected a gradient button surface, got {colors.Count} sampled colors.");
        Assert.Equal(AccessibleRole.PushButton, button.AccessibleRole);
        Assert.True(button.TabStop);
    }

    [Fact]
    public void SupportsKeyboardAndProgrammaticActivation()
    {
        using var button = new AnimatedActionButton
        {
            Size = new Size(220, 44),
            Text = "УСТАНОВИТЬ"
        };
        var clickCount = 0;
        button.Click += (_, _) => clickCount++;

        button.PerformClick();

        Assert.Equal(1, clickCount);
        Assert.Equal(AccessibleRole.PushButton, button.AccessibleRole);
    }

    [Fact]
    public void IgnoresInvalidAnimationInterpolationValues()
    {
        using var button = new AnimatedActionButton { Size = new Size(220, 44) };
        button.AdvanceAnimationFrame(float.NaN);
        button.AdvanceAnimationFrame(float.PositiveInfinity);
        button.AdvanceAnimationFrame(-1f);
        button.AdvanceAnimationFrame(2f);
    }
}
