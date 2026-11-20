using System.Drawing;
using System.Drawing.Drawing2D;

namespace NovaCraft;

internal sealed class MinecraftSkinPreview : Control
{
    Bitmap? skin;
    bool slim;
    float phase;
    readonly System.Windows.Forms.Timer animationTimer;

    public MinecraftSkinPreview()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(15, 18, 29);
        TabStop = false;
        animationTimer = new System.Windows.Forms.Timer { Interval = 80 };
        animationTimer.Tick += (_, _) =>
        {
            phase += 0.045f;
            if (phase > MathF.Tau) phase -= MathF.Tau;
            Invalidate();
        };
        animationTimer.Start();
        Disposed += (_, _) =>
        {
            animationTimer.Stop();
            animationTimer.Dispose();
            skin?.Dispose();
        };
    }

    public void SetSkin(string path, bool isSlim)
    {
        using var source = Image.FromFile(path);
        var copy = new Bitmap(source);
        skin?.Dispose();
        skin = copy;
        slim = isSlim;
        Invalidate();
    }

    public void ClearSkin()
    {
        skin?.Dispose();
        skin = null;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;

        using var bg = new LinearGradientBrush(ClientRectangle,
            Color.FromArgb(22, 26, 43), Color.FromArgb(12, 14, 24), 90f);
        g.FillRectangle(bg, ClientRectangle);

        if (skin == null)
        {
            using var brush = new SolidBrush(Color.FromArgb(120, 130, 150));
            using var font = new Font("Segoe UI Semibold", 11);
            var text = "СКИН НЕ ВЫБРАН";
            var size = g.MeasureString(text, font);
            g.DrawString(text, font, brush, (Width - size.Width) / 2f, (Height - size.Height) / 2f);
            return;
        }

        var bob = MathF.Sin(phase) * 3f;
        var scale = MathF.Min((Width * 0.72f) / 16f, (Height * 0.82f) / 32f);
        var unit = Math.Max(3f, scale);
        var centerX = Width / 2f;
        var top = Height * 0.10f + bob;

        var head = new RectangleF(centerX - 4f * unit, top, 8f * unit, 8f * unit);
        var body = new RectangleF(centerX - 4f * unit, head.Bottom, 8f * unit, 12f * unit);
        var armWidth = (slim ? 3f : 4f) * unit;
        var leftArm = new RectangleF(body.Left - armWidth, body.Top, armWidth, 12f * unit);
        var rightArm = new RectangleF(body.Right, body.Top, armWidth, 12f * unit);
        var rightLeg = new RectangleF(centerX - 4f * unit, body.Bottom, 4f * unit, 12f * unit);
        var leftLeg = new RectangleF(centerX, body.Bottom, 4f * unit, 12f * unit);

        DrawPart(g, head, new Rectangle(8, 8, 8, 8), new Rectangle(40, 8, 8, 8));
        DrawPart(g, body, new Rectangle(20, 20, 8, 12), skin.Height >= 64 ? new Rectangle(20, 36, 8, 12) : null);

        var armY = skin.Height >= 64 ? 52 : 20;
        DrawPart(g, leftArm, new Rectangle(36, armY, slim ? 3 : 4, 12), skin.Height >= 64 ? new Rectangle(52, 52, slim ? 3 : 4, 12) : null);
        DrawPart(g, rightArm, new Rectangle(44, 20, slim ? 3 : 4, 12), skin.Height >= 64 ? new Rectangle(44, 36, slim ? 3 : 4, 12) : null);
        DrawPart(g, rightLeg, new Rectangle(4, 20, 4, 12), skin.Height >= 64 ? new Rectangle(4, 36, 4, 12) : null);
        DrawPart(g, leftLeg, new Rectangle(20, skin.Height >= 64 ? 52 : 20, 4, 12), skin.Height >= 64 ? new Rectangle(4, 52, 4, 12) : null);

        using var shadow = new SolidBrush(Color.FromArgb(55, 0, 0, 0));
        g.FillEllipse(shadow, centerX - 7f * unit, body.Bottom + 1f, 14f * unit, 2.4f * unit);
    }

    void DrawPart(Graphics g, RectangleF destination, Rectangle baseSource, Rectangle? overlaySource)
    {
        if (skin == null) return;
        g.DrawImage(skin, destination, baseSource, GraphicsUnit.Pixel);
        if (overlaySource is Rectangle overlay)
            g.DrawImage(skin, destination, overlay, GraphicsUnit.Pixel);

        using var edge = new Pen(Color.FromArgb(28, 255, 255, 255), Math.Max(1f, destination.Width / 64f));
        g.DrawRectangle(edge, destination.X, destination.Y, destination.Width, destination.Height);
    }
}
