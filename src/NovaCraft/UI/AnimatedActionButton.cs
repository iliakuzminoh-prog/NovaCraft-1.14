using System.Drawing.Drawing2D;

namespace NovaCraft.UI;

/// <summary>
/// A reusable WinForms action button with a small depth response on hover and press.
/// Its animation timer runs only while the control is transitioning between states.
/// </summary>
internal sealed class AnimatedActionButton : Button
{
    private readonly System.Windows.Forms.Timer animationTimer = new() { Interval = 16 };
    private float hoverProgress;
    private float pressProgress;
    private bool pointerInside;
    private bool pointerPressed;

    public AnimatedActionButton()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.Selectable,
            true);

        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = AppTheme.Accent;
        ForeColor = Color.White;
        TextAlign = ContentAlignment.MiddleCenter;
        Cursor = Cursors.Hand;
        TabStop = true;
        AccessibleRole = AccessibleRole.PushButton;

        animationTimer.Tick += (_, _) => AdvanceAnimationFrame(0.24f);
    }

    internal void AdvanceAnimationFrame(float interpolation)
    {
        if (!float.IsFinite(interpolation))
            return;

        var amount = Math.Clamp(interpolation, 0.01f, 1f);
        var targetHover = Enabled && pointerInside ? 1f : 0f;
        var targetPress = Enabled && pointerPressed ? 1f : 0f;

        hoverProgress += (targetHover - hoverProgress) * amount;
        pressProgress += (targetPress - pressProgress) * amount;

        if (Math.Abs(targetHover - hoverProgress) < 0.004f)
            hoverProgress = targetHover;
        if (Math.Abs(targetPress - pressProgress) < 0.004f)
            pressProgress = targetPress;

        Invalidate();

        if (hoverProgress == targetHover && pressProgress == targetPress)
            animationTimer.Stop();
    }

    private void StartAnimation()
    {
        if (IsHandleCreated && Visible && Enabled && !animationTimer.Enabled)
            animationTimer.Start();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        pointerInside = true;
        StartAnimation();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        pointerInside = false;
        StartAnimation();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && Enabled)
        {
            pointerPressed = true;
            StartAnimation();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left)
        {
            pointerPressed = false;
            pointerInside = Enabled && ClientRectangle.Contains(e.Location);
            StartAnimation();
        }
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!Visible)
        {
            pointerInside = false;
            pointerPressed = false;
            animationTimer.Stop();
            hoverProgress = 0f;
            pressProgress = 0f;
        }
        else if (pointerInside || pointerPressed)
        {
            StartAnimation();
        }
        Invalidate();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        animationTimer.Stop();
        base.OnHandleDestroyed(e);
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        if (!Enabled)
        {
            pointerInside = false;
            pointerPressed = false;
            animationTimer.Stop();
            hoverProgress = 0f;
            pressProgress = 0f;
        }
        Invalidate();
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Invalidate();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        Invalidate();
    }

    protected override void OnBackColorChanged(EventArgs e)
    {
        base.OnBackColorChanged(e);
        Invalidate();
    }

    protected override void OnForeColorChanged(EventArgs e)
    {
        base.OnForeColorChanged(e);
        Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // The entire rounded surface is painted in OnPaint to avoid square corners.
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var width = ClientSize.Width;
        var height = ClientSize.Height;
        if (width < 4 || height < 4)
            return;

        var scale = 1f + hoverProgress * 0.012f - pressProgress * 0.026f;
        var state = graphics.Save();
        graphics.TranslateTransform(width / 2f, height / 2f);
        graphics.ScaleTransform(scale, scale);
        graphics.TranslateTransform(-width / 2f, -height / 2f);

        var bounds = new RectangleF(3f, 2f, width - 6f, height - 7f);
        const float radius = 10f;

        if (Enabled)
        {
            var shadowAlpha = (int)(62 + hoverProgress * 24 - pressProgress * 12);
            using var shadowBrush = new SolidBrush(Color.FromArgb(shadowAlpha, 3, 5, 15));
            using var shadowPath = RoundedPath(
                new RectangleF(bounds.X, bounds.Y + 3f - pressProgress * 1.5f, bounds.Width, bounds.Height),
                radius);
            graphics.FillPath(shadowBrush, shadowPath);
        }

        var normalTop = Enabled ? BackColor : Color.FromArgb(55, 62, 78);
        var normalBottom = Enabled ? ShiftColor(BackColor, -22) : Color.FromArgb(39, 45, 59);
        var hoverTop = Enabled ? BlendColor(BackColor, AppTheme.AccentHover, 0.62f) : normalTop;
        var hoverBottom = Enabled ? BlendColor(normalBottom, AppTheme.AccentHover, 0.30f) : normalBottom;
        var topColor = BlendColor(normalTop, hoverTop, hoverProgress);
        var bottomColor = BlendColor(normalBottom, hoverBottom, hoverProgress);

        using (var fill = new LinearGradientBrush(bounds, topColor, bottomColor, LinearGradientMode.Vertical))
        using (var path = RoundedPath(bounds, radius))
            graphics.FillPath(fill, path);

        var borderColor = Enabled
            ? BlendColor(Color.FromArgb(80, 31, 35, 57), Color.FromArgb(175, 193, 180, 255), hoverProgress)
            : Color.FromArgb(80, 111, 121, 145);
        using (var border = new Pen(borderColor, hoverProgress > 0.05f ? 1.1f : 0.8f))
        using (var path = RoundedPath(bounds, radius))
            graphics.DrawPath(border, path);

        if (Enabled && hoverProgress > 0.015f)
        {
            using var glow = new Pen(
                Color.FromArgb((int)(18 + hoverProgress * 65), 174, 151, 255),
                3.2f);
            using var path = RoundedPath(bounds, radius);
            graphics.DrawPath(glow, path);

            using var highlight = new Pen(Color.FromArgb((int)(42 + hoverProgress * 65), 255, 255, 255), 1f);
            graphics.DrawLine(
                highlight,
                bounds.Left + radius,
                bounds.Top + 1.2f,
                bounds.Right - radius,
                bounds.Top + 1.2f);
        }

        DrawButtonContent(graphics, bounds);

        if (Focused && ShowFocusCues)
        {
            var focusBounds = RectangleF.Inflate(bounds, -5f, -5f);
            using var focusPen = new Pen(Color.FromArgb(225, 233, 229, 255), 1f)
            {
                DashStyle = DashStyle.Dot
            };
            using var focusPath = RoundedPath(focusBounds, Math.Max(3f, radius - 4f));
            graphics.DrawPath(focusPen, focusPath);
        }

        graphics.Restore(state);
        base.OnPaint(e);
    }

    private void DrawButtonContent(Graphics graphics, RectangleF bounds)
    {
        var foreground = Enabled ? ForeColor : Color.FromArgb(145, 153, 170);
        var textBounds = Rectangle.Round(bounds);
        textBounds.Inflate(-9, -2);

        if (Image != null)
        {
            var imageSize = Image.Size;
            var maxHeight = Math.Max(1, textBounds.Height - 2);
            var imageScale = Math.Min(1f, maxHeight / (float)Math.Max(1, imageSize.Height));
            var drawSize = new Size(
                Math.Max(1, (int)(imageSize.Width * imageScale)),
                Math.Max(1, (int)(imageSize.Height * imageScale)));
            var imageRect = new Rectangle(
                textBounds.Left,
                textBounds.Top + (textBounds.Height - drawSize.Height) / 2,
                drawSize.Width,
                drawSize.Height);

            graphics.DrawImage(Image, imageRect);
            textBounds.X += drawSize.Width + 7;
            textBounds.Width = Math.Max(1, textBounds.Width - drawSize.Width - 7);
        }

        var flags = TextFormatFlags.EndEllipsis |
                    TextFormatFlags.VerticalCenter |
                    TextFormatFlags.SingleLine |
                    TextFormatFlags.NoPadding;

        flags |= TextAlign switch
        {
            ContentAlignment.TopLeft or ContentAlignment.MiddleLeft or ContentAlignment.BottomLeft => TextFormatFlags.Left,
            ContentAlignment.TopRight or ContentAlignment.MiddleRight or ContentAlignment.BottomRight => TextFormatFlags.Right,
            _ => TextFormatFlags.HorizontalCenter
        };

        TextRenderer.DrawText(graphics, Text, Font, textBounds, foreground, flags);
    }

    private static GraphicsPath RoundedPath(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Min(radius * 2f, Math.Min(bounds.Width, bounds.Height));
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static Color ShiftColor(Color color, int amount) =>
        Color.FromArgb(
            color.A,
            Math.Clamp(color.R + amount, 0, 255),
            Math.Clamp(color.G + amount, 0, 255),
            Math.Clamp(color.B + amount, 0, 255));

    private static Color BlendColor(Color from, Color to, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        return Color.FromArgb(
            (int)(from.A + (to.A - from.A) * amount),
            (int)(from.R + (to.R - from.R) * amount),
            (int)(from.G + (to.G - from.G) * amount),
            (int)(from.B + (to.B - from.B) * amount));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            animationTimer.Stop();
            animationTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}
