using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace NovaCraft;

/// <summary>
/// Lightweight software-rendered Minecraft skin preview.
/// It does not use a game engine, native plugin, or background process.
/// </summary>
internal sealed class Skin3DPreview : Control
{
    private readonly System.Windows.Forms.Timer animationTimer;
    private Bitmap? skin;
    private float rotation = -0.42f;
    private float tilt = -0.10f;
    private bool slim;
    private bool dragging;
    private Point lastMouse;

    private readonly record struct V3(float X, float Y, float Z)
    {
        public static V3 operator +(V3 a, V3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static V3 operator /(V3 a, float b) => new(a.X / b, a.Y / b, a.Z / b);
    }

    private readonly record struct FaceUv(
        Rectangle Front,
        Rectangle Back,
        Rectangle Right,
        Rectangle Left,
        Rectangle Top,
        Rectangle Bottom);

    private sealed record MeshFace(V3[] Points, V3 Normal, Rectangle Uv, float Opacity);

    private readonly record struct ProjectedFace(
        PointF[] Points,
        float Depth,
        float Shade,
        Rectangle Uv,
        float Opacity);

    public Skin3DPreview()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        BackColor = Color.FromArgb(13, 17, 33);
        ForeColor = Color.White;
        Cursor = Cursors.Hand;
        AccessibleName = "3D preview of the selected Minecraft skin";

        animationTimer = new System.Windows.Forms.Timer { Interval = 33 };
        animationTimer.Tick += (_, _) =>
        {
            if (!dragging && skin != null && !IsDisposed)
            {
                rotation += 0.0045f;
                if (rotation > MathF.PI * 2)
                    rotation -= MathF.PI * 2;
                Invalidate();
            }
        };
        animationTimer.Start();
    }

    public bool Slim
    {
        get => slim;
        set
        {
            if (slim == value) return;
            slim = value;
            Invalidate();
        }
    }

    public void SetSkin(Image? source)
    {
        Bitmap? replacement = null;
        if (source != null)
        {
            if (source.Width != 64 || (source.Height != 64 && source.Height != 32))
                throw new InvalidDataException("Для 3D-просмотра нужен PNG 64×64 или 64×32.");

            replacement = new Bitmap(source);
        }

        var previous = skin;
        skin = replacement;
        previous?.Dispose();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.CompositingQuality = CompositingQuality.HighQuality;

        DrawBackground(graphics);

        if (skin == null)
        {
            TextRenderer.DrawText(
                graphics,
                "Выбери PNG-скин",
                new Font("Segoe UI Semibold", 10, FontStyle.Bold),
                ClientRectangle,
                Color.FromArgb(180, 193, 221),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }

        DrawPedestal(graphics);
        DrawModel(graphics);

        using var hintFont = new Font("Segoe UI", 7.5f, FontStyle.Regular);
        TextRenderer.DrawText(
            graphics,
            "ПЕРЕТАЩИ, ЧТОБЫ ПОВЕРНУТЬ",
            hintFont,
            new Rectangle(4, Height - 24, Width - 8, 16),
            Color.FromArgb(135, 153, 194),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    private void DrawBackground(Graphics graphics)
    {
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0)
            return;

        using var background = new LinearGradientBrush(
            ClientRectangle,
            Color.FromArgb(12, 17, 36),
            Color.FromArgb(22, 23, 49),
            LinearGradientMode.Vertical);
        graphics.FillRectangle(background, ClientRectangle);

        using var glowPath = new GraphicsPath();
        var glowSize = Math.Min(Width, Height) * 0.92f;
        glowPath.AddEllipse(
            (Width - glowSize) / 2f,
            (Height - glowSize) / 2f - 6,
            glowSize,
            glowSize);
        using var glow = new PathGradientBrush(glowPath)
        {
            CenterColor = Color.FromArgb(44, 104, 78, 239),
            SurroundColors = new[] { Color.FromArgb(0, 76, 80, 190) }
        };
        graphics.FillPath(glow, glowPath);

        using var gridPen = new Pen(Color.FromArgb(16, 133, 152, 220), 1f);
        var horizon = (int)(Height * 0.78f);
        for (var x = -Width; x <= Width * 2; x += 24)
            graphics.DrawLine(gridPen, Width / 2, horizon, x, Height);
        for (var y = horizon; y < Height; y += 14)
            graphics.DrawLine(gridPen, 0, y, Width, y);
    }

    private void DrawPedestal(Graphics graphics)
    {
        var centerX = Width / 2f;
        var centerY = Height * 0.83f;

        using var shadow = new SolidBrush(Color.FromArgb(75, 4, 5, 16));
        graphics.FillEllipse(shadow, centerX - 66, centerY - 5, 132, 25);

        using var baseBrush = new LinearGradientBrush(
            new RectangleF(centerX - 58, centerY - 8, 116, 22),
            Color.FromArgb(112, 78, 240),
            Color.FromArgb(29, 34, 86),
            LinearGradientMode.Vertical);
        graphics.FillEllipse(baseBrush, centerX - 58, centerY - 9, 116, 23);

        using var rim = new Pen(Color.FromArgb(185, 157, 136, 255), 1.4f);
        graphics.DrawEllipse(rim, centerX - 58, centerY - 9, 116, 23);

        using var inner = new Pen(Color.FromArgb(90, 99, 194, 255), 1f);
        graphics.DrawEllipse(inner, centerX - 43, centerY - 6, 86, 16);
    }

    private void DrawModel(Graphics graphics)
    {
        var faces = new List<MeshFace>(96);
        BuildSkinMesh(faces, skin!.Height == 32);

        var scale = Math.Min((Width - 38f) / 25f, (Height - 62f) / 33f);
        if (!float.IsFinite(scale) || scale <= 0)
            return;

        var center = new PointF(Width / 2f, Height * 0.49f);
        const float cameraDistance = 76f;

        var projected = new List<ProjectedFace>(faces.Count);
        foreach (var face in faces)
        {
            var normal = Rotate(face.Normal);
            if (normal.Z <= 0.015f)
                continue;

            var points = new PointF[4];
            var depth = 0f;
            for (var i = 0; i < 4; i++)
            {
                var rotated = Rotate(face.Points[i]);
                depth += rotated.Z;
                var perspective = cameraDistance / (cameraDistance - rotated.Z);
                points[i] = new PointF(
                    center.X + rotated.X * scale * perspective,
                    center.Y - rotated.Y * scale * perspective);
            }

            depth /= 4f;
            var shade = Math.Clamp(0.62f + normal.Z * 0.16f + Math.Max(0, normal.Y) * 0.22f, 0.45f, 1.12f);
            projected.Add(new ProjectedFace(points, depth, shade, face.Uv, face.Opacity));
        }

        // Draw far faces before near faces to keep cuboids visually coherent.
        projected.Sort((a, b) => a.Depth.CompareTo(b.Depth));

        foreach (var face in projected)
            DrawTexturedFace(graphics, face);
    }

    private V3 Rotate(V3 point)
    {
        var cosY = MathF.Cos(rotation);
        var sinY = MathF.Sin(rotation);
        var x = point.X * cosY + point.Z * sinY;
        var z = -point.X * sinY + point.Z * cosY;

        var cosX = MathF.Cos(tilt);
        var sinX = MathF.Sin(tilt);
        var y2 = point.Y * cosX - z * sinX;
        var z2 = point.Y * sinX + z * cosX;

        return new V3(x, y2, z2);
    }

    private void DrawTexturedFace(Graphics graphics, ProjectedFace face)
    {
        var destination = new[] { face.Points[0], face.Points[1], face.Points[3] };
        var shade = face.Shade;
        var alpha = face.Opacity;

        using var attributes = new ImageAttributes();
        var matrix = new ColorMatrix
        {
            Matrix00 = shade,
            Matrix11 = shade,
            Matrix22 = shade,
            Matrix33 = alpha,
            Matrix44 = 1f
        };
        attributes.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);

        var source = new RectangleF(face.Uv.X, face.Uv.Y, face.Uv.Width, face.Uv.Height);
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        graphics.DrawImage(skin!, destination, source, GraphicsUnit.Pixel, attributes);

        using var outline = new Pen(Color.FromArgb((int)(alpha * 72), 8, 11, 25), 0.65f);
        graphics.DrawPolygon(outline, face.Points);
    }

    private void BuildSkinMesh(List<MeshFace> faces, bool legacySkin)
    {
        var head = new FaceUv(
            new Rectangle(8, 8, 8, 8),
            new Rectangle(24, 8, 8, 8),
            new Rectangle(0, 8, 8, 8),
            new Rectangle(16, 8, 8, 8),
            new Rectangle(8, 0, 8, 8),
            new Rectangle(16, 0, 8, 8));
        AddBox(faces, -4, 20, -4, 8, 8, 8, head, 1f);

        var body = new FaceUv(
            new Rectangle(20, 20, 8, 12),
            new Rectangle(32, 20, 8, 12),
            new Rectangle(16, 20, 4, 12),
            new Rectangle(28, 20, 4, 12),
            new Rectangle(20, 16, 8, 4),
            new Rectangle(28, 16, 8, 4));
        AddBox(faces, -4, 8, -2, 8, 12, 4, body, 1f);

        var rightLeg = new FaceUv(
            new Rectangle(4, 20, 4, 12),
            new Rectangle(12, 20, 4, 12),
            new Rectangle(0, 20, 4, 12),
            new Rectangle(8, 20, 4, 12),
            new Rectangle(4, 16, 4, 4),
            new Rectangle(8, 16, 4, 4));

        var leftLeg = legacySkin
            ? rightLeg
            : new FaceUv(
                new Rectangle(20, 52, 4, 12),
                new Rectangle(28, 52, 4, 12),
                new Rectangle(16, 52, 4, 12),
                new Rectangle(24, 52, 4, 12),
                new Rectangle(20, 48, 4, 4),
                new Rectangle(24, 48, 4, 4));

        AddBox(faces, -4, 0, -2, 4, 8, 4, leftLeg, 1f);
        AddBox(faces, 0, 0, -2, 4, 8, 4, rightLeg, 1f);

        var armWidth = slim ? 3f : 4f;
        var rightArm = new FaceUv(
            new Rectangle(44, 20, (int)armWidth, 12),
            new Rectangle(52, 20, (int)armWidth, 12),
            new Rectangle(40, 20, 4, 12),
            new Rectangle(48, 20, 4, 12),
            new Rectangle(44, 16, (int)armWidth, 4),
            new Rectangle(48, 16, (int)armWidth, 4));

        var leftArm = legacySkin
            ? rightArm
            : new FaceUv(
                new Rectangle(36, 52, (int)armWidth, 12),
                new Rectangle(44, 52, (int)armWidth, 12),
                new Rectangle(32, 52, 4, 12),
                new Rectangle(40, 52, 4, 12),
                new Rectangle(36, 48, (int)armWidth, 4),
                new Rectangle(40, 48, (int)armWidth, 4));

        AddBox(faces, -8, 8, -2, armWidth, 12, 4, leftArm, 1f);
        AddBox(faces, 4, 8, -2, armWidth, 12, 4, rightArm, 1f);

        if (legacySkin)
            return;

        // Outer layers (hat, jacket, sleeves and trousers) are slightly offset.
        var hat = new FaceUv(
            new Rectangle(40, 8, 8, 8),
            new Rectangle(56, 8, 8, 8),
            new Rectangle(32, 8, 8, 8),
            new Rectangle(48, 8, 8, 8),
            new Rectangle(40, 0, 8, 8),
            new Rectangle(48, 0, 8, 8));
        AddBox(faces, -4.25f, 19.75f, -4.25f, 8.5f, 8.5f, 8.5f, hat, 0.94f);

        var jacket = new FaceUv(
            new Rectangle(20, 36, 8, 12),
            new Rectangle(32, 36, 8, 12),
            new Rectangle(16, 36, 4, 12),
            new Rectangle(28, 36, 4, 12),
            new Rectangle(20, 32, 8, 4),
            new Rectangle(28, 32, 8, 4));
        AddBox(faces, -4.18f, 7.82f, -2.18f, 8.36f, 12.36f, 4.36f, jacket, 0.92f);

        var rightLegLayer = new FaceUv(
            new Rectangle(4, 36, 4, 12),
            new Rectangle(12, 36, 4, 12),
            new Rectangle(0, 36, 4, 12),
            new Rectangle(8, 36, 4, 12),
            new Rectangle(4, 32, 4, 4),
            new Rectangle(8, 32, 4, 4));
        var leftLegLayer = new FaceUv(
            new Rectangle(20, 52, 4, 12),
            new Rectangle(28, 52, 4, 12),
            new Rectangle(16, 52, 4, 12),
            new Rectangle(24, 52, 4, 12),
            new Rectangle(20, 48, 4, 4),
            new Rectangle(24, 48, 4, 4));
        AddBox(faces, -4.18f, -0.18f, -2.18f, 4.36f, 8.36f, 4.36f, leftLegLayer, 0.90f);
        AddBox(faces, -0.18f, -0.18f, -2.18f, 4.36f, 8.36f, 4.36f, rightLegLayer, 0.90f);

        var rightSleeve = new FaceUv(
            new Rectangle(44, 36, (int)armWidth, 12),
            new Rectangle(52, 36, (int)armWidth, 12),
            new Rectangle(40, 36, 4, 12),
            new Rectangle(48, 36, 4, 12),
            new Rectangle(44, 32, (int)armWidth, 4),
            new Rectangle(48, 32, (int)armWidth, 4));
        var leftSleeve = new FaceUv(
            new Rectangle(36, 52, (int)armWidth, 12),
            new Rectangle(44, 52, (int)armWidth, 12),
            new Rectangle(32, 52, 4, 12),
            new Rectangle(40, 52, 4, 12),
            new Rectangle(36, 48, (int)armWidth, 4),
            new Rectangle(40, 48, (int)armWidth, 4));

        AddBox(faces, -8.18f, 7.82f, -2.18f, armWidth + 0.36f, 12.36f, 4.36f, leftSleeve, 0.92f);
        AddBox(faces, 3.82f, 7.82f, -2.18f, armWidth + 0.36f, 12.36f, 4.36f, rightSleeve, 0.92f);
    }

    private static void AddBox(
        List<MeshFace> faces,
        float x,
        float y,
        float z,
        float width,
        float height,
        float depth,
        FaceUv uv,
        float opacity)
    {
        var x2 = x + width;
        var y2 = y + height;
        var z2 = z + depth;

        faces.Add(new MeshFace(
            new[] { new V3(x, y2, z2), new V3(x2, y2, z2), new V3(x2, y, z2), new V3(x, y, z2) },
            new V3(0, 0, 1), uv.Front, opacity));

        faces.Add(new MeshFace(
            new[] { new V3(x2, y2, z), new V3(x, y2, z), new V3(x, y, z), new V3(x2, y, z) },
            new V3(0, 0, -1), uv.Back, opacity));

        faces.Add(new MeshFace(
            new[] { new V3(x, y2, z), new V3(x, y2, z2), new V3(x, y, z2), new V3(x, y, z) },
            new V3(-1, 0, 0), uv.Left, opacity));

        faces.Add(new MeshFace(
            new[] { new V3(x2, y2, z2), new V3(x2, y2, z), new V3(x2, y, z), new V3(x2, y, z2) },
            new V3(1, 0, 0), uv.Right, opacity));

        faces.Add(new MeshFace(
            new[] { new V3(x, y2, z), new V3(x2, y2, z), new V3(x2, y2, z2), new V3(x, y2, z2) },
            new V3(0, 1, 0), uv.Top, opacity));

        faces.Add(new MeshFace(
            new[] { new V3(x, y, z2), new V3(x2, y, z2), new V3(x2, y, z), new V3(x, y, z) },
            new V3(0, -1, 0), uv.Bottom, opacity));
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
            return;

        dragging = true;
        lastMouse = e.Location;
        Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!dragging)
            return;

        var deltaX = e.X - lastMouse.X;
        var deltaY = e.Y - lastMouse.Y;
        rotation += deltaX * 0.012f;
        tilt = Math.Clamp(tilt + deltaY * 0.006f, -0.38f, 0.30f);
        lastMouse = e.Location;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left)
            return;

        dragging = false;
        Capture = false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            animationTimer.Stop();
            animationTimer.Dispose();
            skin?.Dispose();
            skin = null;
        }
        base.Dispose(disposing);
    }
}
