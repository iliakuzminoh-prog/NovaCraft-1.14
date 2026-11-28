using System.Drawing.Drawing2D;

namespace NovaCraft.UI;

internal static class AppTheme
{
    internal static readonly Color Canvas = Color.FromArgb(12, 15, 25);
    internal static readonly Color TopBar = Color.FromArgb(18, 22, 36);
    internal static readonly Color Sidebar = Color.FromArgb(14, 18, 30);
    internal static readonly Color Surface = Color.FromArgb(23, 29, 45);
    internal static readonly Color SurfaceRaised = Color.FromArgb(29, 36, 55);
    internal static readonly Color Input = Color.FromArgb(27, 34, 51);
    internal static readonly Color Border = Color.FromArgb(54, 66, 91);
    internal static readonly Color Accent = Color.FromArgb(124, 92, 255);
    internal static readonly Color AccentHover = Color.FromArgb(143, 113, 255);
    internal static readonly Color Text = Color.FromArgb(238, 242, 250);
    internal static readonly Color Muted = Color.FromArgb(153, 166, 190);
    internal static readonly Color Success = Color.FromArgb(76, 212, 157);
    internal static readonly Color Danger = Color.FromArgb(226, 104, 126);

    internal static void Apply(Control root)
    {
        ArgumentNullException.ThrowIfNull(root);

        if (root is TabPage)
        {
            root.BackColor = Canvas;
            root.ForeColor = Text;
        }

        foreach (Control control in root.Controls)
        {
            switch (control)
            {
                case TextBoxBase textBox:
                    textBox.BackColor = Input;
                    textBox.ForeColor = Text;
                    textBox.BorderStyle = BorderStyle.FixedSingle;
                    textBox.Font = NormalizeFont(textBox.Font, 9.5f);
                    textBox.Padding = new Padding(8, 5, 8, 5);
                    break;

                case ComboBox combo:
                    combo.BackColor = Input;
                    combo.ForeColor = Text;
                    combo.FlatStyle = FlatStyle.Flat;
                    combo.Font = NormalizeFont(combo.Font, 9.5f);
                    break;

                case NumericUpDown numeric:
                    numeric.BackColor = Input;
                    numeric.ForeColor = Text;
                    numeric.Font = NormalizeFont(numeric.Font, 9.5f);
                    break;

                case CheckBox checkBox:
                    checkBox.ForeColor = Text;
                    checkBox.Font = NormalizeFont(checkBox.Font, 9.25f);
                    checkBox.Cursor = Cursors.Hand;
                    break;

                case ListBox listBox:
                    listBox.BackColor = SurfaceRaised;
                    listBox.ForeColor = Text;
                    listBox.BorderStyle = BorderStyle.FixedSingle;
                    listBox.Font = NormalizeFont(listBox.Font, 9.5f);
                    break;

                case ListView listView:
                    listView.BackColor = SurfaceRaised;
                    listView.ForeColor = Text;
                    listView.BorderStyle = BorderStyle.None;
                    listView.Font = NormalizeFont(listView.Font, 9.5f);
                    break;

                case Button button:
                    button.FlatStyle = FlatStyle.Flat;
                    button.FlatAppearance.BorderColor = Border;
                    button.FlatAppearance.MouseOverBackColor = Color.FromArgb(42, 51, 72);
                    button.Cursor = Cursors.Hand;
                    button.Font = NormalizeFont(button.Font, 9.25f);

                    if (button.Tag is string marker && marker == "novacraft-primary")
                    {
                        button.BackColor = Accent;
                        button.ForeColor = Color.White;
                        button.FlatAppearance.BorderSize = 0;
                        button.FlatAppearance.MouseOverBackColor = AccentHover;
                    }
                    else if (button.BackColor == SystemColors.Control)
                    {
                        button.BackColor = SurfaceRaised;
                        button.ForeColor = Text;
                        button.FlatAppearance.BorderSize = 1;
                    }
                    break;

                case Label label when label.ForeColor == SystemColors.ControlText:
                    label.ForeColor = Muted;
                    break;
            }

            if (control.HasChildren)
                Apply(control);
        }
    }

    internal static void StyleCard(Panel card, bool selected = false)
    {
        ArgumentNullException.ThrowIfNull(card);

        card.BackColor = selected
            ? Color.FromArgb(39, 34, 69)
            : Surface;
        card.Tag = "novacraft-card";
        card.Paint += (_, e) => PaintCardBorder(card, e.Graphics, selected);
        card.Resize += (_, _) => InvalidateCard(card);
    }

    internal static void StyleCatalogCard(Panel card)
    {
        ArgumentNullException.ThrowIfNull(card);

        card.BackColor = Surface;
        card.Tag = "novacraft-catalog-card";
        card.Paint += (_, e) => PaintCardBorder(card, e.Graphics, false);
        card.Resize += (_, _) => InvalidateCard(card);
    }

    private static void PaintCardBorder(Control control, Graphics graphics, bool selected)
    {
        if (control.ClientSize.Width < 4 || control.ClientSize.Height < 4)
            return;

        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var bounds = new RectangleF(
            0.75f,
            0.75f,
            control.ClientSize.Width - 1.5f,
            control.ClientSize.Height - 1.5f);
        const float radius = 15f;

        using var path = RoundedRectangle(bounds, radius);
        using var border = new Pen(
            selected ? Color.FromArgb(114, 94, 204) : Border,
            selected ? 1.4f : 1f)
        {
            Alignment = PenAlignment.Inset
        };

        graphics.DrawPath(border, path);

        if (selected)
        {
            using var highlight = new Pen(Color.FromArgb(65, Accent), 1f);
            var topLine = new PointF[]
            {
                new(bounds.Left + radius, bounds.Top + 1),
                new(bounds.Right - radius, bounds.Top + 1)
            };
            graphics.DrawLines(highlight, topLine);
        }
    }

    private static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
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

    private static Font NormalizeFont(Font original, float fallbackSize)
    {
        if (original.Size >= 9f)
            return original;

        return new Font("Segoe UI", fallbackSize, original.Style, GraphicsUnit.Point);
    }

    private static void InvalidateCard(Control control)
    {
        if (!control.IsDisposed && control.IsHandleCreated)
            control.Invalidate();
    }
}
