using System.Drawing;
using System.Drawing.Drawing2D;
using NovaCraft;

namespace NovaCraft.Tests;

public sealed class Skin3DPreviewTests
{
    [Theory]
    [InlineData(64, 64, false)]
    [InlineData(64, 64, true)]
    [InlineData(64, 32, false)]
    public void RendersSupportedSkinAtlases(int width, int height, bool slim)
    {
        using var skin = new Bitmap(width, height);
        using (var graphics = Graphics.FromImage(skin))
        {
            graphics.Clear(Color.FromArgb(255, 62, 75, 120));
            using var accent = new SolidBrush(Color.FromArgb(255, 125, 76, 220));
            graphics.FillRectangle(accent, 0, 0, width / 2, height / 2);
        }

        using var preview = new Skin3DPreview
        {
            Size = new Size(220, 220),
            Slim = slim
        };
        preview.SetSkin(skin);
        preview.CreateControl();

        using var output = new Bitmap(220, 220);
        preview.DrawToBitmap(output, new Rectangle(0, 0, output.Width, output.Height));

        Assert.NotEqual(output.GetPixel(0, 0), output.GetPixel(110, 108));
    }

    [Fact]
    public void ClearingSkinReturnsPreviewToEmptyStateWithoutThrowing()
    {
        using var skin = new Bitmap(64, 64);
        using var preview = new Skin3DPreview();
        preview.SetSkin(skin);
        preview.SetSkin(null);
    }
}
