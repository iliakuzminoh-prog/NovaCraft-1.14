using System.Drawing.Drawing2D;

namespace NovaCraft.UI;

internal sealed class PageDepthTransition : Control
{
    readonly Bitmap outgoing;
    readonly Bitmap incoming;
    readonly bool forward;
    readonly System.Windows.Forms.Timer timer = new() { Interval = 16 };
    readonly DateTime started = DateTime.UtcNow;
    readonly int durationMs;
    float progress;

    public event EventHandler? Completed;

    public PageDepthTransition(Bitmap outgoing, Bitmap incoming, bool forward, int durationMs = 320)
    {
        this.outgoing = outgoing;
        this.incoming = incoming;
        this.forward = forward;
        this.durationMs = Math.Max(180, durationMs);

        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

        BackColor = Color.FromArgb(8, 10, 17);
        timer.Tick += (_, _) => Advance();
        timer.Start();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        e.Graphics.SmoothingMode = SmoothingMode.HighQuality;
        e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        e.Graphics.CompositingQuality = CompositingQuality.HighQuality;

        var width = Math.Max(1, ClientSize.Width);
        var height = Math.Max(1, ClientSize.Height);
        var eased = EaseInOutCubic(progress);
        var direction = forward ? 1f : -1f;

        var outgoingScale = 1f - 0.045f * eased;
        var incomingScale = 0.955f + 0.045f * eased;
        var outgoingX = -direction * width * 0.11f * eased;
        var incomingX = direction * width * 0.11f * (1f - eased);

        DrawDepthImage(e.Graphics, outgoing, outgoingScale, outgoingX, 1f - 0.26f * eased, width, height, false);
        DrawDepthImage(e.Graphics, incoming, incomingScale, incomingX, 0.58f + 0.42f * eased, width, height, true);

        var seamX = (int)(width * 0.5f + direction * width * 0.11f * (0.5f - eased));
        var seamLeft = Math.Clamp(seamX - 28, 0, Math.Max(0, width - 1));
        var seamWidth = Math.Min(56, width);
        if (seamWidth > 0)
        {
            using var seam = new LinearGradientBrush(
                new Rectangle(seamLeft, 0, seamWidth, height),
                Color.FromArgb(0, 0, 0, 0),
                Color.FromArgb(75, 0, 0, 0),
                LinearGradientMode.Horizontal);
            e.Graphics.FillRectangle(seam, seamLeft, 0, seamWidth, height);
        }
    }

    void DrawDepthImage(Graphics graphics, Bitmap image, float scale, float offsetX, float opacity,
        int width, int height, bool incomingLayer)
    {
        var drawWidth = width * scale;
        var drawHeight = height * scale;
        var x = (width - drawWidth) / 2f + offsetX;
        var y = (height - drawHeight) / 2f;

        var state = graphics.Save();
        graphics.TranslateTransform(x, y);
        graphics.ScaleTransform(scale, scale);

        using var attributes = new System.Drawing.Imaging.ImageAttributes();
        var matrix = new System.Drawing.Imaging.ColorMatrix { Matrix33 = Math.Clamp(opacity, 0f, 1f) };
        attributes.SetColorMatrix(matrix);

        graphics.DrawImage(
            image,
            new Rectangle(0, 0, width, height),
            0, 0, image.Width, image.Height,
            GraphicsUnit.Pixel,
            attributes);

        graphics.Restore(state);

        var shadowWidth = Math.Max(10, (int)(width * 0.018f));
        var shadowX = incomingLayer
            ? (int)(x - shadowWidth * 0.25f)
            : (int)(x + drawWidth - shadowWidth * 0.75f);

        if (shadowX >= -shadowWidth && shadowX < width)
        {
            using var shadow = new LinearGradientBrush(
                new Rectangle(shadowX, 0, shadowWidth, height),
                incomingLayer
                    ? Color.FromArgb((int)(55 * opacity), 0, 0, 0)
                    : Color.FromArgb((int)(38 * opacity), 0, 0, 0),
                Color.FromArgb(0, 0, 0, 0),
                LinearGradientMode.Horizontal);
            graphics.FillRectangle(shadow, shadowX, 0, shadowWidth, height);
        }
    }

    void Advance()
    {
        progress = Math.Clamp(
            (float)(DateTime.UtcNow - started).TotalMilliseconds / durationMs, 0f, 1f);

        Invalidate();

        if (progress >= 1f)
        {
            timer.Stop();
            Completed?.Invoke(this, EventArgs.Empty);
        }
    }

    static float EaseInOutCubic(float value) =>
        value < 0.5f
            ? 4f * value * value * value
            : 1f - MathF.Pow(-2f * value + 2f, 3f) / 2f;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            timer.Stop();
            timer.Dispose();
            outgoing.Dispose();
            incoming.Dispose();
        }
        base.Dispose(disposing);
    }
}
