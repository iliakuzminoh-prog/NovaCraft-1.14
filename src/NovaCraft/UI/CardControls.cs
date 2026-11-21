using System.Drawing.Drawing2D;

namespace NovaCraft;

internal static class UiPaint
{
    public static GraphicsPath RoundedRect(RectangleF rect, float radius)
    {
        var diameter = Math.Max(0f, Math.Min(radius * 2f, Math.Min(rect.Width, rect.Height)));
        var path = new GraphicsPath();
        if (diameter <= 0f)
        {
            path.AddRectangle(rect);
            return path;
        }

        path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

/// <summary>Карточка проекта со скруглёнными углами и подсветкой рамки при наведении.</summary>
internal sealed class CardPanel : Panel
{
    const float CornerRadius = 16f;
    static readonly Color FallbackOuter = Color.FromArgb(12, 14, 23);
    static readonly Color BorderIdle = Color.FromArgb(44, 49, 74);
    static readonly Color BorderHot = Color.FromArgb(112, 76, 240);
    bool hot;

    public CardPanel()
    {
        SetStyle(
            ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // Фон рисуется целиком в OnPaint, чтобы углы были сглаженными.
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.Clear(Parent is not null && Parent.BackColor.A == 255 ? Parent.BackColor : FallbackOuter);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = new RectangleF(0.75f, 0.75f, Width - 1.5f, Height - 1.5f);
        using var path = UiPaint.RoundedRect(rect, CornerRadius);
        using var fill = new SolidBrush(BackColor);
        graphics.FillPath(fill, path);
        using var pen = new Pen(hot ? BorderHot : BorderIdle, 1.5f);
        graphics.DrawPath(pen, path);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        SetHot(true);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        // Курсор перешёл на дочерний элемент карточки — рамка остаётся подсвеченной.
        if (!ClientRectangle.Contains(PointToClient(Cursor.Position)))
            SetHot(false);
    }

    void SetHot(bool value)
    {
        if (hot == value) return;
        hot = value;
        Invalidate();
    }
}

/// <summary>PictureBox со скруглёнными углами и сглаженным масштабированием иконки.</summary>
internal sealed class RoundedPictureBox : PictureBox
{
    const float CornerRadius = 14f;
    static readonly Color Backplate = Color.FromArgb(35, 39, 58);

    public RoundedPictureBox()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.Clear(BackColor);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var bounds = new RectangleF(0, 0, Width, Height);
        using (var platePath = UiPaint.RoundedRect(bounds, CornerRadius))
        using (var plate = new SolidBrush(Backplate))
            graphics.FillPath(plate, platePath);

        var image = Image;
        if (image is null || image.Width <= 0 || image.Height <= 0) return;

        var scale = Math.Min((float)Width / image.Width, (float)Height / image.Height);
        var width = image.Width * scale;
        var height = image.Height * scale;
        var target = new RectangleF((Width - width) / 2f, (Height - height) / 2f, width, height);

        using var path = UiPaint.RoundedRect(target, CornerRadius);
        using var brush = new TextureBrush(image, WrapMode.Clamp);
        brush.TranslateTransform(target.X, target.Y);
        brush.ScaleTransform(scale, scale);
        graphics.FillPath(brush, path);
    }
}
