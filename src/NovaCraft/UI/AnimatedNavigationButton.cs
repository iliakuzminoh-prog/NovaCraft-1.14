using System.Drawing.Drawing2D;

namespace NovaCraft.UI;

/// <summary>
/// A navigation button that animates its selected background and left-side position marker.
/// The timer is active only while selection or hover state is changing.
/// </summary>
internal sealed class AnimatedNavigationButton : Button
{
    private readonly System.Windows.Forms.Timer animationTimer = new() { Interval = 16 };
    private readonly Color normalBackColor = Color.FromArgb(15, 17, 27);
    private readonly Color selectedBackColor = Color.FromArgb(43, 35, 75);
    private readonly Color hoverBackColor = Color.FromArgb(31, 31, 48);
    private readonly Color normalTextColor = Color.FromArgb(190, 198, 215);
    private float selectionProgress;
    private float hoverProgress;
    private bool pointerInside;
    private bool selected;

    internal float SelectionProgress => selectionProgress;

    public bool Selected
    {
        get => selected;
        set
        {
            if (selected == value)
                return;

            selected = value;
            StartAnimation();
        }
    }

    public AnimatedNavigationButton()
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
        FlatAppearance.MouseOverBackColor = hoverBackColor;
        BackColor = normalBackColor;
        ForeColor = normalTextColor;
        Cursor = Cursors.Hand;
        TabStop = true;
        AccessibleRole = AccessibleRole.PushButton;

        animationTimer.Tick += (_, _) => AdvanceAnimationFrame(0.23f);
    }

    internal void AdvanceAnimationFrame(float interpolation)
    {
        if (!float.IsFinite(interpolation))
            return;

        var amount = Math.Clamp(interpolation, 0.01f, 1f);
        var targetSelection = selected ? 1f : 0f;
        var targetHover = pointerInside && Enabled ? 1f : 0f;

        selectionProgress += (targetSelection - selectionProgress) * amount;
        hoverProgress += (targetHover - hoverProgress) * amount;

        if (Math.Abs(targetSelection - selectionProgress) < 0.004f)
            selectionProgress = targetSelection;
        if (Math.Abs(targetHover - hoverProgress) < 0.004f)
            hoverProgress = targetHover;

        var selectedColor = BlendColor(normalBackColor, selectedBackColor, selectionProgress);
        BackColor = BlendColor(selectedColor, hoverBackColor, hoverProgress * 0.30f);
        ForeColor = BlendColor(
            normalTextColor,
            Color.White,
            selectionProgress * 0.86f + hoverProgress * 0.10f);

        Invalidate();

        if (selectionProgress == targetSelection && hoverProgress == targetHover)
            animationTimer.Stop();
    }

    private void StartAnimation()
    {
        if (!IsDisposed && IsHandleCreated && Visible && Enabled && !animationTimer.Enabled)
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

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!Visible)
        {
            pointerInside = false;
            animationTimer.Stop();
        }
        else
        {
            StartAnimation();
        }
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        if (!Enabled)
        {
            pointerInside = false;
            hoverProgress = 0f;
            animationTimer.Stop();
        }
        else
        {
            StartAnimation();
        }
        Invalidate();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        StartAnimation();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        animationTimer.Stop();
        base.OnHandleDestroyed(e);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // BackColor and the page marker are painted in OnPaint.
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        if (ClientSize.Width < 8 || ClientSize.Height < 8)
            return;

        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        if (hoverProgress > 0.01f)
        {
            using var hoverPen = new Pen(
                Color.FromArgb((int)(16 + hoverProgress * 42), AppTheme.Accent),
                1.2f);
            var hoverBounds = new RectangleF(
                2f,
                2f,
                ClientSize.Width - 4f,
                ClientSize.Height - 4f);
            using var hoverPath = RoundedPath(hoverBounds, 8f);
            graphics.DrawPath(hoverPen, hoverPath);
        }

        if (selectionProgress > 0.005f)
        {
            var barHeight = Math.Max(9f, ClientSize.Height * (0.32f + 0.68f * selectionProgress));
            var barWidth = 2.5f + 1.5f * selectionProgress;
            var barBounds = new RectangleF(
                3f,
                (ClientSize.Height - barHeight) / 2f,
                barWidth,
                barHeight);
            using var barGlow = new Pen(
                Color.FromArgb((int)(25 + selectionProgress * 70), AppTheme.Accent),
                5f);
            using var barPath = RoundedPath(barBounds, 2.5f);
            graphics.DrawPath(barGlow, barPath);

            using var barBrush = new SolidBrush(
                Color.FromArgb((int)(115 + selectionProgress * 140), AppTheme.AccentHover));
            graphics.FillPath(barBrush, barPath);
        }
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
