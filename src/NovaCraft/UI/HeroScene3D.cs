using System.Drawing.Drawing2D;
using System.Numerics;

namespace NovaCraft.UI;

/// <summary>
/// A self-contained, software-rendered 3D hero scene for the launcher home page.
/// The scene is built from projected world-space geometry, not a pre-rendered screenshot.
/// </summary>
internal sealed class HeroScene3D : Control
{
    private readonly System.Windows.Forms.Timer animationTimer = new() { Interval = 33 };
    private readonly List<RenderFace> faces = new();
    private readonly List<Particle> particles = new();
    private readonly Dictionary<int, SolidBrush> materialBrushes = new();
    private readonly SolidBrush[] particleBrushes = new SolidBrush[16];
    private readonly SolidBrush[] starBrushes = new SolidBrush[16];
    private readonly Pen faceEdgePen = new(Color.FromArgb(36, 218, 228, 244), 0.7f);
    private float elapsed;
    private float cameraX;
    private float cameraY;
    private float targetCameraX;
    private float targetCameraY;
    private float buttonHover;
    private bool pointerInsideButton;
    private bool pointerPressed;
    private RectangleF playBounds;
    private const float FocalLength = 470f;

    public event EventHandler? PlayRequested;

    public string GameVersion { get; set; } = "1.21.1";
    public string ProfileName { get; set; } = "OFFLINE PROFILE";

    public HeroScene3D()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.Selectable,
            true);

        BackColor = Color.FromArgb(12, 16, 31);
        ForeColor = Color.White;
        Cursor = Cursors.Hand;
        TabStop = true;
        AccessibleRole = AccessibleRole.PushButton;
        AccessibleName = "Запустить Minecraft";
        AccessibleDescription = "Анимированная трёхмерная сцена NovaCraft. Нажми Enter или кнопку «Играть», чтобы запустить выбранную сборку.";

        for (var index = 0; index < 38; index++)
        {
            var seed = index * 17.371f;
            particles.Add(new Particle(
                UnitHash(seed + 1.1f),
                UnitHash(seed + 4.7f),
                0.28f + UnitHash(seed + 8.3f) * 0.72f,
                0.4f + UnitHash(seed + 9.9f) * 1.6f,
                UnitHash(seed + 12.5f) * MathF.Tau));
        }

        for (var index = 0; index < 16; index++)
        {
            var alpha = index * 16;
            starBrushes[index] = new SolidBrush(Color.FromArgb(alpha, 214, 224, 255));
            particleBrushes[index] = new SolidBrush(Color.FromArgb(alpha, 150, 203, 255));
        }

        BuildWorldMesh();

        animationTimer.Tick += (_, _) =>
        {
            elapsed += animationTimer.Interval / 1000f;
            cameraX += (targetCameraX - cameraX) * 0.075f;
            cameraY += (targetCameraY - cameraY) * 0.075f;
            var targetHover = pointerInsideButton ? 1f : 0f;
            buttonHover += (targetHover - buttonHover) * 0.19f;
            Invalidate();
        };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UpdateAnimationState();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        UpdateAnimationState();
    }

    private void UpdateAnimationState()
    {
        if (IsHandleCreated && Visible && !IsDisposed)
            animationTimer.Start();
        else
            animationTimer.Stop();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var width = ClientSize.Width;
        var height = ClientSize.Height;
        if (width < 2 || height < 2)
            return;

        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.CompositingQuality = CompositingQuality.HighQuality;

        DrawSky(graphics, width, height);
        DrawDistantLandscape(graphics, width, height);
        DrawWorldGeometry(graphics, width, height);
        DrawAtmosphere(graphics, width, height);
        DrawTypography(graphics, width, height);
        DrawPlayButton(graphics, width, height);
    }

    private void DrawSky(Graphics graphics, int width, int height)
    {
        using (var sky = new LinearGradientBrush(
            new Rectangle(0, 0, width, height),
            Color.FromArgb(17, 24, 49),
            Color.FromArgb(48, 30, 72),
            LinearGradientMode.Vertical))
        {
            graphics.FillRectangle(sky, 0, 0, width, height);
        }

        var sunCenter = new PointF(width * 0.755f + cameraX * 5f, height * 0.31f + cameraY * 3f);
        var sunRadius = Math.Max(31f, height * 0.24f);
        using (var glowPath = new GraphicsPath())
        {
            glowPath.AddEllipse(
                sunCenter.X - sunRadius * 2.45f,
                sunCenter.Y - sunRadius * 2.45f,
                sunRadius * 4.9f,
                sunRadius * 4.9f);
            using var glow = new PathGradientBrush(glowPath)
            {
                CenterPoint = sunCenter,
                CenterColor = Color.FromArgb(108, 255, 164, 112),
                SurroundColors = new[] { Color.FromArgb(0, 255, 128, 120) }
            };
            graphics.FillPath(glow, glowPath);
        }

        using (var sunBrush = new LinearGradientBrush(
            new RectangleF(sunCenter.X - sunRadius, sunCenter.Y - sunRadius, sunRadius * 2, sunRadius * 2),
            Color.FromArgb(255, 238, 203, 151),
            Color.FromArgb(255, 237, 127, 125),
            LinearGradientMode.Vertical))
        {
            graphics.FillEllipse(sunBrush, sunCenter.X - sunRadius, sunCenter.Y - sunRadius, sunRadius * 2, sunRadius * 2);
        }

        for (var index = 0; index < 58; index++)
        {
            var x = UnitHash(index * 3.71f + 2.8f) * width;
            var y = UnitHash(index * 8.13f + 5.2f) * height * 0.69f;
            var wave = 0.48f + 0.52f * MathF.Sin(elapsed * (0.7f + index % 4 * 0.13f) + index);
            var alpha = (int)(28 + wave * 112);
            var radius = 0.55f + UnitHash(index * 2.33f) * 1.25f;
            graphics.FillEllipse(starBrushes[Math.Clamp(alpha / 16, 0, starBrushes.Length - 1)],
                x, y, radius * 2, radius * 2);
        }

        DrawCloudBand(graphics, width, height, 0.19f, 0.54f, 0.18f, 0.10f, 0.24f);
        DrawCloudBand(graphics, width, height, 0.63f, 0.72f, 0.12f, 0.075f, -0.18f);
    }

    private void DrawCloudBand(Graphics graphics, int width, int height, float x, float y,
        float cloudWidth, float cloudHeight, float speed)
    {
        var offset = (elapsed * speed * width) % (width + cloudWidth * width);
        var cloudX = x * width + offset;
        if (speed < 0)
            cloudX = x * width - offset;

        using var cloud = new SolidBrush(Color.FromArgb(18, 194, 179, 220));
        var w = cloudWidth * width;
        var h = cloudHeight * height;
        for (var part = 0; part < 4; part++)
        {
            var px = cloudX + part * w * 0.19f;
            var py = y * height + MathF.Sin(elapsed * 0.32f + part) * 3f;
            graphics.FillEllipse(cloud, px, py, w * 0.34f, h * 0.55f);
        }
    }

    private void DrawDistantLandscape(Graphics graphics, int width, int height)
    {
        var baseline = height * 0.71f + cameraY * 2f;

        PointF[] farMountain =
        {
            new(width * 0.37f, baseline + height * 0.02f),
            new(width * 0.49f, height * 0.34f),
            new(width * 0.56f, height * 0.44f),
            new(width * 0.66f, height * 0.27f),
            new(width * 0.75f, height * 0.47f),
            new(width * 0.86f, height * 0.33f),
            new(width, height * 0.58f),
            new(width, height),
            new(width * 0.37f, height)
        };
        using (var mountainBrush = new LinearGradientBrush(
            new Rectangle(0, (int)(height * 0.27f), width, (int)(height * 0.73f)),
            Color.FromArgb(87, 67, 117),
            Color.FromArgb(35, 40, 76),
            LinearGradientMode.Vertical))
        {
            graphics.FillPolygon(mountainBrush, farMountain);
        }

        PointF[] nearMountain =
        {
            new(width * 0.43f, baseline + height * 0.07f),
            new(width * 0.57f, height * 0.52f),
            new(width * 0.65f, height * 0.59f),
            new(width * 0.77f, height * 0.46f),
            new(width * 0.9f, height * 0.61f),
            new(width, height * 0.51f),
            new(width, height),
            new(width * 0.43f, height)
        };
        using (var ridgeBrush = new LinearGradientBrush(
            new Rectangle(0, (int)(height * 0.44f), width, (int)(height * 0.56f)),
            Color.FromArgb(43, 61, 93),
            Color.FromArgb(18, 29, 49),
            LinearGradientMode.Vertical))
        {
            graphics.FillPolygon(ridgeBrush, nearMountain);
        }

        using var haze = new LinearGradientBrush(
            new Rectangle(0, (int)(height * 0.56f), width, Math.Max(1, (int)(height * 0.30f))),
            Color.FromArgb(0, 242, 154, 153),
            Color.FromArgb(36, 159, 117, 184),
            LinearGradientMode.Vertical);
        graphics.FillRectangle(haze, 0, height * 0.56f, width, height * 0.30f);
    }

    private void BuildWorldMesh()
    {
        for (var z = 0; z < 9; z++)
        {
            for (var x = -4; x <= 4; x++)
            {
                var wave = MathF.Sin((x + 1.5f) * 0.63f) * 0.13f + MathF.Cos(z * 0.61f) * 0.12f;
                var top = 0.12f + Math.Max(0, wave);
                var worldX = x * 0.91f;
                var worldZ = z * 0.91f + 0.8f;

                var grass = Color.FromArgb(
                    255,
                    49 + (x + 4) * 2,
                    117 + (z % 3) * 9,
                    93 + (x + z + 13) % 4 * 5);

                AddCubeMesh(worldX, top - 0.84f, worldZ, 0.9f, 0.9f, 0.9f,
                    grass,
                    Color.FromArgb(255, 66, 75, 91),
                    Color.FromArgb(255, 42, 52, 73));

                if ((x * 7 + z * 3) % 17 == 0)
                {
                    AddCubeMesh(worldX, top + 0.06f, worldZ, 0.34f, 0.34f, 0.34f,
                        Color.FromArgb(255, 86, 155, 125),
                        Color.FromArgb(255, 51, 105, 96),
                        Color.FromArgb(255, 36, 75, 83));
                }
            }
        }

        AddTree(-2.6f, 0.22f, 2.4f, 1f);
        AddTree(2.4f, 0.25f, 4.7f, 0.78f);
        AddCrystal(1.05f, 0.2f, 1.9f, 0.72f);

        AddRotatingCube(-2.4f, 2.0f, 3.7f, 0.48f,
            Color.FromArgb(255, 117, 95, 220), 0.47f, 1.2f, 0.10f, 0.62f);
        AddRotatingCube(2.9f, 2.7f, 6.2f, 0.34f,
            Color.FromArgb(255, 70, 177, 174), -0.54f, 4.1f, 0.08f, 0.75f);
    }

    private void DrawWorldGeometry(Graphics graphics, int width, int height)
    {
        var yaw = cameraX * 0.035f + MathF.Sin(elapsed * 0.23f) * 0.012f;
        var pitch = -0.145f + cameraY * 0.018f;

        foreach (var face in faces)
            ProjectFace(face, yaw, pitch, width, height);

        faces.Sort(static (left, right) => right.Depth.CompareTo(left.Depth));

        foreach (var face in faces)
        {
            graphics.FillPolygon(face.Brush, face.Points);
            graphics.DrawPolygon(faceEdgePen, face.Points);
        }
    }

    private void AddTree(float x, float y, float z, float scale)
    {
        var trunk = 0.25f * scale;
        AddCubeMesh(x, y, z, trunk, 1.05f * scale, trunk,
            Color.FromArgb(255, 133, 87, 63),
            Color.FromArgb(255, 91, 61, 62),
            Color.FromArgb(255, 68, 49, 61));

        AddCubeMesh(x - 0.42f * scale, y + 0.7f * scale, z, 0.92f * scale, 0.72f * scale, 0.88f * scale,
            Color.FromArgb(255, 73, 155, 119),
            Color.FromArgb(255, 41, 106, 92),
            Color.FromArgb(255, 31, 78, 82),
            bobAmplitude: 0.018f,
            bobSpeed: 0.9f,
            bobPhase: x);

        AddCubeMesh(x - 0.27f * scale, y + 1.12f * scale, z + 0.02f, 0.62f * scale, 0.58f * scale, 0.65f * scale,
            Color.FromArgb(255, 94, 181, 139),
            Color.FromArgb(255, 51, 126, 104),
            Color.FromArgb(255, 33, 91, 90),
            bobAmplitude: 0.025f,
            bobSpeed: 0.9f,
            bobPhase: x + 0.7f);
    }

    private void AddCrystal(float x, float y, float z, float scale)
    {
        var baseY = y;
        var bottomLeft = new Vector3(x - 0.28f * scale, baseY, z);
        var bottomRight = new Vector3(x + 0.28f * scale, baseY, z);
        var bottomBack = new Vector3(x, baseY, z + 0.36f * scale);
        var tip = new Vector3(x, baseY + 1.2f * scale, z + 0.12f * scale);
        var center = new Vector3(x, baseY + 0.26f * scale, z + 0.12f * scale);
        var rotationCenter = new Vector3(x, baseY + 0.55f * scale, z + 0.12f * scale);

        AddTriangle(tip, bottomLeft, center, Color.FromArgb(210, 115, 145, 255), rotationCenter, 0.035f, 0.4f, 0.025f, 1.2f);
        AddTriangle(tip, center, bottomRight, Color.FromArgb(220, 91, 110, 229), rotationCenter, 0.035f, 0.4f, 0.025f, 1.2f);
        AddTriangle(tip, bottomRight, bottomBack, Color.FromArgb(205, 72, 81, 191), rotationCenter, 0.035f, 0.4f, 0.025f, 1.2f);
        AddTriangle(tip, bottomBack, bottomLeft, Color.FromArgb(225, 146, 166, 255), rotationCenter, 0.035f, 0.4f, 0.025f, 1.2f);
    }

    private void AddRotatingCube(float x, float y, float z, float size,
        Color topColor, float rotationSpeed, float rotationPhase, float bobAmplitude, float bobSpeed)
    {
        AddCubeMesh(x, y, z, size, size, size,
            topColor,
            Color.FromArgb(255, Math.Max(0, topColor.R - 35), Math.Max(0, topColor.G - 35), Math.Max(0, topColor.B - 25)),
            Color.FromArgb(255, Math.Max(0, topColor.R - 62), Math.Max(0, topColor.G - 48), Math.Max(0, topColor.B - 28)),
            rotationSpeed: rotationSpeed,
            rotationPhase: rotationPhase,
            bobAmplitude: bobAmplitude,
            bobSpeed: bobSpeed,
            bobPhase: rotationPhase * 0.6f);
    }

    private void AddCubeMesh(float x, float y, float z, float sizeX, float sizeY, float sizeZ,
        Color topColor, Color frontColor, Color sideColor,
        float rotationSpeed = 0f, float rotationPhase = 0f,
        float bobAmplitude = 0f, float bobSpeed = 0f, float bobPhase = 0f)
    {
        var x0 = x;
        var x1 = x + sizeX;
        var y0 = y;
        var y1 = y + sizeY;
        var z0 = z;
        var z1 = z + sizeZ;

        var vertices = new[]
        {
            new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y1, z0), new Vector3(x0, y1, z0),
            new Vector3(x0, y0, z1), new Vector3(x1, y0, z1), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1)
        };

        var pivot = new Vector3(x + sizeX / 2f, y + sizeY / 2f, z + sizeZ / 2f);
        AddMeshFace(new[] { vertices[3], vertices[2], vertices[6], vertices[7] }, topColor,
            pivot, rotationSpeed, rotationPhase, bobAmplitude, bobSpeed, bobPhase);
        AddMeshFace(new[] { vertices[0], vertices[1], vertices[2], vertices[3] }, frontColor,
            pivot, rotationSpeed, rotationPhase, bobAmplitude, bobSpeed, bobPhase);
        AddMeshFace(new[] { vertices[1], vertices[5], vertices[6], vertices[2] }, sideColor,
            pivot, rotationSpeed, rotationPhase, bobAmplitude, bobSpeed, bobPhase);
    }

    private void AddTriangle(Vector3 a, Vector3 b, Vector3 c, Color color,
        Vector3 rotationCenter, float rotationSpeed, float rotationPhase,
        float bobAmplitude, float bobSpeed)
    {
        AddMeshFace(new[] { a, b, c }, color, rotationCenter,
            rotationSpeed, rotationPhase, bobAmplitude, bobSpeed, rotationPhase);
    }

    private void AddMeshFace(Vector3[] vertices, Color color, Vector3 rotationCenter,
        float rotationSpeed, float rotationPhase, float bobAmplitude, float bobSpeed, float bobPhase)
    {
        var brushKey = color.ToArgb();
        if (!materialBrushes.TryGetValue(brushKey, out var brush))
        {
            brush = new SolidBrush(color);
            materialBrushes.Add(brushKey, brush);
        }

        faces.Add(new RenderFace(
            vertices,
            new PointF[vertices.Length],
            brush,
            rotationCenter,
            rotationSpeed,
            rotationPhase,
            bobAmplitude,
            bobSpeed,
            bobPhase));
    }

    private void ProjectFace(RenderFace face, float yaw, float pitch, int width, int height)
    {
        var depthTotal = 0f;
        var rotation = face.RotationSpeed == 0f ? 0f : elapsed * face.RotationSpeed + face.RotationPhase;
        var bob = face.BobAmplitude == 0f ? 0f : MathF.Sin(elapsed * face.BobSpeed + face.BobPhase) * face.BobAmplitude;

        for (var index = 0; index < face.Vertices.Length; index++)
        {
            var vertex = face.Vertices[index];
            if (rotation != 0f)
                vertex = RotateY(vertex, face.RotationCenter, rotation);
            if (bob != 0f)
                vertex.Y += bob;

            face.Points[index] = Project(vertex, yaw, pitch, width, height, out var depth);
            depthTotal += depth;
        }

        face.Depth = depthTotal / face.Vertices.Length;
    }

    private PointF Project(Vector3 point, float yaw, float pitch, int width, int height, out float depth)
    {
        const float cameraY = 2.65f;
        const float cameraZ = -8.7f;

        var dx = point.X;
        var dy = point.Y - cameraY;
        var dz = point.Z - cameraZ;

        var cosYaw = MathF.Cos(yaw);
        var sinYaw = MathF.Sin(yaw);
        var rotatedX = dx * cosYaw - dz * sinYaw;
        var rotatedZ = dx * sinYaw + dz * cosYaw;

        var cosPitch = MathF.Cos(pitch);
        var sinPitch = MathF.Sin(pitch);
        var rotatedY = dy * cosPitch - rotatedZ * sinPitch;
        depth = dy * sinPitch + rotatedZ * cosPitch;

        depth = Math.Max(2.2f, depth);
        var perspective = FocalLength / depth;
        var anchorX = width * 0.705f;
        var anchorY = height * 0.72f;

        return new PointF(
            anchorX + rotatedX * perspective,
            anchorY - rotatedY * perspective);
    }

    private void DrawAtmosphere(Graphics graphics, int width, int height)
    {
        foreach (var particle in particles)
        {
            var drift = (elapsed * particle.Speed * 0.018f + particle.Phase) % 1f;
            var x = particle.X * width + MathF.Sin(elapsed * 0.4f + particle.Phase) * 9f;
            var y = height * (0.22f + (particle.Y * 0.62f + drift * 0.06f) % 0.68f);
            var radius = particle.Radius * (0.55f + 0.45f * MathF.Sin(elapsed * 1.3f + particle.Phase));
            var alpha = (int)(35 + 75 * (0.5f + 0.5f * MathF.Sin(elapsed * 1.7f + particle.Phase)));

            graphics.FillEllipse(particleBrushes[Math.Clamp(alpha / 16, 0, particleBrushes.Length - 1)],
                x, y, radius * 2.2f, radius * 2.2f);
        }

        using var vignettePath = new GraphicsPath();
        vignettePath.AddRectangle(new Rectangle(0, 0, width, height));
        using var vignette = new PathGradientBrush(vignettePath)
        {
            CenterPoint = new PointF(width * 0.68f, height * 0.47f),
            CenterColor = Color.FromArgb(0, 0, 0, 0),
            SurroundColors = new[] { Color.FromArgb(105, 3, 5, 15) }
        };
        graphics.FillRectangle(vignette, 0, 0, width, height);
        using var bottomShade = new LinearGradientBrush(
            new Rectangle(0, (int)(height * 0.72f), width, Math.Max(1, height / 3)),
            Color.FromArgb(0, 8, 11, 24),
            Color.FromArgb(120, 8, 11, 24),
            LinearGradientMode.Vertical);
        graphics.FillRectangle(bottomShade, 0, height * 0.72f, width, height * 0.28f);
    }

    private void DrawTypography(Graphics graphics, int width, int height)
    {
        var left = Math.Max(25f, width * 0.045f);
        var titleY = height * 0.20f;

        using var eyebrowFont = new Font("Segoe UI Semibold", Math.Clamp(height * 0.040f, 8f, 10f), FontStyle.Bold, GraphicsUnit.Pixel);
        using var titleFont = new Font("Segoe UI Semibold", Math.Clamp(height * 0.125f, 26f, 36f), FontStyle.Bold, GraphicsUnit.Pixel);
        using var subtitleFont = new Font("Segoe UI", Math.Clamp(height * 0.047f, 10f, 13f), FontStyle.Regular, GraphicsUnit.Pixel);
        using var detailFont = new Font("Segoe UI Semibold", Math.Clamp(height * 0.034f, 8f, 9.5f), FontStyle.Bold, GraphicsUnit.Pixel);

        using var eyebrowBrush = new SolidBrush(Color.FromArgb(185, 184, 197, 229));
        using var titleBrush = new SolidBrush(Color.FromArgb(250, 247, 248, 255));
        using var subtitleBrush = new SolidBrush(Color.FromArgb(214, 200, 210, 235));
        using var detailBrush = new SolidBrush(Color.FromArgb(168, 160, 180, 214));

        graphics.DrawString("A NEW WORLD BEGINS HERE", eyebrowFont, eyebrowBrush, left, titleY);
        graphics.DrawString("NovaCraft", titleFont, titleBrush, left - 1, titleY + height * 0.075f);
        graphics.DrawString("Твой мир. Твои правила.", subtitleFont, subtitleBrush, left + 2, titleY + height * 0.235f);

        var chipY = height * 0.82f;
        var chipText = $"{GameVersion}  ·  {ProfileName}";
        graphics.DrawString(chipText, detailFont, detailBrush, left + 2, chipY);
    }

    private void DrawPlayButton(Graphics graphics, int width, int height)
    {
        var buttonWidth = Math.Clamp(width * 0.205f, 164f, 212f);
        var buttonHeight = Math.Clamp(height * 0.17f, 40f, 48f);
        var left = Math.Max(25f, width * 0.045f);
        var top = height * 0.64f - buttonHeight / 2f;
        var scale = 1f + buttonHover * 0.035f - (pointerPressed ? 0.018f : 0f);
        var scaledWidth = buttonWidth * scale;
        var scaledHeight = buttonHeight * scale;
        var centerX = left + buttonWidth / 2f;
        var centerY = top + buttonHeight / 2f;

        playBounds = new RectangleF(
            centerX - scaledWidth / 2f,
            centerY - scaledHeight / 2f,
            scaledWidth,
            scaledHeight);

        var glowAlpha = (int)(34 + buttonHover * 48);
        using (var glow = new Pen(Color.FromArgb(glowAlpha, 145, 117, 255), 8f))
        using (var glowPath = RoundedPath(playBounds, 12f))
            graphics.DrawPath(glow, glowPath);

        var topColor = Lerp(Color.FromArgb(122, 91, 255), Color.FromArgb(151, 124, 255), buttonHover);
        var bottomColor = Lerp(Color.FromArgb(77, 54, 194), Color.FromArgb(105, 81, 227), buttonHover);
        using (var buttonBrush = new LinearGradientBrush(playBounds, topColor, bottomColor, LinearGradientMode.Vertical))
        using (var path = RoundedPath(playBounds, 12f))
            graphics.FillPath(buttonBrush, path);

        using (var border = new Pen(Color.FromArgb(105 + (int)(buttonHover * 70), 224, 216, 255), 1f))
        using (var path = RoundedPath(playBounds, 12f))
            graphics.DrawPath(border, path);

        var center = new PointF(playBounds.Left + playBounds.Width * 0.31f, playBounds.Top + playBounds.Height / 2f);
        PointF[] triangle =
        {
            new(center.X - 4f, center.Y - 8f),
            new(center.X - 4f, center.Y + 8f),
            new(center.X + 9f, center.Y)
        };
        using (var playBrush = new SolidBrush(Color.White))
            graphics.FillPolygon(playBrush, triangle);

        using var textFont = new Font("Segoe UI Semibold", 12f, FontStyle.Bold, GraphicsUnit.Point);
        using var textBrush = new SolidBrush(Color.White);
        using var textFormat = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        graphics.DrawString("ИГРАТЬ", textFont, textBrush,
            new RectangleF(playBounds.Left + 22f, playBounds.Top, playBounds.Width - 22f, playBounds.Height), textFormat);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var width = Math.Max(1, ClientSize.Width);
        var height = Math.Max(1, ClientSize.Height);

        targetCameraX = Math.Clamp((e.X / (float)width - 0.5f) * 2f, -1f, 1f);
        targetCameraY = Math.Clamp((e.Y / (float)height - 0.5f) * 2f, -1f, 1f);

        var inside = playBounds.Contains(e.Location);
        if (inside != pointerInsideButton)
        {
            pointerInsideButton = inside;
            Cursor = inside ? Cursors.Hand : Cursors.Default;
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        targetCameraX = 0f;
        targetCameraY = 0f;
        pointerInsideButton = false;
        Cursor = Cursors.Default;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && playBounds.Contains(e.Location))
        {
            pointerPressed = true;
            Capture = true;
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        var shouldLaunch = pointerPressed && e.Button == MouseButtons.Left && playBounds.Contains(e.Location);
        pointerPressed = false;
        Capture = false;
        if (shouldLaunch)
            PlayRequested?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            e.SuppressKeyPress = true;
            PlayRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Invalidate();
    }

    private static GraphicsPath RoundedPath(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2f;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static Vector3 RotateY(Vector3 point, Vector3 center, float angle)
    {
        var relative = point - center;
        var cos = MathF.Cos(angle);
        var sin = MathF.Sin(angle);
        return new Vector3(
            center.X + relative.X * cos - relative.Z * sin,
            point.Y,
            center.Z + relative.X * sin + relative.Z * cos);
    }

    private static Color Lerp(Color from, Color to, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        return Color.FromArgb(
            (int)(from.A + (to.A - from.A) * amount),
            (int)(from.R + (to.R - from.R) * amount),
            (int)(from.G + (to.G - from.G) * amount),
            (int)(from.B + (to.B - from.B) * amount));
    }

    private static float UnitHash(float value)
    {
        var result = MathF.Sin(value * 127.1f + 311.7f) * 43758.5453f;
        return result - MathF.Floor(result);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            animationTimer.Stop();
            animationTimer.Dispose();
            faceEdgePen.Dispose();
            foreach (var brush in materialBrushes.Values)
                brush.Dispose();
            foreach (var brush in particleBrushes)
                brush?.Dispose();
            foreach (var brush in starBrushes)
                brush?.Dispose();
        }
        base.Dispose(disposing);
    }

    private sealed class RenderFace
    {
        public Vector3[] Vertices { get; }
        public PointF[] Points { get; }
        public SolidBrush Brush { get; }
        public Vector3 RotationCenter { get; }
        public float RotationSpeed { get; }
        public float RotationPhase { get; }
        public float BobAmplitude { get; }
        public float BobSpeed { get; }
        public float BobPhase { get; }
        public float Depth { get; set; }

        public RenderFace(Vector3[] vertices, PointF[] points, SolidBrush brush,
            Vector3 rotationCenter, float rotationSpeed, float rotationPhase,
            float bobAmplitude, float bobSpeed, float bobPhase)
        {
            Vertices = vertices;
            Points = points;
            Brush = brush;
            RotationCenter = rotationCenter;
            RotationSpeed = rotationSpeed;
            RotationPhase = rotationPhase;
            BobAmplitude = bobAmplitude;
            BobSpeed = bobSpeed;
            BobPhase = bobPhase;
        }
    }

    private sealed record Particle(float X, float Y, float Radius, float Speed, float Phase);
}
