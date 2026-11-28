using System.Drawing.Drawing2D;
using System.Numerics;

namespace NovaCraft.UI;

/// <summary>
/// A compact software-rendered rotating cube mark for the launcher header.
/// The animation runs only while the control is visible.
/// </summary>
internal sealed class BrandCube3D : Control
{
    private readonly System.Windows.Forms.Timer animationTimer = new() { Interval = 33 };
    private readonly SolidBrush[] faceBrushes =
    {
        new(Color.FromArgb(255, 122, 91, 255)),
        new(Color.FromArgb(255, 94, 62, 226)),
        new(Color.FromArgb(255, 77, 48, 205)),
        new(Color.FromArgb(255, 51, 35, 155)),
        new(Color.FromArgb(255, 133, 111, 255)),
        new(Color.FromArgb(255, 31, 25, 99))
    };
    private readonly Pen edgePen = new(Color.FromArgb(210, 218, 207, 255), 1.15f);
    private readonly Pen glowPen = new(Color.FromArgb(58, 132, 101, 255), 4f);
    private float rotation;
    private static readonly int[][] CubeFaces =
    {
        new[] { 0, 1, 2, 3 },
        new[] { 5, 4, 7, 6 },
        new[] { 4, 0, 3, 7 },
        new[] { 1, 5, 6, 2 },
        new[] { 3, 2, 6, 7 },
        new[] { 4, 5, 1, 0 }
    };

    public BrandCube3D()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);

        BackColor = Color.Transparent;
        TabStop = false;
        AccessibleRole = AccessibleRole.Graphic;
        AccessibleName = "Логотип NovaCraft";

        animationTimer.Tick += (_, _) => AdvanceFrame(animationTimer.Interval / 1000f);
    }

    internal void AdvanceFrame(float deltaSeconds)
    {
        if (!float.IsFinite(deltaSeconds))
            return;

        rotation = (rotation + Math.Clamp(deltaSeconds, 0f, 0.25f) * 0.9f) % MathF.Tau;
        Invalidate();
    }

    internal void RenderPreview(Graphics graphics)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        DrawCube(graphics);
    }

    private void UpdateAnimationState()
    {
        if (IsHandleCreated && Visible && !IsDisposed)
            animationTimer.Start();
        else
            animationTimer.Stop();
    }

    protected override void OnParentChanged(EventArgs e)
    {
        base.OnParentChanged(e);
        UpdateAnimationState();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UpdateAnimationState();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        animationTimer.Stop();
        base.OnHandleDestroyed(e);
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        UpdateAnimationState();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        DrawCube(e.Graphics);
    }

    private void DrawCube(Graphics graphics)
    {
        var width = ClientSize.Width;
        var height = ClientSize.Height;
        if (width < 4 || height < 4)
            return;

        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var vertices = new[]
        {
            new Vector3(-1f, -1f, -1f), new Vector3(1f, -1f, -1f),
            new Vector3(1f, 1f, -1f), new Vector3(-1f, 1f, -1f),
            new Vector3(-1f, -1f, 1f), new Vector3(1f, -1f, 1f),
            new Vector3(1f, 1f, 1f), new Vector3(-1f, 1f, 1f)
        };

        const float pitch = -0.56f;
        const float roll = 0.12f;
        var projected = new PointF[vertices.Length];
        var depths = new float[vertices.Length];
        var cosY = MathF.Cos(rotation);
        var sinY = MathF.Sin(rotation);
        var cosX = MathF.Cos(pitch);
        var sinX = MathF.Sin(pitch);
        var cosZ = MathF.Cos(roll);
        var sinZ = MathF.Sin(roll);
        var scale = Math.Min(width, height) * 0.30f;
        var centerX = width / 2f;
        var centerY = height / 2f;

        for (var index = 0; index < vertices.Length; index++)
        {
            var point = vertices[index];

            var x = point.X * cosY - point.Z * sinY;
            var z = point.X * sinY + point.Z * cosY;
            var y = point.Y * cosX - z * sinX;
            z = point.Y * sinX + z * cosX;

            var rotatedX = x * cosZ - y * sinZ;
            var rotatedY = x * sinZ + y * cosZ;

            depths[index] = z;
            projected[index] = new PointF(
                centerX + rotatedX * scale,
                centerY - rotatedY * scale);
        }

        var visibleFaces = new List<(PointF[] Points, float Depth, int Material)>(3);
        for (var faceIndex = 0; faceIndex < CubeFaces.Length; faceIndex++)
        {
            var face = CubeFaces[faceIndex];
            var a = vertices[face[0]];
            var b = vertices[face[1]];
            var c = vertices[face[2]];
            var normal = Vector3.Cross(b - a, c - a);

            // The camera is on the negative Z axis; rotate the face normal by the
            // same matrix and draw only faces turned toward the camera.
            var zAfterYaw = normal.X * sinY + normal.Z * cosY;
            var normalZ = normal.Y * sinX + zAfterYaw * cosX;
            if (normalZ <= 0.02f)
                continue;

            var points = new PointF[face.Length];
            var depthTotal = 0f;
            for (var index = 0; index < face.Length; index++)
            {
                var vertexIndex = face[index];
                points[index] = projected[vertexIndex];
                depthTotal += depths[vertexIndex];
            }

            visibleFaces.Add((points, depthTotal / face.Length, faceIndex));
        }

        visibleFaces.Sort(static (left, right) => right.Depth.CompareTo(left.Depth));

        foreach (var face in visibleFaces)
        {
            graphics.DrawPolygon(glowPen, face.Points);
            graphics.FillPolygon(faceBrushes[face.Material], face.Points);
            graphics.DrawPolygon(edgePen, face.Points);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            animationTimer.Stop();
            animationTimer.Dispose();
            foreach (var brush in faceBrushes)
                brush.Dispose();
            edgePen.Dispose();
            glowPen.Dispose();
        }
        base.Dispose(disposing);
    }
}
