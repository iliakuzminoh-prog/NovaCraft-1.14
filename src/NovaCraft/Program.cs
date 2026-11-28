using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.IO.Compression;
using System.Diagnostics;
using CmlLib.Core;
using NovaCraft.Services;
using CmlLib.Core.Auth;
using CmlLib.Core.ProcessBuilder;
using CmlLib.Core.ModLoaders.FabricMC;
using CmlLib.Core.Installer.Forge;
using CmlLib.Core.Installers;
using CmlLib.Core.ModLoaders.QuiltMC;

namespace NovaCraft;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => AppDiagnostics.Report(e.Exception, "UI");
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                AppDiagnostics.Report(ex, "AppDomain");
        };
        try
        {
            if (args.Any(a => string.Equals(a, "--smoke-test", StringComparison.OrdinalIgnoreCase)))
            {
                using var smoke = new MainForm();
                smoke.CreateControl();
                smoke.PerformLayout();
                return;
            }

            Application.Run(new MainForm());
        }
        catch (Exception ex)
        {
            AppDiagnostics.Report(ex, "Main");
            MessageBox.Show(
                "NovaCraft не смог запуститься. Подробный отчёт сохранён в папке NovaCraft в LocalAppData.\n\n" + ex.Message,
                "NovaCraft — ошибка запуска",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}

internal sealed class OfflineProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Username { get; set; } = "NovaPlayer";
    public string Uuid { get; set; } = Guid.NewGuid().ToString();
    public string SkinPath { get; set; } = "";
    public bool Slim { get; set; }
}

internal sealed class LauncherSettings
{
    public int SchemaVersion { get; set; } = 2;
    public string GameDirectory { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NovaCraft", "minecraft");
    public string Username { get; set; } = "NovaPlayer";
    public string ActiveProfileId { get; set; } = "";
    public List<OfflineProfile> OfflineProfiles { get; set; } = new();
    public int MaximumRamMb { get; set; } = 2048;
    public bool FpsBoost { get; set; }
    public string LastVersion { get; set; } = "1.21.1";
    public string ModLoader { get; set; } = "fabric";
    public int ScreenWidth { get; set; } = 1280;
    public int ScreenHeight { get; set; } = 720;
    public bool FullScreen { get; set; }
    public bool CloseAfterLaunch { get; set; }
    public bool AutoRepair { get; set; } = true;
    public string ExtraJvmArguments { get; set; } = "";
    public List<string> SavedBuilds { get; set; } = new();
    public List<string> FavoriteServers { get; set; } = new();
}

internal sealed class HiddenTabControl : TabControl
{
    const int TcmAdjustRect = 0x1328;

    public HiddenTabControl()
    {
        Appearance = TabAppearance.FlatButtons;
        SizeMode = TabSizeMode.Fixed;
        ItemSize = new Size(1, 1);
        Multiline = true;
        Padding = new Point(0, 0);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == TcmAdjustRect)
        {
            m.Result = IntPtr.Zero;
            return;
        }
        base.WndProc(ref m);
    }
}

internal sealed class MainForm : Form
{
    readonly LauncherSettings settings;
    MinecraftLauncher? launcher;
    readonly TextBox username = new();
    readonly ComboBox version = new();
    readonly ComboBox gameLoader = new();
    readonly NumericUpDown ram = new();
    readonly TextBox gameDir = new();
    readonly CheckBox boost = new();
    readonly RichTextBox log = new();
    readonly Label status = new();
    readonly HiddenTabControl pageHost = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(18, 19, 23) };
    readonly List<TabPage> navigationPages = new();
    readonly List<Button> navigationButtons = new();
    readonly List<Func<TabPage>> navigationFactories = new();
    readonly List<bool> navigationInitialized = new();
    TextBox? customMusicPath;
    NumericUpDown? widthInput;
    NumericUpDown? heightInput;
    CheckBox? fullScreenInput;
    CheckBox? closeAfterLaunchInput;
    CheckBox? autoRepairInput;
    TextBox? jvmArgsInput;
    readonly Button play = new();
    readonly Button install = new();
    readonly FlowLayoutPanel popularFeed = new() { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(8), BackColor = Color.FromArgb(18, 19, 23) };
    readonly FlowLayoutPanel modCards = new() { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(8), BackColor = Color.FromArgb(18, 19, 23) };
    readonly ComboBox modLoader = new();
    string? selectedModProject;
    string? selectedModTitle;
    string? selectedDatapackWorldDirectory;
    string modFeedQuery = "";
    string modFeedSort = "downloads";
    int modFeedOffset;
    bool modFeedLoading;
    readonly HttpClient http = new(new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All }) { Timeout = TimeSpan.FromMinutes(3) };

    public MainForm()
    {
        settings = SettingsService.Load();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("NovaCraft/1.14 (https://github.com/iliakuzminoh-prog/NovaCraft-1.14)");
        // Не создаём MinecraftLauncher в конструкторе: старый/недоступный путь игры
        // не должен блокировать запуск самого лаунчера. Он создаётся только при
        // установке/запуске игры, когда папка уже проверена.
        try
        {
            Directory.CreateDirectory(settings.GameDirectory);
        }
        catch (Exception ex)
        {
            AppDiagnostics.Report(ex, "Settings.GameDirectory");
            settings.GameDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NovaCraft",
                "minecraft");
            Directory.CreateDirectory(settings.GameDirectory);
        }

        Text = "NovaCraft 1.14";
        username.ReadOnly = true;
        username.TabStop = false;
        Width = 1180; Height = 760; MinimumSize = new Size(980, 650);
        try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        BackColor = Color.FromArgb(18, 19, 23); ForeColor = Color.White;
        Font = new Font("Segoe UI", 10); StartPosition = FormStartPosition.CenterScreen;

        BuildUi();

        // Лаунчер должен открываться даже при недоступном интернете.
        // Сеть используется уже после отображения главного окна.
        if (version.Items.Count == 0 && !string.IsNullOrWhiteSpace(settings.LastVersion))
        {
            version.Items.Add(settings.LastVersion);
            version.SelectedIndex = 0;
        }

        Shown += (_, _) =>
        {
            status.Text = "Готов. Проверяю список версий в фоне…";
            _ = RefreshVersionsAsync();
        };
    }

    static Bitmap LoadHeroArt()
    {
        var bitmap = new Bitmap(1100, 235);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var background = new System.Drawing.Drawing2D.LinearGradientBrush(
            new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            Color.FromArgb(48, 30, 92),
            Color.FromArgb(20, 24, 54),
            15f);
        graphics.FillRectangle(background, 0, 0, bitmap.Width, bitmap.Height);
        using var glow = new SolidBrush(Color.FromArgb(45, 128, 91, 235));
        graphics.FillEllipse(glow, bitmap.Width - 330, -120, 420, 420);
        using var glow2 = new SolidBrush(Color.FromArgb(35, 64, 180, 255));
        graphics.FillEllipse(glow2, bitmap.Width - 160, 100, 260, 260);
        return bitmap;
    }

    static void RoundCorners(Control control, int radius = 14)
    {
        void Apply()
        {
            if (control.Width <= 0 || control.Height <= 0) return;
            using var path = new System.Drawing.Drawing2D.GraphicsPath();
            var d = radius * 2;
            path.AddArc(0, 0, d, d, 180, 90);
            path.AddArc(control.Width - d, 0, d, d, 270, 90);
            path.AddArc(control.Width - d, control.Height - d, d, d, 0, 90);
            path.AddArc(0, control.Height - d, d, d, 90, 90);
            path.CloseFigure();
            control.Region = new Region(path);
        }
        control.Resize += (_, _) => Apply();
        Apply();
    }

    static Panel ModernCard(int width, int height, string title, Color backColor)
    {
        var card = new Panel
        {
            Width = width,
            Height = height,
            BackColor = backColor,
            Margin = new Padding(7),
            Padding = new Padding(18)
        };
        RoundCorners(card, 16);

        var accent = new Panel
        {
            Location = new Point(18, 14),
            Size = new Size(4, 22),
            BackColor = Color.FromArgb(112, 74, 242)
        };
        RoundCorners(accent, 2);

        card.Controls.Add(accent);
        card.Controls.Add(new Label
        {
            Text = title,
            AutoSize = true,
            Location = new Point(32, 14),
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold)
        });
        return card;
    }

    static Button ModernButton(string text, int width, int height)
    {
        var b = new Button
        {
            Text = text,
            Width = width,
            Height = height,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(92, 48, 224),
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
            Cursor = Cursors.Hand,
            Padding = new Padding(10, 0, 10, 0)
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(111, 70, 235);
        b.FlatAppearance.MouseDownBackColor = Color.FromArgb(76, 39, 185);
        RoundCorners(b, 10);
        return b;
    }

    static Bitmap CreateNavIcon(string glyph)
    {
        var bitmap = new Bitmap(24, 24);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);
        using var brush = new SolidBrush(Color.FromArgb(214, 220, 235));
        using var font = new Font("Segoe UI Symbol", 13, FontStyle.Bold, GraphicsUnit.Point);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        graphics.DrawString(glyph, font, brush, new RectangleF(0, 0, 24, 24), format);
        return bitmap;
    }

    void BuildUi()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 76, BackColor = Color.FromArgb(17, 19, 30), Padding = new Padding(18, 8, 18, 8) };
        header.Controls.Add(new Label { Text = "◆  NOVACRAFT", Font = new Font("Segoe UI Semibold", 21, FontStyle.Bold), AutoSize = true, Location = new Point(18, 13), ForeColor = Color.White });
        header.Controls.Add(new Label { Text = "Minecraft Java · лаунчер без рекламы и фоновых служб", AutoSize = true, Location = new Point(250, 26), ForeColor = Color.FromArgb(160, 170, 190) });

        var sidebar = new Panel { Dock = DockStyle.Left, Width = 252, BackColor = Color.FromArgb(15, 17, 27), Padding = new Padding(12, 14, 12, 10) };
        var navSearch = new TextBox { PlaceholderText = "⌕  Поиск раздела…", Dock = DockStyle.Top, Height = 36, BackColor = Color.FromArgb(28,31,47), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        sidebar.Controls.Add(navSearch);
        var navScroll = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Color.Transparent };
        sidebar.Controls.Add(navScroll);

        var pages = new (string Name, Func<TabPage> Factory)[]
        {
            ("⌂  Главная", HomePage),
            ("◈  Версии", VersionsPage),
            ("▣  Сборки", BuildsPage),
            ("✦  Моды", ModsPage),
            ("▧  Ресурспаки", () => CatalogPage("Ресурспаки Modrinth", "resourcepack")),
            ("✧  Шейдеры", () => CatalogPage("Шейдеры Modrinth", "shader")),
            ("▦  Датапаки", () => CatalogPage("Датапаки Modrinth", "datapack")),
            ("▤  Каталог", () => CatalogPage("Сборки Modrinth", "modpack")),
            ("♫  Музыка", MusicPage),
            ("♙  Скины", AccountPage),
            ("⚡  Производительность", PerformancePage),
            ("◆  Сервера", ServersPage),
            ("⚙  Настройки", SettingsPage),
            ("✓  Исправление", DiagnosticsPage)
        };

        var sectionLabels = new Dictionary<int, string>
        {
            [0] = "ИГРА",
            [3] = "КОНТЕНТ",
            [8] = "ИНСТРУМЕНТЫ"
        };

        foreach (var item in pages.Select((value, index) => (value, index)))
        {
            if (sectionLabels.TryGetValue(item.index, out var sectionTitle))
            {
                navScroll.Controls.Add(new Label
                {
                    Text = sectionTitle,
                    Width = 226,
                    Height = 24,
                    Margin = new Padding(4, item.index == 0 ? 0 : 12, 0, 2),
                    ForeColor = Color.FromArgb(104, 114, 137),
                    Font = new Font("Segoe UI Semibold", 8, FontStyle.Bold),
                    TextAlign = ContentAlignment.BottomLeft
                });
            }

            var page = new TabPage(item.value.Name)
            {
                BackColor = Color.FromArgb(13, 15, 25),
                Padding = new Padding(24)
            };
            navigationFactories.Add(item.value.Factory);
            navigationInitialized.Add(false);
            navigationPages.Add(page);
            pageHost.TabPages.Add(page);

            var parts = item.value.Name.Split(new[] { "  " }, StringSplitOptions.None);
            var glyph = parts.Length > 1 ? parts[0] : "◆";
            var caption = parts.Length > 1 ? parts[1] : item.value.Name;
            var button = new Button
            {
                Text = caption,
                Width = 226,
                Height = 46,
                Margin = new Padding(0, 2, 0, 5),
                TextAlign = ContentAlignment.MiddleLeft,
                Image = CreateNavIcon(glyph),
                ImageAlign = ContentAlignment.MiddleLeft,
                TextImageRelation = TextImageRelation.ImageBeforeText,
                Padding = new Padding(12, 0, 8, 0),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(15, 17, 27),
                ForeColor = Color.FromArgb(198, 204, 220),
                Font = new Font("Segoe UI Semibold", 9.5f)
            };
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(39, 34, 61);
            var index = navigationPages.Count - 1;
            var tooltip = new ToolTip { AutoPopDelay = 5000, InitialDelay = 350, ReshowDelay = 100 };
            tooltip.SetToolTip(button, caption);
            button.MouseEnter += (_, _) =>
            {
                if (pageHost.SelectedIndex != index)
                    button.BackColor = Color.FromArgb(30, 27, 45);
            };
            button.MouseLeave += (_, _) =>
            {
                if (pageHost.SelectedIndex != index)
                    button.BackColor = Color.FromArgb(22, 24, 35);
            };
            button.Click += (_, _) => ShowNavigationPage(index);
            navigationButtons.Add(button);
            navScroll.Controls.Add(button);
        }

        var sideFooter = new Label
        {
            Text = "NOVACRAFT 1.14\nMinecraft Java",
            Dock = DockStyle.Bottom,
            Height = 48,
            ForeColor = Color.FromArgb(88, 97, 119),
            Font = new Font("Segoe UI Semibold", 8, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(10, 0, 0, 0)
        };
        sidebar.Controls.Add(sideFooter);

        status.Text = "Готов";
        status.Dock = DockStyle.Bottom;
        status.Height = 30;
        status.Padding = new Padding(14, 6, 0, 0);
        status.ForeColor = Color.FromArgb(185, 194, 210);
        status.BackColor = Color.FromArgb(24, 26, 34);

        Controls.Add(pageHost);
        Controls.Add(sidebar);
        Controls.Add(header);
        Controls.Add(status);
        ApplyDarkTheme(this);
        navSearch.TextChanged += (_, _) => { var q = navSearch.Text.Trim(); foreach (var button in navigationButtons) button.Visible = string.IsNullOrWhiteSpace(q) || button.Text.Contains(q, StringComparison.OrdinalIgnoreCase); };
        ShowNavigationPage(0);
    }

    void ShowNavigationPage(int index)
    {
        if (index < 0 || index >= navigationPages.Count) return;

        if (!navigationInitialized[index])
        {
            TabPage loaded;
            try
            {
                loaded = navigationFactories[index]();
            }
            catch (Exception ex)
            {
                AppDiagnostics.Report(ex, "Navigation." + navigationPages[index].Text);
                loaded = new TabPage(navigationPages[index].Text)
                {
                    BackColor = Color.FromArgb(13, 15, 25),
                    Padding = new Padding(24)
                };
                loaded.Controls.Add(new Label
                {
                    Text = "Этот раздел временно недоступен.\n\nNovaCraft продолжит работать. Ошибка записана в LocalAppData\\NovaCraft\\logs\\startup-errors.log.",
                    AutoSize = false,
                    Size = new Size(820, 120),
                    ForeColor = Color.OrangeRed,
                    Font = new Font("Segoe UI", 10),
                    Location = new Point(24, 24)
                });
            }

            loaded.Dock = DockStyle.Fill;
            var oldPage = navigationPages[index];
            var selected = pageHost.SelectedIndex == index;
            pageHost.TabPages.RemoveAt(index);
            pageHost.TabPages.Insert(index, loaded);
            navigationPages[index] = loaded;
            navigationInitialized[index] = true;
            oldPage.Dispose();
            if (selected) pageHost.SelectedIndex = index;
        }

        pageHost.SelectedIndex = index;
        for (var i = 0; i < navigationPages.Count; i++)
        {
            navigationButtons[i].BackColor = i == index ? Color.FromArgb(101, 61, 235) : Color.FromArgb(22, 24, 35);
            navigationButtons[i].ForeColor = i == index ? Color.White : Color.FromArgb(190, 198, 215);
        }
    }

    static void ApplyDarkTheme(Control root)
    {
        foreach (Control c in root.Controls)
        {
            if (c is TextBox textBox)
            {
                textBox.BackColor = Color.FromArgb(31, 35, 44);
                textBox.ForeColor = Color.White;
                textBox.BorderStyle = BorderStyle.FixedSingle;
            }
            else if (c is ComboBox combo)
            {
                combo.BackColor = Color.FromArgb(31, 35, 44);
                combo.ForeColor = Color.White;
                combo.FlatStyle = FlatStyle.Flat;
            }
            else if (c is NumericUpDown numeric)
            {
                numeric.BackColor = Color.FromArgb(31, 35, 44);
                numeric.ForeColor = Color.White;
            }
            else if (c is Button button)
            {
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = Color.FromArgb(75, 87, 108);
                if (button.BackColor == SystemColors.Control)
                    button.BackColor = Color.FromArgb(38, 44, 56);
                button.ForeColor = Color.White;
                button.Cursor = Cursors.Hand;
            }
            else if (c is ListView list)
            {
                list.BackColor = Color.FromArgb(27, 31, 39);
                list.ForeColor = Color.White;
            }
            else if (c is ListBox box)
            {
                box.BackColor = Color.FromArgb(27, 31, 39);
                box.ForeColor = Color.White;
            }
            if (c.HasChildren) ApplyDarkTheme(c);
        }
    }

    TabPage HomePage()
    {
        var p = new TabPage("Главная") { BackColor = Color.FromArgb(13, 15, 25), Padding = new Padding(18) };

        var top = new Panel { Dock = DockStyle.Top, Height = 52, BackColor = Color.FromArgb(13, 15, 25) };
        var search = new TextBox
        {
            PlaceholderText = "Поиск модов, сборок и версий…",
            Font = new Font("Segoe UI", 10.5f),
            ForeColor = Color.White,
            BackColor = Color.FromArgb(29, 31, 49),
            BorderStyle = BorderStyle.FixedSingle,
            Location = new Point(8, 7),
            Width = 590,
            Height = 36
        };
        top.Controls.Add(search);
        var settingsButton = new Button { Text = "⚙", Width = 42, Height = 36, Location = new Point(610, 7), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(29,31,49), ForeColor = Color.White };
        settingsButton.FlatAppearance.BorderSize = 0;
        settingsButton.Click += (_, _) => ShowNavigationPage(10);
        top.Controls.Add(settingsButton);

        var body = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(13, 15, 25), Padding = new Padding(0, 8, 0, 0), AutoScroll = true };

        var hero = new Panel { Dock = DockStyle.Top, Height = 235, BackColor = Color.FromArgb(39, 29, 76), Padding = new Padding(28) };
        var artwork = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.StretchImage, BackColor = Color.FromArgb(39,29,76), Image = LoadHeroArt() };
        hero.Controls.Add(artwork);
        var heroShade = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(105, 12, 15, 28) };
        hero.Controls.Add(heroShade);
        var welcome = new Label { Text = "Добро пожаловать в\nNovaCraft!", AutoSize = true, Location = new Point(28, 24), ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 25, FontStyle.Bold) };
        var subtitle = new Label { Text = "Современный лаунчер Minecraft Java", AutoSize = true, Location = new Point(31, 92), ForeColor = Color.FromArgb(224, 225, 240), Font = new Font("Segoe UI", 11) };
        var playHero = new Button { Text = "▶  Играть", Location = new Point(28, 145), Width = 170, Height = 48, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(91, 39, 235), ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 11, FontStyle.Bold) };
        playHero.FlatAppearance.BorderSize = 0;
        playHero.Click += async (_, _) => await LaunchAsync();
        hero.Controls.AddRange(new Control[] { welcome, subtitle, playHero });

        var content = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0, 14, 0, 30), BackColor = Color.FromArgb(13,15,25) };

        var latest = ModernCard(440, 185, "ПОСЛЕДНЯЯ ВЕРСИЯ", Color.FromArgb(25,28,45));
        latest.Controls.Add(new Label { Text = "⬛  " + settings.LastVersion, AutoSize = true, Location = new Point(18, 45), ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 22, FontStyle.Bold) });
        latest.Controls.Add(new Label { Text = "●  Установлена", AutoSize = true, Location = new Point(20, 86), ForeColor = Color.FromArgb(50, 225, 140), Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold) });
        var check = ModernButton("↻  Проверить обновления", 230, 38);
        check.Location = new Point(18, 124);
        check.Click += async (_, _) => await RefreshVersionsAsync();
        latest.Controls.Add(check);

        var quick = ModernCard(440, 185, "БЫСТРЫЙ ЗАПУСК", Color.FromArgb(25,28,45));
        var quickVersion = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(18, 42), Width = 250, Height = 32, BackColor = Color.FromArgb(34,37,58), ForeColor = Color.White };
        quickVersion.Items.Add(settings.LastVersion);
        quickVersion.SelectedIndex = 0;
        var quickLoader = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(18, 82), Width = 250, Height = 32, BackColor = Color.FromArgb(34,37,58), ForeColor = Color.White };
        quickLoader.Items.AddRange(new object[]{"Fabric","Forge","Quilt","Vanilla"});
        quickLoader.SelectedItem = gameLoader.SelectedItem?.ToString() ?? "Fabric";
        var quickPlay = ModernButton("▶  Запустить", 250, 38);
        quickPlay.Location = new Point(18, 126);
        quickPlay.Click += async (_, _) =>
        {
            if (quickVersion.SelectedItem is string v) version.SelectedItem = v;
            if (quickLoader.SelectedItem is string l && gameLoader.Items.Contains(l)) gameLoader.SelectedItem = l;
            await LaunchAsync();
        };
        quick.Controls.AddRange(new Control[]{quickVersion,quickLoader,quickPlay});

        var buildsCard = ModernCard(440, 185, "ПОПУЛЯРНЫЕ СБОРКИ", Color.FromArgb(25,28,45));
        var buildFlow = new FlowLayoutPanel { Location = new Point(14, 43), Size = new Size(408, 120), AutoScroll = true, WrapContents = false, BackColor = Color.Transparent };
        if (settings.SavedBuilds.Count == 0)
        {
            buildFlow.Controls.Add(new Label { Text = "Здесь появятся твои сохранённые профили.\nСоздай первую сборку во вкладке «Сборки».", AutoSize = true, ForeColor = Color.Silver, Padding = new Padding(10,15,0,0) });
        }
        else
        {
            foreach (var saved in settings.SavedBuilds.Take(4))
            {
                var parts = saved.Split('|');
                var b = new Panel { Width = 175, Height = 100, BackColor = Color.FromArgb(33,37,61), Margin = new Padding(4) };
                b.Controls.Add(new Label { Text = parts[0], Location = new Point(10,10), Size = new Size(150,40), ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 10, FontStyle.Bold), AutoEllipsis = true });
                b.Controls.Add(new Label { Text = parts.Length>1 ? parts[1] : "", Location = new Point(10,54), AutoSize = true, ForeColor = Color.FromArgb(170,180,205) });
                buildFlow.Controls.Add(b);
            }
        }
        buildsCard.Controls.Add(buildFlow);

        var modsCard = ModernCard(440, 185, "НОВЫЕ МОДЫ", Color.FromArgb(25,28,45));
        var modInfo = new Label { Text = "Каталог Modrinth\nИконки, авторы, описания и загрузки\nзагружаются из реального каталога.", AutoSize = true, Location = new Point(18, 46), ForeColor = Color.FromArgb(195,200,220), Font = new Font("Segoe UI", 10) };
        modsCard.Controls.Add(modInfo);
        var modsOpen = ModernButton("Открыть каталог →", 180, 38);
        modsOpen.Location = new Point(18, 122);
        modsOpen.Click += (_, _) => ShowNavigationPage(3);
        modsCard.Controls.Add(modsOpen);

        content.Controls.AddRange(new Control[] { latest, quick, buildsCard, modsCard });
        body.Controls.Add(content);
        p.Controls.Add(body);
        p.Controls.Add(top);
        return p;
    }

    TabPage VersionsPage()
    {
        var p = new TabPage("Версии") { BackColor = Color.FromArgb(13,15,25), Padding = new Padding(24) };

        p.Controls.Add(new Label
        {
            Text = "Версии Minecraft",
            AutoSize = true,
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 25, FontStyle.Bold),
            Location = new Point(24,18)
        });
        p.Controls.Add(new Label
        {
            Text = "Быстрый выбор версии с понятным статусом и запуском в один клик.",
            AutoSize = true,
            ForeColor = Color.FromArgb(151,161,183),
            Location = new Point(27,61)
        });

        var search = new TextBox
        {
            PlaceholderText = "⌕  Поиск версии…",
            Location = new Point(27,96),
            Width = 420,
            Height = 38
        };

        var refresh = ModernButton("↻  Обновить", 120, 38);
        refresh.Location = new Point(455,96);
        refresh.Click += async (_,_) => await RefreshVersionsAsync();

        var selected = new Label
        {
            Text = "Выбрано: " + settings.LastVersion,
            AutoSize = true,
            ForeColor = Color.FromArgb(145,155,178),
            Location = new Point(600,106)
        };

        var list = new ListView
        {
            Location = new Point(27,150),
            Size = new Size(900,480),
            View = View.Tile,
            TileSize = new Size(850,72),
            FullRowSelect = true,
            HideSelection = false,
            BorderStyle = BorderStyle.None,
            BackColor = Color.FromArgb(18,21,34),
            ForeColor = Color.White
        };

        void Render(string query)
        {
            var wanted = query.Trim();
            var versions = list.Tag as List<string> ?? new List<string>();
            list.BeginUpdate();
            list.Items.Clear();
            foreach (var item in versions.Where(v => string.IsNullOrWhiteSpace(wanted) || v.Contains(wanted, StringComparison.OrdinalIgnoreCase)))
            {
                var row = new ListViewItem(item);
                row.SubItems.Add(item == settings.LastVersion ? "Последняя выбранная" : "Готово к установке");
                row.ForeColor = Color.White;
                list.Items.Add(row);
            }
            list.EndUpdate();
            selected.Text = "Показано: " + list.Items.Count;
        }

        list.DoubleClick += async (_,_) =>
        {
            if (list.SelectedItems.Count == 0) return;
            version.SelectedItem = list.SelectedItems[0].Text;
            settings.LastVersion = list.SelectedItems[0].Text;
            SettingsService.Save(settings);
            selected.Text = "Выбрано: " + settings.LastVersion;
            ShowNavigationPage(0);
            await Task.CompletedTask;
        };

        list.SelectedIndexChanged += (_,_) =>
        {
            if (list.SelectedItems.Count == 0) return;
            selected.Text = "Выбрано: " + list.SelectedItems[0].Text;
        };

        search.TextChanged += (_,_) => Render(search.Text);
        refresh.Tag = Render;

        p.Controls.AddRange(new Control[] { search, refresh, selected, list });
        version.Tag = list;
        list.Tag = new List<string> { settings.LastVersion };
        Render("");
        return p;
    }

    TabPage ModsPage()
    {
        var p = new TabPage("Моды") { BackColor = Color.FromArgb(12, 14, 23), Padding = new Padding(20) };

        var top = new Panel { Dock = DockStyle.Top, Height = 126, BackColor = Color.FromArgb(12,14,23) };
        top.Controls.Add(new Label
        {
            Text = "Моды",
            AutoSize = true,
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 25, FontStyle.Bold),
            Location = new Point(4, 2)
        });
        top.Controls.Add(new Label
        {
            Text = "Тысячи проектов из Modrinth — с настоящими иконками, авторами, рейтингом и загрузкой",
            AutoSize = true,
            ForeColor = Color.FromArgb(153, 163, 184),
            Location = new Point(7, 43),
            Font = new Font("Segoe UI", 9.5f)
        });

        var search = new TextBox
        {
            PlaceholderText = "⌕  Найти мод…",
            Location = new Point(4, 78),
            Width = 420,
            Height = 38,
            BackColor = Color.FromArgb(27, 30, 47),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };
        var searchButton = ModernButton("Найти", 92, 38);
        searchButton.Location = new Point(432, 78);

        modLoader.DropDownStyle = ComboBoxStyle.DropDownList;
        modLoader.Items.Clear();
        modLoader.Items.AddRange(new object[] { "Fabric", "Forge", "Quilt" });
        modLoader.SelectedItem = settings.ModLoader switch { "forge" => "Forge", "quilt" => "Quilt", _ => "Fabric" };
        modLoader.SetBounds(534, 78, 130, 38);

        var sort = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Color.FromArgb(27,30,47),
            ForeColor = Color.White,
            Location = new Point(676,78),
            Width = 170,
            Height = 38
        };
        sort.Items.AddRange(new object[] { "Популярные", "Новые", "Обновлённые" });
        sort.SelectedIndex = 0;

        top.Controls.AddRange(new Control[] { search, searchButton, modLoader, sort });

        modCards.Dock = DockStyle.Fill;
        modCards.Padding = new Padding(4, 8, 10, 40);
        modCards.BackColor = Color.FromArgb(12,14,23);
        modCards.WrapContents = true;

        async Task Reload()
        {
            modFeedQuery = search.Text.Trim();
            modFeedSort = sort.SelectedIndex switch { 1 => "newest", 2 => "updated", _ => "downloads" };
            modFeedOffset = 0;
            await LoadModFeedAsync(true);
        }

        searchButton.Click += async (_, _) => await Reload();
        search.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await Reload(); } };
        sort.SelectedIndexChanged += async (_, _) => await Reload();
        modLoader.SelectedIndexChanged += (_, _) =>
        {
            settings.ModLoader = modLoader.SelectedItem?.ToString() switch { "Forge" => "forge", "Quilt" => "quilt", _ => "fabric" };
            if (gameLoader.Items.Contains(modLoader.SelectedItem)) gameLoader.SelectedItem = modLoader.SelectedItem;
            SettingsService.Save(settings);
        };

        p.Controls.Add(modCards);
        p.Controls.Add(top);
        p.Enter += async (_, _) =>
        {
            if (modCards.Controls.Count == 0)
            {
                modFeedQuery = "";
                modFeedSort = "downloads";
                modFeedOffset = 0;
                await LoadModFeedAsync(true);
            }
        };
        modCards.Scroll += async (_, e) =>
        {
            if (e.ScrollOrientation != ScrollOrientation.VerticalScroll) return;
            var nearBottom = modCards.VerticalScroll.Value >= Math.Max(0, modCards.VerticalScroll.Maximum - modCards.ClientSize.Height - 180);
            if (nearBottom) await LoadModFeedAsync(false);
        };
        return p;
    }

    TabPage CatalogPage(string title, string projectType)
    {
        var p = new TabPage(title) { BackColor = Color.FromArgb(12,14,23), Padding = new Padding(20) };
        var toolbar = new Panel { Dock = DockStyle.Top, Height = 112, BackColor = Color.FromArgb(12,14,23) };

        var heading = new Label
        {
            Text = title.Replace(" Modrinth", ""),
            AutoSize = true,
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 24, FontStyle.Bold),
            Location = new Point(4, 2)
        };
        var description = new Label
        {
            Text = "Живая лента Modrinth · иконки, авторы, описания и новые страницы подгружаются автоматически",
            AutoSize = true,
            ForeColor = Color.FromArgb(145,155,178),
            Location = new Point(7, 40)
        };
        var search = new TextBox
        {
            PlaceholderText = "⌕  Найти проект…",
            Location = new Point(4, 72),
            Width = 420,
            Height = 36
        };
        var find = ModernButton("Найти", 92, 36);
        find.Location = new Point(432,72);

        var sort = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(534,72),
            Width = 160,
            Height = 36,
            BackColor = Color.FromArgb(27,30,47),
            ForeColor = Color.White
        };
        sort.Items.AddRange(new object[] { "Популярные", "Новые", "Обновлённые" });
        sort.SelectedIndex = 0;

        var worldButton = (Button?)null;
        var worldCaption = (Label?)null;
        if (projectType == "datapack")
        {
            worldButton = new Button { Text = "Папка мира…", Location = new Point(708,72), Width = 125, Height = 36, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(38,43,63), ForeColor = Color.White };
            worldButton.FlatAppearance.BorderSize = 0;
            worldCaption = new Label { Text = "Мир не выбран", AutoSize = true, ForeColor = Color.FromArgb(145,155,178), Location = new Point(842,82) };
            worldButton.Click += (_, _) =>
            {
                using var dialog = new FolderBrowserDialog { Description = "Выбери папку мира Minecraft, в которой находится level.dat", SelectedPath = selectedDatapackWorldDirectory ?? settings.GameDirectory };
                if (dialog.ShowDialog() != DialogResult.OK) return;
                if (!File.Exists(Path.Combine(dialog.SelectedPath, "level.dat")))
                {
                    MessageBox.Show("В выбранной папке нет level.dat.", "Не папка мира", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                selectedDatapackWorldDirectory = dialog.SelectedPath;
                worldCaption!.Text = "Мир: " + Path.GetFileName(dialog.SelectedPath);
            };
        }

        toolbar.Controls.AddRange(new Control[] { heading, description, search, find, sort });
        if (worldButton != null) toolbar.Controls.Add(worldButton);
        if (worldCaption != null) toolbar.Controls.Add(worldCaption);

        var results = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(4, 8, 10, 60),
            BackColor = Color.FromArgb(12,14,23)
        };

        var offset = 0;
        var loading = false;
        var query = "";

        async Task LoadPage(bool reset)
        {
            if (loading) return;
            loading = true;
            try
            {
                if (reset)
                {
                    offset = 0;
                    results.Controls.Clear();
                }

                status.Text = offset == 0 ? "Загружаю каталог Modrinth…" : "Загружаю следующую страницу…";
                var facets = Uri.EscapeDataString(JsonSerializer.Serialize(new[] { new[] { "project_type:" + projectType } }));
                var index = sort.SelectedIndex switch { 1 => "newest", 2 => "updated", _ => "downloads" };
                var q = string.IsNullOrWhiteSpace(query) ? "" : "&query=" + Uri.EscapeDataString(query);
                var url = "https://api.modrinth.com/v2/search?limit=24&offset=" + offset + "&index=" + index + q + "&facets=" + facets;
                var root = JsonNode.Parse(await http.GetStringAsync(url));
                var hits = (root?["hits"]?.AsArray() ?? new JsonArray()).ToList();

                using var gate = new SemaphoreSlim(4);
                var iconTasks = new List<Task>();
                foreach (var hit in hits)
                {
                    var card = CreateProjectCard(hit!, out var picture, projectType);
                    results.Controls.Add(card);
                    iconTasks.Add(LoadProjectIconAsync(picture, hit?["icon_url"]?.ToString(), gate));
                }

                offset += hits.Count;
                await Task.WhenAll(iconTasks);
                status.Text = hits.Count == 0 ? "Больше проектов не найдено" : "Загружено проектов: " + offset;
            }
            catch (Exception ex)
            {
                Fail("Каталог Modrinth недоступен", ex);
            }
            finally { loading = false; }
        }

        async Task Reload()
        {
            query = search.Text.Trim();
            await LoadPage(true);
        }

        find.Click += async (_, _) => await Reload();
        search.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            await Reload();
        };
        sort.SelectedIndexChanged += async (_, _) => await Reload();

        results.Scroll += async (_, e) =>
        {
            if (e.ScrollOrientation != ScrollOrientation.VerticalScroll) return;
            var nearBottom = results.VerticalScroll.Value >= Math.Max(0, results.VerticalScroll.Maximum - results.ClientSize.Height - 220);
            if (nearBottom) await LoadPage(false);
        };
        p.Enter += async (_, _) =>
        {
            if (results.Controls.Count == 0) await LoadPage(true);
        };

        p.Controls.Add(results);
        p.Controls.Add(toolbar);
        return p;
    }

    TabPage BuildsPage()
    {
        var p = new TabPage("Сборки") { BackColor = Color.FromArgb(13,15,25), Padding = new Padding(24), AutoScroll = true };

        var title = new Label { Text = "Мои сборки", AutoSize = true, ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 26, FontStyle.Bold), Location = new Point(24,18) };
        var subtitle = new Label { Text = "Отдельные профили Minecraft, загрузчика и параметров запуска.", AutoSize = true, ForeColor = Color.FromArgb(151,161,183), Location = new Point(27,62) };
        p.Controls.AddRange(new Control[] { title, subtitle });

        var createCard = new Panel
        {
            Location = new Point(24, 102),
            Size = new Size(880, 112),
            BackColor = Color.FromArgb(25,28,45),
            Padding = new Padding(18)
        };
        createCard.Controls.Add(new Label { Text = "Создать профиль", AutoSize = true, ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 12, FontStyle.Bold), Location = new Point(18,12) });
        var name = new TextBox { PlaceholderText = "Например: Survival Fabric", Location = new Point(18,48), Width = 300, Height = 36 };
        var add = ModernButton("+  Сохранить текущий профиль", 240, 38); add.Location = new Point(334,47);
        createCard.Controls.AddRange(new Control[] { name, add });
        p.Controls.Add(createCard);

        var list = new FlowLayoutPanel
        {
            Location = new Point(24, 232),
            Size = new Size(900, 360),
            AutoScroll = true,
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = Color.Transparent,
            Padding = new Padding(0)
        };
        p.Controls.Add(list);

        void RefreshBuildList()
        {
            list.Controls.Clear();
            if (settings.SavedBuilds.Count == 0)
            {
                var empty = new Panel { Width = 860, Height = 150, BackColor = Color.FromArgb(22,25,38), Padding = new Padding(22) };
                empty.Controls.Add(new Label { Text = "Пока нет сохранённых сборок", AutoSize = true, ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 12, FontStyle.Bold), Location = new Point(22,22) });
                empty.Controls.Add(new Label { Text = "Выбери версию и загрузчик на главной, затем сохрани профиль здесь.", AutoSize = true, ForeColor = Color.FromArgb(155,164,184), Location = new Point(22,58) });
                list.Controls.Add(empty);
                return;
            }

            foreach (var saved in settings.SavedBuilds.Select((value,index) => (value,index)))
            {
                var parts = saved.value.Split('|');
                var buildName = parts.Length >= 3 ? parts[0] : saved.value;
                var buildVersion = parts.Length >= 3 ? parts[1] : settings.LastVersion;
                var buildLoader = parts.Length >= 3 ? parts[2] : "Fabric";

                var card = new Panel { Width = 275, Height = 145, Margin = new Padding(0,0,12,12), BackColor = Color.FromArgb(25,28,45), Padding = new Padding(16) };
                card.Controls.Add(new Label { Text = buildName, Location = new Point(16,14), Size = new Size(235,26), ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 11, FontStyle.Bold), AutoEllipsis = true });
                card.Controls.Add(new Label { Text = "Minecraft " + buildVersion, AutoSize = true, Location = new Point(16,47), ForeColor = Color.FromArgb(166,176,198) });
                card.Controls.Add(new Label { Text = "◆  " + buildLoader, AutoSize = true, Location = new Point(16,70), ForeColor = Color.FromArgb(101,214,173) });

                var launch = ModernButton("▶  Запустить", 112, 34); launch.Location = new Point(16,100);
                launch.Click += async (_,_) =>
                {
                    settings.LastVersion = buildVersion;
                    if (gameLoader.Items.Contains(buildLoader)) gameLoader.SelectedItem = buildLoader;
                    version.SelectedItem = buildVersion;
                    SettingsService.Save(settings);
                    ShowNavigationPage(0);
                    await LaunchAsync();
                };
                var remove = new Button { Text = "Удалить", Width = 78, Height = 34, Location = new Point(138,100), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(38,42,57), ForeColor = Color.White, Cursor = Cursors.Hand };
                remove.FlatAppearance.BorderSize = 0;
                remove.Click += (_,_) =>
                {
                    if (saved.index < 0 || saved.index >= settings.SavedBuilds.Count) return;
                    if (MessageBox.Show("Удалить профиль «" + buildName + "»?", "Удаление сборки", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    settings.SavedBuilds.RemoveAt(saved.index);
                    SettingsService.Save(settings);
                    RefreshBuildList();
                };
                var folder = new Button { Text = "Папка", Width = 66, Height = 34, Location = new Point(222,100), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(38,42,57), ForeColor = Color.White, Cursor = Cursors.Hand };
                folder.FlatAppearance.BorderSize = 0;
                folder.Click += (_,_) =>
                {
                    try
                    {
                        Directory.CreateDirectory(settings.GameDirectory);
                        Process.Start(new ProcessStartInfo("explorer.exe", """ + settings.GameDirectory + """) { UseShellExecute = true });
                    }
                    catch (Exception ex) { Fail("Не удалось открыть папку Minecraft", ex); }
                };
                card.Controls.AddRange(new Control[] { launch, remove, folder });
                list.Controls.Add(card);
            }
        }

        add.Click += (_,_) =>
        {
            var buildName = string.IsNullOrWhiteSpace(name.Text) ? "Моя сборка " + (settings.SavedBuilds.Count + 1) : name.Text.Trim();
            var gv = version.SelectedItem?.ToString() ?? settings.LastVersion;
            var loader = gameLoader.SelectedItem?.ToString() ?? "Fabric";
            settings.SavedBuilds.Add(buildName + "|" + gv + "|" + loader);
            SettingsService.Save(settings);
            name.Clear();
            RefreshBuildList();
            status.Text = "Профиль сохранён";
        };

        RefreshBuildList();
        return p;
    }

    TabPage NewsPage()
    {
        var p = new TabPage("Новости") { BackColor = Color.FromArgb(18, 19, 23), Padding = new Padding(26) };
        p.Controls.Add(new Label { Text = "Новости и обновления", Font = new Font("Segoe UI Semibold", 20, FontStyle.Bold), AutoSize = true, ForeColor = Color.White, Location = new Point(24, 24) });
        p.Controls.Add(new Label { Text = "Здесь будут новости NovaCraft и обновления каталога. В этой версии новости не подменяются выдуманными публикациями.", AutoSize = false, Size = new Size(700, 64), ForeColor = Color.Silver, Location = new Point(26, 76) });
        return p;
    }

    TabPage MusicPage()
    {
        var p = new TabPage("Музыка") { BackColor = Color.FromArgb(18, 19, 23), Padding = new Padding(26) };
        p.Controls.Add(new Label { Text = "СВОЯ МУЗЫКА В MINECRAFT", Font = new Font("Segoe UI Semibold", 19, FontStyle.Bold), AutoSize = true, ForeColor = Color.White, Location = new Point(24, 24) });
        p.Controls.Add(new Label
        {
            Text = "Выбери собственный файл OGG. NovaCraft соберёт ресурс-пак, который заменяет фоновую музыку игры. Исходные файлы Minecraft не перезаписываются.",
            AutoSize = false, Size = new Size(760, 58), ForeColor = Color.Silver, Location = new Point(26, 76)
        });
        customMusicPath = new TextBox { Location = new Point(26, 150), Width = 560, Height = 34, ReadOnly = true };
        var browse = new Button { Text = "Выбрать OGG…", Location = new Point(600, 148), Width = 150, Height = 38 };
        var installPack = new Button { Text = "Создать ресурс-пак", Location = new Point(26, 210), Width = 190, Height = 42 };
        var result = new Label { Text = "Поддерживается OGG — это формат звука, который использует Minecraft Java.", Location = new Point(26, 270), Size = new Size(760, 90), ForeColor = Color.Silver };
        browse.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog { Filter = "OGG audio (*.ogg)|*.ogg", Title = "Выбери музыкальный файл" };
            if (dialog.ShowDialog() == DialogResult.OK) customMusicPath.Text = dialog.FileName;
        };
        installPack.Click += (_, _) =>
        {
            try
            {
                var source = customMusicPath.Text;
                if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
                    throw new InvalidOperationException("Сначала выбери существующий файл .ogg.");
                var selectedGameVersion = version.SelectedItem?.ToString() ?? settings.LastVersion;
                if (selectedGameVersion != "1.21.1")
                    throw new InvalidOperationException("В этой версии генератор музыки формирует ресурс-пак для Minecraft 1.21.1 (pack_format 34). Выбери Minecraft 1.21.1, иначе игра может отклонить ресурс-пак.");
                if (!string.Equals(Path.GetExtension(source), ".ogg", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Minecraft Java принимает этот ресурс-пак с аудио в формате OGG.");
                var targetDir = Path.Combine(settings.GameDirectory, "resourcepacks");
                Directory.CreateDirectory(targetDir);
                var target = Path.Combine(targetDir, "NovaCraftCustomMusic.zip");
                var temp = target + ".tmp";
                if (File.Exists(temp)) File.Delete(temp);
                using (var archive = ZipFile.Open(temp, ZipArchiveMode.Create))
                {
                    var meta = archive.CreateEntry("pack.mcmeta");
                    using (var writer = new StreamWriter(meta.Open()))
                        writer.Write("{\"pack\":{\"pack_format\":34,\"description\":\"NovaCraft custom background music\"}}");
                    var sounds = archive.CreateEntry("assets/minecraft/sounds.json");
                    using (var writer = new StreamWriter(sounds.Open()))
                        writer.Write("{\"music.game\":{\"replace\":true,\"sounds\":[{\"name\":\"music/game/novacraft_custom\",\"stream\":true}]}}");
                    var audio = archive.CreateEntry("assets/minecraft/sounds/music/game/novacraft_custom.ogg", CompressionLevel.Optimal);
                    using var input = File.OpenRead(source);
                    using var output = audio.Open();
                    input.CopyTo(output);
                }
                File.Move(temp, target, true);
                result.Text = "Ресурс-пак создан:\n" + target + "\nОткрой Minecraft → Настройки → Ресурс-паки и включи NovaCraftCustomMusic. Он меняет фоновую музыку игры, не звуки шагов/ударов.";
            }
            catch (Exception ex) { result.Text = "Не удалось создать ресурс-пак: " + ex.Message; }
        };
        p.Controls.AddRange(new Control[] { customMusicPath, browse, installPack, result });
        return p;
    }

    TabPage AccountPage()
    {
        var p = new TabPage("Скины") { BackColor = Color.FromArgb(13, 15, 25), Padding = new Padding(24), AutoScroll = true };

        p.Controls.Add(new Label { Text = "ПРОФИЛИ И СКИНЫ", Font = new Font("Segoe UI Semibold", 25, FontStyle.Bold), AutoSize = true, ForeColor = Color.White, Location = new Point(24, 20) });
        p.Controls.Add(new Label { Text = "Офлайн-профили с постоянным UUID и локальными скинами. Никакой Microsoft-авторизации.", AutoSize = true, ForeColor = Color.FromArgb(151, 161, 183), Location = new Point(27, 61) });

        var profileCard = ModernCard(860, 205, "ОФЛАЙН-ПРОФИЛИ", Color.FromArgb(22, 25, 39));
        profileCard.Location = new Point(24, 98);
        var profileList = new ListBox { Location = new Point(18, 48), Size = new Size(330, 130), BackColor = Color.FromArgb(17, 20, 31), ForeColor = Color.White, BorderStyle = BorderStyle.None };
        var profileName = new TextBox { PlaceholderText = "Новый ник", Location = new Point(370, 48), Width = 230, Height = 36 };
        var addProfile = ModernButton("+ Добавить", 120, 38); addProfile.Location = new Point(610, 47);
        var renameProfile = ModernButton("Переименовать", 120, 38); renameProfile.Location = new Point(610, 91);
        var deleteProfile = ModernButton("Удалить", 120, 38); deleteProfile.Location = new Point(610, 135); deleteProfile.BackColor = Color.FromArgb(42, 46, 62);
        var uuidLabel = new Label { Text = "", AutoSize = false, Size = new Size(450, 54), Location = new Point(370, 105), ForeColor = Color.FromArgb(156, 168, 191) };

        void EnsureProfiles()
        {
            settings.OfflineProfiles ??= new List<OfflineProfile>();
            if (settings.OfflineProfiles.Count == 0)
            {
                var name = string.IsNullOrWhiteSpace(settings.Username) ? "NovaPlayer" : settings.Username;
                settings.OfflineProfiles.Add(new OfflineProfile { Username = name, Uuid = OfflineProfileRules.OfflineUuid(name) });
            }
            if (string.IsNullOrWhiteSpace(settings.ActiveProfileId) || !settings.OfflineProfiles.Any(x => x.Id == settings.ActiveProfileId))
                settings.ActiveProfileId = settings.OfflineProfiles[0].Id;
        }

        void RenderProfiles()
        {
            EnsureProfiles();
            profileList.Items.Clear();
            foreach (var profile in settings.OfflineProfiles)
                profileList.Items.Add(profile.Username + "  ·  " + profile.Uuid[..8]);
            var activeIndex = settings.OfflineProfiles.FindIndex(x => x.Id == settings.ActiveProfileId);
            profileList.SelectedIndex = Math.Max(0, activeIndex);
            SettingsService.Save(settings);
        }

        void SelectProfile()
        {
            if (profileList.SelectedIndex < 0 || profileList.SelectedIndex >= settings.OfflineProfiles.Count) return;
            var profile = settings.OfflineProfiles[profileList.SelectedIndex];
            settings.ActiveProfileId = profile.Id;
            settings.Username = profile.Username;
            username.Text = profile.Username;
            uuidLabel.Text = "Постоянный UUID:\n" + profile.Uuid + "\nСмена ника не создаёт новый профиль автоматически.";
            SettingsService.Save(settings);
        }

        profileList.SelectedIndexChanged += (_, _) => SelectProfile();
        addProfile.Click += (_, _) =>
        {
            var name = profileName.Text.Trim();
            var validation = NickValidator.Validate(name);
            if (!validation.ok)
            {
                MessageBox.Show(validation.error, "Некорректный ник", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (settings.OfflineProfiles.Any(x => string.Equals(x.Username, name, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show("Профиль с таким ником уже существует.", "Профиль", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var profile = new OfflineProfile { Username = name, Uuid = OfflineProfileRules.OfflineUuid(name) };
            settings.OfflineProfiles.Add(profile);
            settings.ActiveProfileId = profile.Id;
            settings.Username = profile.Username;
            username.Text = profile.Username;
            profileName.Clear();
            RenderProfiles();
            SelectProfile();
            status.Text = "Профиль создан";
        };
        renameProfile.Click += (_, _) =>
        {
            if (profileList.SelectedIndex < 0) return;
            var profile = settings.OfflineProfiles[profileList.SelectedIndex];
            var dialog = new Form
            {
                Text = "Переименовать профиль",
                ClientSize = new Size(360, 150),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false
            };
            var input = new TextBox { Text = profile.Username, Location = new Point(20, 25), Width = 320 };
            var ok = ModernButton("Сохранить", 120, 38); ok.Location = new Point(20, 75);
            var cancel = ModernButton("Отмена", 120, 38); cancel.Location = new Point(160, 75); cancel.BackColor = Color.FromArgb(42, 46, 62);
            ok.Click += (_, _) =>
            {
                var newName = input.Text.Trim();
                var validation = NickValidator.Validate(newName);
                if (!validation.ok)
                {
                    MessageBox.Show(dialog, validation.error, "Некорректный ник", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (settings.OfflineProfiles.Any(x => x.Id != profile.Id && string.Equals(x.Username, newName, StringComparison.OrdinalIgnoreCase)))
                {
                    MessageBox.Show(dialog, "Профиль с таким ником уже существует.", "Профиль", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                profile.Username = newName;
                profile.Uuid = OfflineProfileRules.OfflineUuid(newName);
                settings.Username = newName;
                settings.ActiveProfileId = profile.Id;
                username.Text = newName;
                SettingsService.Save(settings);
                dialog.DialogResult = DialogResult.OK;
                dialog.Close();
                RenderProfiles();
                SelectProfile();
            };
            cancel.DialogResult = DialogResult.Cancel;
            dialog.Controls.AddRange(new Control[] { input, ok, cancel });
            dialog.AcceptButton = ok;
            dialog.CancelButton = cancel;
            dialog.ShowDialog(this);
        };

        deleteProfile.Click += (_, _) =>
        {
            if (profileList.SelectedIndex < 0 || settings.OfflineProfiles.Count <= 1) return;
            var profile = settings.OfflineProfiles[profileList.SelectedIndex];
            if (MessageBox.Show("Удалить профиль «" + profile.Username + "»? Сохранённый UUID и привязанный к нему скин больше не будут активны.", "Удаление профиля", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            settings.OfflineProfiles.Remove(profile);
            settings.ActiveProfileId = settings.OfflineProfiles[0].Id;
            settings.Username = settings.OfflineProfiles[0].Username;
            RenderProfiles();
            SelectProfile();
        };
        profileCard.Controls.AddRange(new Control[] { profileList, profileName, addProfile, renameProfile, deleteProfile, uuidLabel });
        p.Controls.Add(profileCard);

        var skinCard = ModernCard(860, 305, "ЛОКАЛЬНЫЙ СКИН", Color.FromArgb(22, 25, 39));
        skinCard.Location = new Point(24, 320);
        var preview = new PictureBox { Location = new Point(18, 48), Size = new Size(220, 220), BackColor = Color.FromArgb(15,18,29), SizeMode = PictureBoxSizeMode.Zoom };
        var skinStatus = new Label { Location = new Point(260, 55), Size = new Size(560, 70), ForeColor = Color.FromArgb(175,185,205), Text = "Выбери PNG. NovaCraft сохранит его в формате LocalSkin, который может использовать CustomSkinLoader." };
        var choose = ModernButton("Выбрать PNG…", 160, 40); choose.Location = new Point(260, 140);
        var bridge = ModernButton("Установить Skin Bridge", 210, 40); bridge.Location = new Point(430, 140);
        var model = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(260, 195), Width = 190, Height = 36 };
        model.Items.AddRange(new object[] { "Classic · 4px", "Slim · 3px" }); model.SelectedIndex = 0;
        var openFolder = ModernButton("Открыть папку скинов", 190, 38); openFolder.Location = new Point(260, 240);

        void LoadPreview(string path)
        {
            using var stream = File.OpenRead(path);
            using var image = Image.FromStream(stream, false, true);
            var copy = new Bitmap(image);
            var old = preview.Image; preview.Image = copy; old?.Dispose();
        }

        string GetSkinDestination(OfflineProfile profile)
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NovaCraft", "skins");
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, profile.Id + ".png");
        }

        EnsureProfiles();
        RenderProfiles();

        choose.Click += (_, _) =>
        {
            if (profileList.SelectedIndex < 0) return;
            using var dialog = new OpenFileDialog { Title = "Выбери PNG-скин Minecraft", Filter = "Minecraft skin (*.png)|*.png", CheckFileExists = true };
            if (dialog.ShowDialog() != DialogResult.OK) return;
            try
            {
                var profile = settings.OfflineProfiles[profileList.SelectedIndex];
                var info = new FileInfo(dialog.FileName);
                if (info.Length is < 1 or > 8_000_000) throw new InvalidDataException("PNG должен занимать от 1 байта до 8 МБ.");
                using var stream = File.OpenRead(dialog.FileName);
                using var image = Image.FromStream(stream, false, true);
                if (!((image.Width == 64 && image.Height == 64) || (image.Width == 64 && image.Height == 32)))
                    throw new InvalidDataException("Для Java Edition нужен PNG 64×64 или 64×32.");
                var destination = GetSkinDestination(profile);
                File.Copy(dialog.FileName, destination, true);
                profile.SkinPath = destination;
                profile.Slim = model.SelectedIndex == 1;
                settings.ActiveProfileId = profile.Id;
                settings.Username = profile.Username;
                SettingsService.Save(settings);
                LoadPreview(destination);
                skinStatus.Text = "Скин установлен для «" + profile.Username + "». Путь: " + destination;
                status.Text = "Локальный скин сохранён";
            }
            catch (Exception ex) { Fail("Не удалось установить локальный скин", ex); }
        };

        model.SelectedIndexChanged += (_, _) =>
        {
            if (profileList.SelectedIndex < 0) return;
            settings.OfflineProfiles[profileList.SelectedIndex].Slim = model.SelectedIndex == 1;
            SettingsService.Save(settings);
        };

        bridge.Click += async (_, _) => await InstallCustomSkinLoaderAsync(skinStatus);
        openFolder.Click += (_, _) =>
        {
            try { Directory.CreateDirectory(Path.Combine(settings.GameDirectory, "CustomSkinLoader", "LocalSkin", "skins")); Process.Start(new ProcessStartInfo("explorer.exe", Path.Combine(settings.GameDirectory, "CustomSkinLoader", "LocalSkin", "skins")) { UseShellExecute = true }); }
            catch (Exception ex) { Fail("Не удалось открыть папку скинов", ex); }
        };

        profileList.SelectedIndexChanged += (_, _) =>
        {
            if (profileList.SelectedIndex < 0) return;
            var profile = settings.OfflineProfiles[profileList.SelectedIndex];
            model.SelectedIndex = profile.Slim ? 1 : 0;
            if (!string.IsNullOrWhiteSpace(profile.SkinPath) && File.Exists(profile.SkinPath))
            {
                try { LoadPreview(profile.SkinPath); } catch { }
            }
            else skinStatus.Text = "Для этого профиля локальный скин ещё не выбран.";
        };

        p.Controls.Add(skinCard);
        skinCard.Controls.AddRange(new Control[] { preview, skinStatus, choose, bridge, model, openFolder });

        var info = new Label
        {
            Text = "Важно: сам Minecraft Java не умеет показывать произвольный локальный PNG в обычном offline-профиле. Поэтому NovaCraft использует официальный для этой задачи механизм CustomSkinLoader: он читает LocalSkin/<USERNAME>.png. Другие игроки увидят такой скин только если их клиент тоже поддерживает этот механизм.",
            AutoSize = false, Size = new Size(850, 90), Location = new Point(24, 645),
            ForeColor = Color.FromArgb(145, 155, 178)
        };
        p.Controls.Add(info);
        return p;
    }

    static string OfflineUuid(string username) => OfflineProfileRules.OfflineUuid(username);

    async Task InstallCustomSkinLoaderAsync(Label result)
    {
        try
        {
            var game = version.SelectedItem?.ToString() ?? settings.LastVersion;
            var loader = settings.ModLoader;
            if (loader == "vanilla")
                throw new InvalidOperationException("Для локальных скинов через Skin Bridge нужен Fabric, Forge или Quilt.");
            var versionsUrl = "https://api.modrinth.com/v2/project/customskinloader/version?game_versions=" +
                              Uri.EscapeDataString(JsonSerializer.Serialize(new[] { game })) +
                              "&loaders=" + Uri.EscapeDataString(JsonSerializer.Serialize(new[] { loader })) +
                              "&include_changelog=false";
            var versions = JsonNode.Parse(await http.GetStringAsync(versionsUrl))?.AsArray();
            var file = versions?.FirstOrDefault()?["files"]?.AsArray()?.FirstOrDefault();
            var url = file?["url"]?.ToString();
            var sha = file?["hashes"]?["sha512"]?.ToString();
            var filename = file?["filename"]?.ToString();
            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(sha) || string.IsNullOrWhiteSpace(filename))
                throw new InvalidOperationException("Для выбранной версии Minecraft и загрузчика совместимый CustomSkinLoader не найден.");
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                throw new SecurityException("Некорректная ссылка Skin Bridge.");
            result.Text = "Скачиваю Skin Bridge и проверяю SHA-512…";
            var mods = Path.Combine(settings.GameDirectory, "mods");
            await DownloadService.DownloadVerifiedAsync(
                http, uri, sha, mods, filename, new[] { ".jar" }, 50L * 1024 * 1024,
                null, CancellationToken.None,
                name => MessageBox.Show(
                    "Установить Skin Bridge: " + name + "?",
                    "Подтверждение установки",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes);
            foreach (var profile in settings.OfflineProfiles)
            {
                if (string.IsNullOrWhiteSpace(profile.SkinPath) || !File.Exists(profile.SkinPath)) continue;
                var destination = GetProfileSkinPath(profile);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(profile.SkinPath, destination, true);
            }
            SettingsService.Save(settings);
            result.Text = "Skin Bridge установлен. Локальный скин будет загружаться через CustomSkinLoader.";
            status.Text = "Skin Bridge готов";
        }
        catch (Exception ex) { Fail("Не удалось установить Skin Bridge", ex); }
    }

    string GetProfileSkinPath(OfflineProfile profile) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NovaCraft", "skins", profile.Id + ".png");

    void SyncActiveProfileSkinToGame()
    {
        var profile = settings.OfflineProfiles?.FirstOrDefault(x => x.Id == settings.ActiveProfileId);
        if (profile == null || string.IsNullOrWhiteSpace(profile.SkinPath) || !File.Exists(profile.SkinPath))
            return;

        var targetDir = Path.Combine(settings.GameDirectory, "CustomSkinLoader", "LocalSkin", "skins");
        Directory.CreateDirectory(targetDir);
        var target = Path.Combine(targetDir, profile.Username + ".png");
        File.Copy(profile.SkinPath, target, true);
    }

    static void CreatePresetSkin(string path, string preset)
    {
        var accent = preset switch
        {
            "Cyber Blue" => Color.FromArgb(48, 132, 232),
            "Emerald Scout" => Color.FromArgb(48, 174, 118),
            "Purple Neon" => Color.FromArgb(151, 77, 232),
            _ => Color.FromArgb(72, 88, 128)
        };
        var dark = Color.FromArgb(24, 27, 38);
        var skin = Color.FromArgb(224, 170, 128);
        var skinShadow = Color.FromArgb(186, 128, 94);
        var white = Color.FromArgb(225, 232, 242);
        var shoe = Color.FromArgb(16, 18, 25);

        using var bitmap = new Bitmap(64, 64, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.Transparent);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;

        void Box(int x, int y, int w, int h, int d, Color front, Color side, Color top)
        {
            using var f = new SolidBrush(front);
            using var s = new SolidBrush(side);
            using var t = new SolidBrush(top);
            g.FillRectangle(t, x + d, y, w, d);
            g.FillRectangle(s, x, y + d, d, h);
            g.FillRectangle(f, x + d, y + d, w, h);
            g.FillRectangle(s, x + d + w, y + d, d, h);
            g.FillRectangle(s, x + d + w + d, y + d, w, h);
            g.FillRectangle(t, x + d + w, y, w, d);
        }

        Box(0, 0, 8, 8, 8, skin, skinShadow, Color.FromArgb(244, 194, 151));
        Box(16, 16, 8, 12, 4, accent, dark, Color.FromArgb(90, 105, 132));
        Box(40, 16, 4, 12, 4, accent, dark, Color.FromArgb(90, 105, 132));
        Box(32, 48, 4, 12, 4, accent, dark, Color.FromArgb(90, 105, 132));
        Box(0, 16, 4, 12, 4, shoe, dark, Color.FromArgb(45, 49, 59));
        Box(16, 48, 4, 12, 4, shoe, dark, Color.FromArgb(45, 49, 59));

        using var hair = new SolidBrush(dark);
        using var eye = new SolidBrush(white);
        using var pupil = new SolidBrush(Color.FromArgb(30, 39, 55));
        using var mouth = new SolidBrush(skinShadow);
        g.FillRectangle(hair, 8, 8, 8, 3);
        g.FillRectangle(hair, 8, 8, 2, 5);
        g.FillRectangle(eye, 10, 11, 1, 1);
        g.FillRectangle(eye, 13, 11, 1, 1);
        g.FillRectangle(pupil, 10, 11, 1, 1);
        g.FillRectangle(pupil, 13, 11, 1, 1);
        g.FillRectangle(mouth, 11, 13, 4, 1);

        Box(16, 32, 8, 12, 4, Color.FromArgb(
            Math.Min(255, accent.R + 10),
            Math.Min(255, accent.G + 10),
            Math.Min(255, accent.B + 10)), accent, Color.FromArgb(115, 125, 150));
        using var stripe = new SolidBrush(white);
        g.FillRectangle(stripe, 20, 36, 8, 2);

        // Outer hat layer: a simple dark cap with an accent stripe.
        using var cap = new SolidBrush(dark);
        g.FillRectangle(cap, 40, 8, 8, 3);
        g.FillRectangle(cap, 40, 8, 2, 8);
        using var capAccent = new SolidBrush(accent);
        g.FillRectangle(capAccent, 40, 10, 8, 1);

        using var accentBrush = new SolidBrush(accent);
        g.FillRectangle(accentBrush, 44, 44, 4, 4);
        g.FillRectangle(accentBrush, 52, 60, 4, 2);
        g.FillRectangle(accentBrush, 20, 60, 4, 2);

        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    static void GenOpenSkinStudio()
    {
        try { Process.Start(new ProcessStartInfo("https://skinsrestorer.net/skin-upload") { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show("Не удалось открыть Skin Studio: " + ex.Message); }
    }

    TabPage PerformancePage()
    {
        var p = new TabPage("Производительность") { BackColor = Color.FromArgb(13,15,25), Padding = new Padding(24), AutoScroll = true };
        p.Controls.Add(new Label { Text = "ПРОИЗВОДИТЕЛЬНОСТЬ", AutoSize = true, ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 25, FontStyle.Bold), Location = new Point(24,18) });
        p.Controls.Add(new Label { Text = "Реальные клиентские оптимизации вместо обещаний нереального прироста.", AutoSize = true, ForeColor = Color.FromArgb(151,161,183), Location = new Point(27,62) });

        var active = new Label
        {
            Text = "Текущий режим памяти: " + (settings.FpsBoost ? "3 ГБ + JVM-флаги" : "2 ГБ"),
            AutoSize = true,
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 12, FontStyle.Bold),
            Location = new Point(28,105)
        };

        var balanced = ModernCard(410, 170, "ЛИМИТ ПАМЯТИ · 2 ГБ", Color.FromArgb(23,27,42));
        balanced.Location = new Point(24,145);
        balanced.Controls.Add(new Label
        {
            Text = "Стандартный лимит Java.\nСам по себе не повышает FPS, но обычно безопаснее для слабого ПК.",
            AutoSize = true, ForeColor = Color.FromArgb(190,198,215), Location = new Point(18,50)
        });
        var b1 = ModernButton("Выбрать 2 ГБ", 140, 38);
        b1.Location = new Point(18,122);
        b1.Click += (_,_) =>
        {
            settings.MaximumRamMb = 2048;
            settings.FpsBoost = false;
            SettingsService.Save(settings);
            active.Text = "Текущий режим памяти: 2 ГБ";
            status.Text = "Лимит Java установлен на 2 ГБ";
        };
        balanced.Controls.Add(b1);

        var boostCard = ModernCard(410, 170, "ЛИМИТ ПАМЯТИ · 3 ГБ", Color.FromArgb(27,25,45));
        boostCard.Location = new Point(455,145);
        boostCard.Controls.Add(new Label
        {
            Text = "Только при достаточном запасе ОЗУ.\nЭто не ускоритель FPS; на ПК с нехваткой памяти может вызвать тормоза.",
            AutoSize = true, ForeColor = Color.FromArgb(190,198,215), Location = new Point(18,50)
        });
        var b2 = ModernButton("Выбрать 3 ГБ", 140, 38);
        b2.Location = new Point(18,122);
        b2.Click += (_,_) =>
        {
            settings.MaximumRamMb = 3072;
            settings.FpsBoost = true;
            SettingsService.Save(settings);
            active.Text = "Текущий режим памяти: 3 ГБ + JVM-флаги";
            status.Text = "Лимит Java установлен на 3 ГБ; прирост FPS не гарантирован";
        };
        boostCard.Controls.Add(b2);

        var profile = ModernCard(841, 245, "FABRIC 1.21.11 · НАБОР ОПТИМИЗАЦИИ ДЛЯ СЛАБОГО ПК", Color.FromArgb(21,27,43));
        profile.Location = new Point(24,330);
        profile.Controls.Add(new Label
        {
            Text = "Установка проверенных клиентских модов: Lithium, FerriteCore, Entity Culling и ImmediatelyFast. " +
                   "Они не требуют установки на сервер для обычного подключения, но совместимость с каждым отдельным модпаком не гарантируется. " +
                   "Файлы берутся с Modrinth CDN и проверяются по SHA-512. Старые версии тех же модов будут сохранены в резервную папку.",
            AutoSize = true, MaximumSize = new Size(800, 0), ForeColor = Color.FromArgb(190,198,215), Location = new Point(18,50)
        });
        var installProfile = ModernButton("Установить оптимизации Fabric 1.21.11", 300, 40);
        installProfile.Location = new Point(18,170);
        installProfile.Click += async (_,_) => await InstallRecommendedPerformanceProfileAsync();
        profile.Controls.Add(installProfile);

        var compatibility = ModernCard(841, 160, "ВАЖНО ДЛЯ ТВОЕЙ ВИДЕОКАРТЫ", Color.FromArgb(22,25,39));
        compatibility.Location = new Point(24,590);
        compatibility.Controls.Add(new Label
        {
            Text = "Sodium намеренно не включён: Radeon HD 6900 старше официально поддерживаемого AMD HD 7000/GCN 1, " +
                   "а Sodium требует OpenGL 4.5+. У HD 6900 официально заявлен OpenGL 4.4 и драйверы переведены в legacy. " +
                   "Поэтому установка Sodium вслепую может закончиться отказом запуска. Набор выше улучшает отдельные части клиента, " +
                   "но не ускорит чужой серверный TPS, пинг и не даст прирост в 800 раз.",
            AutoSize = true, MaximumSize = new Size(800, 0), ForeColor = Color.FromArgb(181,190,207), Location = new Point(18,50)
        });

        p.Controls.AddRange(new Control[] { active, balanced, boostCard, profile, compatibility });
        return p;
    }

    async Task InstallRecommendedPerformanceProfileAsync()
    {
        var busy = false;
        string? stagingDirectory = null;
        try
        {
            var selectedGame = version.SelectedItem?.ToString();
            if (!string.Equals(selectedGame, "1.21.11", StringComparison.Ordinal))
                throw new InvalidOperationException("Сначала выбери Minecraft 1.21.11 на главной странице.");
            var selectedLoader = gameLoader.SelectedItem?.ToString();
            if (!string.Equals(selectedLoader, "Fabric", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Этот набор рассчитан только на Fabric. Выбери Fabric в загрузчике.");

            settings.LastVersion = selectedGame;
            settings.ModLoader = "fabric";
            SettingsService.Save(settings);

            var gameFilter = Uri.EscapeDataString(JsonSerializer.Serialize(new[] { selectedGame }));
            var loaderFilter = Uri.EscapeDataString(JsonSerializer.Serialize(new[] { "fabric" }));
            var roots = new (string Slug, string Title)[]
            {
                ("lithium", "Lithium"),
                ("ferrite-core", "FerriteCore"),
                ("entityculling", "Entity Culling"),
                ("immediatelyfast", "ImmediatelyFast")
            };

            var files = new List<(string ProjectId, string Title, JsonNode File, string Sha512)>();
            var projects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var filenames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddVersionFile(JsonNode? modVersion, string title)
            {
                if (modVersion == null)
                    throw new InvalidDataException("Modrinth не вернул совместимую версию для " + title + ".");

                var gameVersions = modVersion["game_versions"]?.AsArray();
                if (gameVersions == null || !gameVersions.Any(x => string.Equals(x?.ToString(), selectedGame, StringComparison.Ordinal)))
                    throw new InvalidDataException(title + " не подтверждает совместимость с Minecraft " + selectedGame + ".");

                var loaders = modVersion["loaders"]?.AsArray();
                if (loaders == null || !loaders.Any(x => string.Equals(x?.ToString(), "fabric", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException(title + " не подтверждает совместимость с Fabric.");

                if (string.Equals(modVersion["environment"]?["client"]?.ToString(), "unsupported", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(title + " не поддерживает клиентскую установку.");

                var projectId = modVersion["project_id"]?.ToString();
                if (string.IsNullOrWhiteSpace(projectId))
                    throw new InvalidDataException("У " + title + " отсутствует ID проекта Modrinth.");
                if (!projects.Add(projectId))
                    return;

                var file = modVersion["files"]?.AsArray()?.FirstOrDefault(x => x?["primary"]?.GetValue<bool>() == true)
                           ?? modVersion["files"]?.AsArray()?.FirstOrDefault()
                           ?? throw new InvalidDataException("У " + title + " отсутствует основной JAR-файл.");

                var filename = file["filename"]?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(filename) ||
                    !string.Equals(filename, Path.GetFileName(filename), StringComparison.Ordinal) ||
                    !filename.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Modrinth вернул некорректное имя JAR для " + title + ".");
                if (!filenames.Add(filename))
                    throw new InvalidDataException("Два проекта Modrinth вернули одинаковое имя файла: " + filename + ".");

                var sha512 = file["hashes"]?["sha512"]?.ToString();
                if (string.IsNullOrWhiteSpace(sha512) || sha512.Length != 128 || !sha512.All(Uri.IsHexDigit))
                    throw new InvalidDataException("У " + title + " отсутствует корректный SHA-512.");

                var download = file["url"]?.ToString();
                if (!Uri.TryCreate(download, UriKind.Absolute, out var downloadUri) ||
                    downloadUri.Scheme != Uri.UriSchemeHttps ||
                    !string.Equals(downloadUri.Host, "cdn.modrinth.com", StringComparison.OrdinalIgnoreCase))
                    throw new SecurityException("Загрузка разрешена только с официального HTTPS CDN Modrinth.");

                files.Add((projectId, title, file, sha512));
            }

            SetBusy(true, "Проверяю совместимые версии оптимизационных модов…");
            busy = true;

            foreach (var root in roots)
            {
                var endpoint = "https://api.modrinth.com/v2/project/" + Uri.EscapeDataString(root.Slug) +
                    "/version?game_versions=" + gameFilter + "&loaders=" + loaderFilter +
                    "&version_type=release&include_changelog=false";
                var versions = JsonNode.Parse(await http.GetStringAsync(endpoint))?.AsArray();
                var selectedVersion = versions?.FirstOrDefault();
                AddVersionFile(selectedVersion, root.Title);

                var dependencies = selectedVersion?["dependencies"]?.AsArray() ?? new JsonArray();
                foreach (var dependency in dependencies)
                {
                    if (!string.Equals(dependency?["dependency_type"]?.ToString(), "required", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var versionId = dependency?["version_id"]?.ToString();
                    var dependencyProjectId = dependency?["project_id"]?.ToString();
                    if (string.IsNullOrWhiteSpace(versionId) && string.IsNullOrWhiteSpace(dependencyProjectId))
                        continue;

                    JsonNode? dependencyVersion = null;
                    if (!string.IsNullOrWhiteSpace(versionId))
                    {
                        var dependencyUrl = "https://api.modrinth.com/v2/version/" + Uri.EscapeDataString(versionId);
                        dependencyVersion = JsonNode.Parse(await http.GetStringAsync(dependencyUrl));
                    }
                    else if (!string.IsNullOrWhiteSpace(dependencyProjectId))
                    {
                        var dependencyUrl = "https://api.modrinth.com/v2/project/" + Uri.EscapeDataString(dependencyProjectId) +
                            "/version?game_versions=" + gameFilter + "&loaders=" + loaderFilter +
                            "&version_type=release&include_changelog=false";
                        dependencyVersion = JsonNode.Parse(await http.GetStringAsync(dependencyUrl))?.AsArray()?.FirstOrDefault();
                    }

                    AddVersionFile(dependencyVersion, dependencyProjectId ?? "Обязательная зависимость");
                }
            }

            if (files.Count == 0)
                throw new InvalidDataException("Modrinth не вернул ни одного файла для установки.");

            var summary = string.Join(Environment.NewLine, files.Select(x =>
                "• " + x.Title + " — " + (x.File["filename"]?.ToString() ?? "JAR")));
            var approval = MessageBox.Show(
                "Будут установлены клиентские оптимизации:" + Environment.NewLine + summary +
                Environment.NewLine + Environment.NewLine +
                "Minecraft: 1.21.11 · Fabric" +
                "\nИсточник: официальный Modrinth CDN" +
                "\nПроверка: SHA-512 и Fabric mod ID" +
                "\nСуществующие версии этих же модов будут перенесены в резервную папку вне mods." +
                "\n\nПродолжить?",
                "NovaCraft — оптимизация Fabric",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);
            if (approval != DialogResult.Yes)
                return;

            var modsDirectory = Path.Combine(settings.GameDirectory, "mods");
            Directory.CreateDirectory(modsDirectory);
            stagingDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NovaCraft", "staging", "performance-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stagingDirectory);

            var staged = new List<(string Path, string Filename, string ModId, string Sha512)>();
            foreach (var entry in files)
            {
                var filename = entry.File["filename"]?.ToString() ?? throw new InvalidDataException("У файла отсутствует имя.");
                var download = entry.File["url"]?.ToString() ?? throw new InvalidDataException("У файла отсутствует URL.");
                var uri = new Uri(download);
                var stagedPath = await DownloadService.DownloadVerifiedAsync(
                    http, uri, entry.Sha512, stagingDirectory, filename, new[] { ".jar" },
                    250L * 1024 * 1024, null, CancellationToken.None, _ => true);

                var modId = ReadFabricModId(stagedPath);
                if (string.IsNullOrWhiteSpace(modId))
                    throw new InvalidDataException("Файл " + filename + " не содержит корректный fabric.mod.json. Установка остановлена.");
                staged.Add((stagedPath, filename, modId, entry.Sha512));
                WriteLog("Проверен JAR: " + filename + " · mod id: " + modId);
            }

            if (staged.Select(x => x.ModId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != staged.Count)
                throw new InvalidDataException("В наборе обнаружены два JAR с одинаковым Fabric mod ID.");

            var existing = Directory.GetFiles(modsDirectory, "*.jar", SearchOption.TopDirectoryOnly)
                .Select(path => (Path: path, ModId: ReadFabricModId(path)))
                .Where(x => !string.IsNullOrWhiteSpace(x.ModId))
                .ToList();

            var backupFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var toInstall = new List<(string Path, string Filename, string ModId, string Sha512)>();
            foreach (var incoming in staged)
            {
                var sameMod = existing.Where(x => string.Equals(x.ModId, incoming.ModId, StringComparison.OrdinalIgnoreCase)).ToList();
                var identicalIndex = sameMod.FindIndex(x =>
                    string.Equals(GetFileSha512(x.Path), incoming.Sha512, StringComparison.OrdinalIgnoreCase));

                if (identicalIndex >= 0)
                {
                    File.Delete(incoming.Path);
                    for (var i = 0; i < sameMod.Count; i++)
                    {
                        if (i != identicalIndex)
                            backupFiles.Add(sameMod[i].Path);
                    }
                }
                else
                {
                    foreach (var old in sameMod)
                        backupFiles.Add(old.Path);
                    toInstall.Add(incoming);
                }
            }

            foreach (var incoming in toInstall)
            {
                var destination = Path.Combine(modsDirectory, incoming.Filename);
                if (File.Exists(destination) && !backupFiles.Contains(destination))
                    throw new IOException("Нельзя безопасно заменить уже существующий файл " + incoming.Filename + ".");
            }

            var backupDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NovaCraft", "mod-backups", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6]);
            var movedBackups = new List<(string Original, string Backup)>();
            var movedNew = new List<string>();

            try
            {
                if (backupFiles.Count > 0)
                {
                    Directory.CreateDirectory(backupDirectory);
                    foreach (var oldPath in backupFiles)
                    {
                        var backupPath = Path.Combine(backupDirectory, Path.GetFileName(oldPath));
                        File.Move(oldPath, backupPath, false);
                        movedBackups.Add((oldPath, backupPath));
                    }
                }

                foreach (var incoming in toInstall)
                {
                    var destination = Path.Combine(modsDirectory, incoming.Filename);
                    File.Move(incoming.Path, destination, false);
                    movedNew.Add(destination);
                }
            }
            catch
            {
                foreach (var newPath in movedNew.AsEnumerable().Reverse())
                {
                    try { if (File.Exists(newPath)) File.Delete(newPath); } catch { }
                }
                foreach (var backup in movedBackups.AsEnumerable().Reverse())
                {
                    try
                    {
                        if (File.Exists(backup.Backup) && !File.Exists(backup.Original))
                            File.Move(backup.Backup, backup.Original, false);
                    }
                    catch { }
                }
                throw;
            }

            SettingsService.Save(settings);
            WriteLog("Профиль оптимизации установлен. Новых JAR: " + toInstall.Count +
                "; одинаковых версий уже было: " + (staged.Count - toInstall.Count) +
                "; резервных копий: " + movedBackups.Count);
            status.Text = "Оптимизации Fabric 1.21.11 установлены. Резервные копии: " + movedBackups.Count;
            MessageBox.Show(
                "Установка завершена.\nНовых модов: " + toInstall.Count +
                "\nУже актуальных: " + (staged.Count - toInstall.Count) +
                "\nРезервных копий: " + movedBackups.Count +
                (movedBackups.Count > 0 ? "\nПапка резервных копий: " + backupDirectory : ""),
                "NovaCraft — готово",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Fail("Не удалось установить профиль оптимизации", ex);
        }
        finally
        {
            if (stagingDirectory != null && Directory.Exists(stagingDirectory))
            {
                try { Directory.Delete(stagingDirectory, true); }
                catch (Exception ex) { WriteLog("Не удалось удалить временную папку: " + ex.Message); }
            }
            if (busy)
                SetBusy(false);
        }
    }

    static string? ReadFabricModId(string jarPath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(jarPath);
            var metadata = archive.GetEntry("fabric.mod.json");
            if (metadata == null) return null;
            using var stream = metadata.Open();
            using var document = JsonDocument.Parse(stream);
            return document.RootElement.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
                ? id.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }

    static string GetFileSha512(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA512.HashData(stream));
    }

    TabPage ServersPage()
    {
        var p = new TabPage("Сервера") { BackColor = Color.FromArgb(13,15,25), Padding = new Padding(24), AutoScroll = true };
        p.Controls.Add(new Label { Text = "СЕРВЕРА", AutoSize = true, ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 25, FontStyle.Bold), Location = new Point(24,18) });
        p.Controls.Add(new Label { Text = "Избранное, проверка доступности и быстрый запуск.", AutoSize = true, ForeColor = Color.FromArgb(151,161,183), Location = new Point(27,62) });

        var address = new TextBox { PlaceholderText = "play.example.net:25565", Location = new Point(27,105), Width = 390, Height = 38 };
        var add = ModernButton("+ Добавить", 120, 38); add.Location = new Point(427,105);
        var list = new ListBox { Location = new Point(27,160), Size = new Size(600,280), BackColor = Color.FromArgb(22,25,39), ForeColor = Color.White, BorderStyle = BorderStyle.None };
        foreach (var server in settings.FavoriteServers) list.Items.Add(server);

        var statusLabel = new Label { Text = "Выбери сервер и нажми «Проверить».", AutoSize = false, Size = new Size(600, 55), Location = new Point(27,455), ForeColor = Color.FromArgb(165,177,199) };
        var ping = ModernButton("Проверить сервер", 170, 38); ping.Location = new Point(27,520);
        var launch = ModernButton("▶  Запустить Minecraft", 205, 38); launch.Location = new Point(207,520);
        var remove = ModernButton("Удалить выбранный", 170, 38); remove.Location = new Point(422,520); remove.BackColor = Color.FromArgb(42,46,62);

        void SaveServers()
        {
            settings.FavoriteServers = list.Items.Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            SettingsService.Save(settings);
        }

        static (string Host, int Port) ParseAddress(string value)
        {
            value = value.Trim();
            var host = value;
            var port = 25565;
            var colon = value.LastIndexOf(':');
            if (colon > 0 && int.TryParse(value[(colon + 1)..], out var parsed) && parsed is > 0 and <= 65535)
            {
                host = value[..colon];
                port = parsed;
            }
            if (host.Length == 0 || host.Length > 253 || host.Contains(' ') || host.Contains('/') || host.Contains('\\'))
                throw new InvalidDataException("Некорректный адрес сервера.");
            return (host, port);
        }

        add.Click += (_,_) =>
        {
            try
            {
                var value = address.Text.Trim();
                _ = ParseAddress(value);
                if (!list.Items.Cast<string>().Contains(value, StringComparer.OrdinalIgnoreCase)) list.Items.Add(value);
                address.Clear(); SaveServers();
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Адрес сервера", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        };

        ping.Click += async (_,_) =>
        {
            if (list.SelectedItem is not string value) return;
            try
            {
                var (host, port) = ParseAddress(value);
                statusLabel.Text = "Проверяю " + host + ":" + port + "…";
                var result = await QueryMinecraftServerAsync(host, port);
                statusLabel.Text = result;
            }
            catch (Exception ex) { statusLabel.Text = "Сервер недоступен: " + ex.Message; }
        };

        launch.Click += async (_,_) =>
        {
            if (list.SelectedItem is not string value) return;
            try
            {
                var (host, port) = ParseAddress(value);
                var selectedVersion = version.SelectedItem?.ToString() ?? settings.LastVersion;
                if (string.IsNullOrWhiteSpace(selectedVersion)) throw new InvalidOperationException("Сначала выбери версию Minecraft.");
                SetBusy(true, "Подготавливаю подключение к " + host + ":" + port + "…");
                await InstallSelectedLoaderAsync(selectedVersion);
                var option = CreateLaunchOptions();
                option.ServerIp = host;
                option.ServerPort = port;
                var process = await GetLauncher().BuildProcessAsync(selectedVersion, option);
                process.Start();
                status.Text = "Minecraft запущен";
            }
            catch (Exception ex) { Fail("Не удалось запустить сервер", ex); }
            finally { SetBusy(false); }
        };

        remove.Click += (_,_) => { if (list.SelectedIndex >= 0) { list.Items.RemoveAt(list.SelectedIndex); SaveServers(); statusLabel.Text = "Сервер удалён из избранного."; } };
        p.Controls.AddRange(new Control[] { address, add, list, statusLabel, ping, launch, remove });
        return p;
    }

    async Task<string> QueryMinecraftServerAsync(string host, int port)
    {
        using var tcp = new System.Net.Sockets.TcpClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await tcp.ConnectAsync(host, port, cts.Token);
        using var stream = tcp.GetStream();

        static void WriteVarInt(Stream stream, int value)
        {
            uint v = unchecked((uint)value);
            do
            {
                var b = (byte)(v & 0x7F);
                v >>= 7;
                if (v != 0) b |= 0x80;
                stream.WriteByte(b);
            } while (v != 0);
        }

        static async Task<int> ReadVarIntAsync(Stream stream, CancellationToken token)
        {
            var result = 0; var shift = 0;
            while (shift < 35)
            {
                var b = new byte[1];
                var n = await stream.ReadAsync(b.AsMemory(0,1), token);
                if (n == 0) throw new EndOfStreamException("Сервер закрыл соединение.");
                result |= (b[0] & 0x7F) << shift;
                if ((b[0] & 0x80) == 0) return result;
                shift += 7;
            }
            throw new InvalidDataException("Некорректный VarInt.");
        }

        var hostBytes = Encoding.UTF8.GetBytes(host);
        using var payload = new MemoryStream();
        WriteVarInt(payload, 0);
        WriteVarInt(payload, 765);
        WriteVarInt(payload, hostBytes.Length); payload.Write(hostBytes);
        payload.WriteByte((byte)(port >> 8)); payload.WriteByte((byte)port);
        WriteVarInt(payload, 1);
        WriteVarInt(stream, (int)payload.Length + 1); stream.WriteByte(0); payload.Position = 0; payload.CopyTo(stream);
        WriteVarInt(stream, 1); stream.WriteByte(0);

        await stream.FlushAsync(cts.Token);
        _ = await ReadVarIntAsync(stream, cts.Token);
        _ = await ReadVarIntAsync(stream, cts.Token);
        var length = await ReadVarIntAsync(stream, cts.Token);
        if (length <= 0 || length > 2_000_000) throw new InvalidDataException("Слишком большой ответ сервера.");
        var data = new byte[length];
        var read = 0;
        while (read < length)
        {
            var n = await stream.ReadAsync(data.AsMemory(read, length-read), cts.Token);
            if (n == 0) throw new EndOfStreamException("Неполный ответ сервера.");
            read += n;
        }
        var json = Encoding.UTF8.GetString(data);
        var node = JsonNode.Parse(json);
        var description = node?["description"]?.ToString()?.Replace("\n"," ") ?? "Minecraft server";
        var online = node?["players"]?["online"]?.ToString() ?? "?";
        var max = node?["players"]?["max"]?.ToString() ?? "?";
        return "Онлайн: " + online + "/" + max + " · " + description;
    }

    TabPage SettingsPage()
    {
        var p = new TabPage("Настройки") { BackColor = Color.FromArgb(13,15,25), Padding = new Padding(24), AutoScroll = true };
        p.Controls.Add(new Label { Text="Настройки",AutoSize=true,ForeColor=Color.White,Font=new Font("Segoe UI Semibold",24,FontStyle.Bold),Location=new Point(24,20)});
        p.Controls.Add(new Label { Text="Производительность, игра и внешний вид NovaCraft.",AutoSize=true,ForeColor=Color.FromArgb(160,170,195),Location=new Point(27,62)});

        var game=ModernCard(720,190,"ИГРА",Color.FromArgb(25,28,45)); game.Location=new Point(24,105);
        game.Controls.Add(new Label{Text="Папка Minecraft",AutoSize=true,ForeColor=Color.Silver,Location=new Point(18,44)});
        gameDir.Text=settings.GameDirectory; gameDir.SetBounds(18,68,520,34);
        var browse=new Button{Text="Выбрать…",Width=105,Height=34,Location=new Point(548,68),FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(38,43,63),ForeColor=Color.White};browse.FlatAppearance.BorderSize=0;
        browse.Click+=(_,_)=>{using var d=new FolderBrowserDialog{Description="Выберите папку Minecraft",SelectedPath=gameDir.Text};if(d.ShowDialog()==DialogResult.OK)gameDir.Text=d.SelectedPath;};
        game.Controls.AddRange(new Control[]{gameDir,browse});
        game.Controls.Add(new Label{Text="Оперативная память",AutoSize=true,ForeColor=Color.Silver,Location=new Point(18,116)});
        ram.Minimum=1024;ram.Maximum=8192;ram.Increment=256;ram.Value=Math.Clamp(settings.MaximumRamMb,1024,8192);ram.SetBounds(150,112,170,34);game.Controls.Add(ram);
        game.Controls.Add(new Label{Text="МБ",AutoSize=true,ForeColor=Color.Silver,Location=new Point(325,120)});
        game.Controls.Add(new Label{Text="Разрешение",AutoSize=true,ForeColor=Color.Silver,Location=new Point(390,116)});
        widthInput=new NumericUpDown{Minimum=800,Maximum=7680,Value=Math.Clamp(settings.ScreenWidth,800,7680),Width=85,Location=new Point(470,112)};
        heightInput=new NumericUpDown{Minimum=600,Maximum=4320,Value=Math.Clamp(settings.ScreenHeight,600,4320),Width=85,Location=new Point(565,112)};
        game.Controls.AddRange(new Control[]{widthInput,heightInput});

        var launch=ModernCard(720,225,"ЗАПУСК",Color.FromArgb(25,28,45));launch.Location=new Point(24,310);
        fullScreenInput=new CheckBox{Text="Полный экран",Checked=settings.FullScreen,AutoSize=true,Location=new Point(18,48),ForeColor=Color.White};
        closeAfterLaunchInput=new CheckBox{Text="Закрывать NovaCraft после запуска Minecraft",Checked=settings.CloseAfterLaunch,AutoSize=true,Location=new Point(18,82),ForeColor=Color.White};
        autoRepairInput=new CheckBox{Text="Автоисправление типовых ошибок",Checked=settings.AutoRepair,AutoSize=true,Location=new Point(18,116),ForeColor=Color.White};
        launch.Controls.AddRange(new Control[]{fullScreenInput,closeAfterLaunchInput,autoRepairInput});
        launch.Controls.Add(new Label{Text="Дополнительные аргументы Java",AutoSize=true,ForeColor=Color.Silver,Location=new Point(18,151)});
        jvmArgsInput=new TextBox{Text=settings.ExtraJvmArguments,Width=650,Location=new Point(18,174),BackColor=Color.FromArgb(34,37,58),ForeColor=Color.White};
        launch.Controls.Add(jvmArgsInput);

        var save=ModernButton("✓  Сохранить настройки",210,42);save.Location=new Point(24,560);save.Click+=(_,_)=>{try{SaveSettings();status.Text="Настройки сохранены";}catch(Exception ex){Fail("Не удалось сохранить настройки",ex);}};p.Controls.AddRange(new Control[]{game,launch,save});
        return p;
    }

    TabPage DiagnosticsPage()
    {
        var p = new TabPage("Исправление") { BackColor = Color.FromArgb(18, 19, 23), Padding = new Padding(24) };
        p.Controls.Add(new Label { Text = "ПРОВЕРКА И АВТОИСПРАВЛЕНИЕ", Font = new Font("Segoe UI Semibold", 19, FontStyle.Bold), AutoSize = true, ForeColor = Color.White, Location = new Point(24, 22) });
        p.Controls.Add(new Label { Text = "Проверяются папка игры, доступ на запись, память, Java и файлы выбранной версии. Ошибки не замалчиваются: подробности попадают в журнал NovaCraft.", AutoSize = false, Size = new Size(820, 70), ForeColor = Color.Silver, Location = new Point(26, 70) });
        var result = new Label { Text = "Проверка ещё не запускалась.", AutoSize = false, Size = new Size(760, 90), ForeColor = Color.FromArgb(190, 205, 220), Location = new Point(26, 155), Padding = new Padding(10), BackColor = Color.FromArgb(27, 31, 39) };
        var repair = ModernButton("Проверить и исправить", 220, 44);
        repair.Location = new Point(26, 265);
        repair.Click += async (_, _) => await RunAutoRepairAsync(result);

        var java = ModernButton("Проверить Java", 160, 44);
        java.Location = new Point(260, 265);
        java.Click += async (_, _) => result.Text = await CheckJavaAsync();

        var copy = ModernButton("Скопировать отчёт", 190, 44);
        copy.Location = new Point(438, 265);
        copy.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(log.Text)) Clipboard.SetText(log.Text);
            status.Text = "Технический отчёт скопирован";
        };
        p.Controls.AddRange(new Control[] { result, repair, java, copy });
        return p;
    }

    async Task<string> CheckJavaAsync()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "java",
                Arguments = "-version",
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi) ?? throw new InvalidOperationException("Java не найдена.");
            var stderr = await process.StandardError.ReadToEndAsync();
            var stdout = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            var text = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            if (process.ExitCode != 0) throw new InvalidOperationException(text.Trim());
            var selected = version.SelectedItem?.ToString() ?? settings.LastVersion;
            var required = selected.StartsWith("1.20.5", StringComparison.OrdinalIgnoreCase) ||
                           selected.StartsWith("1.20.6", StringComparison.OrdinalIgnoreCase) ||
                           selected.StartsWith("1.21", StringComparison.OrdinalIgnoreCase)
                ? "Java 21"
                : selected.StartsWith("1.18", StringComparison.OrdinalIgnoreCase) ||
                  selected.StartsWith("1.19", StringComparison.OrdinalIgnoreCase) ||
                  selected.StartsWith("1.20.", StringComparison.OrdinalIgnoreCase)
                    ? "Java 17"
                    : "Java 8+";
            WriteLog("Java: " + text.Trim());
            return "Java найдена. Для " + selected + " рекомендуется " + required + ".\n\n" + text.Trim();
        }
        catch (Exception ex)
        {
            WriteLog("Проверка Java: " + ex);
            return "Java не удалось проверить автоматически.\n\n" + ex.Message;
        }
    }

    Control Labeled(string text, Control control, int x, int y, int width)
    {
        var panel=new Panel{Bounds=new Rectangle(x,y,width,62)};
        panel.Controls.Add(new Label{Text=text,AutoSize=true,Location=new Point(0,0),ForeColor=Color.Silver});
        control.SetBounds(0,25,width,34); panel.Controls.Add(control); return panel;
    }

    async Task RefreshVersionsAsync()
    {
        try {
            status.Text="Получение списка версий…";
            var all=await GetLauncher().GetAllVersionsAsync();
            var releases=all?.Where(v=>string.Equals(v?.Type?.ToString(),"release",StringComparison.OrdinalIgnoreCase))
                .Select(v=>v!.Name).Distinct().Take(100).ToList() ?? new List<string>();
            version.Items.Clear(); foreach(var v in releases) version.Items.Add(v);
            version.SelectedItem = releases.Contains(settings.LastVersion) ? settings.LastVersion : releases.FirstOrDefault();
            if(version.Tag is ListView list)
            {
                list.Tag = releases;
                list.Items.Clear();
                foreach (var item in releases)
                {
                    var row = new ListViewItem(item);
                    row.ForeColor = Color.White;
                    list.Items.Add(row);
                }
            }
            WriteLog("Версий получено: "+releases.Count); status.Text="Готов";
        } catch(Exception ex){Fail("Не удалось получить список версий",ex);}
    }

    async Task<string> InstallSelectedLoaderAsync(string vanillaVersion)
    {
        var loaderName = settings.ModLoader;
        if (loaderName == "vanilla")
        {
            await GetLauncher().InstallAsync(vanillaVersion);
            return vanillaVersion;
        }

        WriteLog("Установка " + loaderName + " для Minecraft " + vanillaVersion);
        string installedId;
        var path = new MinecraftPath(settings.GameDirectory);
        if (loaderName == "fabric")
        {
            var fabric = new FabricInstaller(http);
            installedId = await fabric.Install(vanillaVersion, path);
        }
        else if (loaderName == "forge")
        {
            var forge = new ForgeInstaller(GetLauncher());
            installedId = await forge.Install(vanillaVersion, new ForgeInstallOptions
            {
                InstallerOutput = new Progress<string>(line => WriteLog(line))
            });
        }
        else if (loaderName == "quilt")
        {
            var quilt = new QuiltInstaller(http);
            installedId = await quilt.Install(vanillaVersion, path);
        }
        else throw new InvalidOperationException("Неизвестный загрузчик.");

        await GetLauncher().InstallAsync(installedId);
        WriteLog("Загрузчик установлен: " + installedId);
        return installedId;
    }

    async Task InstallAsync()
    {
        if(version.SelectedItem is not string v)return;
        try
        {
            SetBusy(true,"Установка " + settings.ModLoader + " для " + v + "…");
            var installedId = await InstallSelectedLoaderAsync(v);
            settings.LastVersion = v;
            SettingsService.Save(settings);
            WriteLog("Готово: " + installedId);
            status.Text = "Установка завершена: " + installedId;
        }
        catch(Exception ex){Fail("Ошибка установки загрузчика",ex);}
        finally{SetBusy(false);}
    }

    MSession CreateOfflineSessionForActiveProfile()
    {
        var name = username.Text.Trim();
        var profile = settings.OfflineProfiles?.FirstOrDefault(x => x.Id == settings.ActiveProfileId);
        if (profile == null)
        {
            profile = new OfflineProfile { Username = name, Uuid = OfflineProfileRules.OfflineUuid(name) };
            settings.OfflineProfiles ??= new List<OfflineProfile>();
            settings.OfflineProfiles.Add(profile);
            settings.ActiveProfileId = profile.Id;
        }
        profile.Username = name;
        if (!Guid.TryParse(profile.Uuid, out _))
            profile.Uuid = OfflineProfileRules.OfflineUuid(name);
        SettingsService.Save(settings);
        return new MSession
        {
            Username = profile.Username,
            UUID = profile.Uuid,
            AccessToken = "0",
            Xuid = ""
        };
    }

    MLaunchOption CreateLaunchOptions()
    {
        var option = new MLaunchOption
        {
            Session = CreateOfflineSessionForActiveProfile(),
            MaximumRamMb = settings.MaximumRamMb,
            ScreenWidth = settings.ScreenWidth,
            ScreenHeight = settings.ScreenHeight,
            FullScreen = settings.FullScreen
        };
        var extra = new List<MArgument>();
        if (boost.Checked)
        {
            extra.Add(MArgument.FromCommandLine("-XX:+UseG1GC"));
            extra.Add(MArgument.FromCommandLine("-XX:+ParallelRefProcEnabled"));
            extra.Add(MArgument.FromCommandLine("-XX:MaxGCPauseMillis=100"));
            extra.Add(MArgument.FromCommandLine("-XX:+UnlockExperimentalVMOptions"));
            extra.Add(MArgument.FromCommandLine("-XX:+DisableExplicitGC"));
        }
        if (!string.IsNullOrWhiteSpace(settings.ExtraJvmArguments))
            extra.Add(MArgument.FromCommandLine(settings.ExtraJvmArguments));
        if (extra.Count > 0) option.ExtraJvmArguments = extra;
        return option;
    }

    async Task LaunchAsync()
    {
        if (version.SelectedItem is not string v) { MessageBox.Show("Сначала выбери версию Minecraft."); return; }
        var activeProfile = settings.OfflineProfiles?.FirstOrDefault(x => x.Id == settings.ActiveProfileId);
        var nicknameValidation = NickValidator.Validate(activeProfile?.Username);
        if (!nicknameValidation.ok)
        {
            MessageBox.Show(nicknameValidation.error, "Некорректный ник", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        string launchVersion = v;
        try
        {
            SaveSettings();
            SetBusy(true, "Подготовка игры…");
            launchVersion = await InstallSelectedLoaderAsync(v);
            var process = await GetLauncher().BuildProcessAsync(launchVersion, CreateLaunchOptions());
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => BeginInvoke(new Action(() =>
            {
                status.Text = process.ExitCode == 0
                    ? "Minecraft завершён"
                    : "Minecraft завершился с кодом " + process.ExitCode;
                WriteLog("Minecraft завершился. ExitCode=" + process.ExitCode);
            }));
            process.Start();
            WriteLog("Minecraft запущен: " + launchVersion);
            status.Text = "Игра запущена";
            if (settings.CloseAfterLaunch) BeginInvoke(new Action(Close));
        }
        catch (Exception ex)
        {
            WriteLog("Ошибка запуска: " + ex);
            if (settings.AutoRepair && IsKnownLaunchError(ex))
            {
                try
                {
                    SetBusy(true, "Обнаружена типовая ошибка. Восстанавливаю файлы…");
                    var errorText = ex.ToString();
                    if (errorText.Contains("Invalid maximum heap size", StringComparison.OrdinalIgnoreCase) ||
                        errorText.Contains("Could not reserve enough space for object heap", StringComparison.OrdinalIgnoreCase) ||
                        errorText.Contains("Could not create Java Virtual Machine", StringComparison.OrdinalIgnoreCase))
                    {
                        var oldRam = settings.MaximumRamMb;
                        settings.MaximumRamMb = Math.Max(1024, oldRam - 512);
                        ram.Value = Math.Clamp(settings.MaximumRamMb, (int)ram.Minimum, (int)ram.Maximum);
                        MessageBox.Show($"JVM не смогла выделить память. Лимит уменьшен с {oldRam} до {settings.MaximumRamMb} МБ.", "Автоисправление ОЗУ", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    SettingsService.Save(settings);
                    await GetLauncher().InstallAsync(launchVersion);
                    var repaired = await GetLauncher().BuildProcessAsync(launchVersion, CreateLaunchOptions());
                    repaired.Start();
                    status.Text = "Файлы восстановлены, игра запущена";
                    WriteLog("Автоисправление завершено: " + launchVersion);
                    if (settings.CloseAfterLaunch) BeginInvoke(new Action(Close));
                    return;
                }
                catch (Exception repairError)
                {
                    WriteLog("Автоисправление не помогло: " + repairError);
                    Fail("Автоисправление не смогло устранить проблему", repairError);
                }
            }
            else Fail("Minecraft не удалось запустить", ex);
        }
        finally { SetBusy(false); }
    }

    Panel CreateProjectCard(JsonNode hit, out PictureBox icon, string projectType = "mod")
    {
        var projectId = hit?["project_id"]?.ToString() ?? "";
        var slug = hit?["slug"]?.ToString() ?? projectId;
        var title = hit?["title"]?.ToString() ?? "Без названия";
        var description = hit?["description"]?.ToString() ?? "Описание отсутствует";
        var author = hit?["author"]?.ToString() ?? "Автор не указан";
        var downloads = long.TryParse(hit?["downloads"]?.ToString(), out var count) ? count.ToString("N0") : "—";
        var follows = long.TryParse(hit?["follows"]?.ToString(), out var followCount) ? followCount.ToString("N0") : "—";

        var card = new Panel
        {
            Width = 410,
            Height = 214,
            Margin = new Padding(7),
            BackColor = Color.FromArgb(24, 27, 42),
            Padding = new Padding(14)
        };

        icon = new PictureBox
        {
            Location = new Point(14, 14),
            Size = new Size(70, 70),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.FromArgb(35, 39, 58)
        };

        var source = new Label
        {
            Text = "MODRINTH",
            AutoSize = true,
            Location = new Point(96, 15),
            ForeColor = Color.FromArgb(78, 220, 151),
            Font = new Font("Segoe UI Semibold", 8, FontStyle.Bold)
        };
        var name = new Label
        {
            Text = title,
            Location = new Point(96, 34),
            Size = new Size(292, 28),
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 12, FontStyle.Bold),
            AutoEllipsis = true
        };
        var by = new Label
        {
            Text = "Автор: " + author,
            Location = new Point(96, 62),
            Size = new Size(292, 20),
            ForeColor = Color.FromArgb(148, 160, 184),
            AutoEllipsis = true
        };
        var desc = new Label
        {
            Text = description,
            Location = new Point(14, 93),
            Size = new Size(382, 43),
            ForeColor = Color.FromArgb(202, 208, 222),
            AutoEllipsis = true
        };
        var stats = new Label
        {
            Text = "↓ " + downloads + "    ★ " + follows,
            Location = new Point(14, 145),
            Size = new Size(180, 24),
            ForeColor = Color.FromArgb(143, 157, 181),
            Font = new Font("Segoe UI", 8.5f)
        };

        var canInstallHere = projectType is "mod" or "resourcepack" or "shader" or "datapack";
        var action = new Button
        {
            Text = canInstallHere ? "Установить" : "Открыть",
            Location = new Point(270, 143),
            Size = new Size(126, 38),
            Tag = projectId,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(92, 48, 224),
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold)
        };
        action.FlatAppearance.BorderSize = 0;

        if (projectType == "mod")
            action.Click += async (_, _) => { selectedModProject = projectId; selectedModTitle = title; await DownloadSelectedModAsync(); };
        else if (projectType is "resourcepack" or "shader")
            action.Click += async (_, _) => await DownloadCatalogArchiveAsync(projectId, title, projectType);
        else if (projectType == "datapack")
            action.Click += async (_, _) => await DownloadCatalogArchiveAsync(projectId, title, projectType, selectedDatapackWorldDirectory);
        else
            action.Click += (_, _) =>
            {
                try { Process.Start(new ProcessStartInfo("https://modrinth.com/" + projectType + "/" + Uri.EscapeDataString(slug)) { UseShellExecute = true }); }
                catch (Exception ex) { Fail("Не удалось открыть страницу проекта", ex); }
            };

        card.Controls.AddRange(new Control[] { icon, source, name, by, desc, stats, action });
        return card;
    }


    static async Task<byte[]> ReadBoundedContentAsync(HttpContent content, int maxBytes)
    {
        await using var input = await content.ReadAsStreamAsync();
        using var output = new MemoryStream(Math.Min(maxBytes, 1024 * 1024));
        var buffer = new byte[81920];
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length));
            if (read == 0) break;
            if (output.Length + read > maxBytes)
                throw new InvalidDataException($"Загружаемый файл превышает лимит {maxBytes / (1024 * 1024)} МБ.");
            await output.WriteAsync(buffer.AsMemory(0, read));
        }
        return output.ToArray();
    }

    async Task LoadProjectIconAsync(PictureBox picture, string? iconUrl, SemaphoreSlim gate)
    {
        if (string.IsNullOrWhiteSpace(iconUrl) || !Uri.TryCreate(iconUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != "https" || (uri.Host != "cdn.modrinth.com" && uri.Host != "images.modrinth.com"))
            return;
        await gate.WaitAsync();
        try
        {
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long length && (length <= 0 || length > 5 * 1024 * 1024)) return;
            var bytes = await ReadBoundedContentAsync(response.Content, 5 * 1024 * 1024);
            if (bytes.Length == 0 || bytes.Length > 5 * 1024 * 1024) return;
            using var ms = new MemoryStream(bytes);
            using var source = Image.FromStream(ms);
            var bitmap = new Bitmap(source);
            if (picture.IsDisposed) { bitmap.Dispose(); return; }
            picture.Image?.Dispose();
            picture.Image = bitmap;
            picture.Controls.Clear();
        }
        catch { /* Отсутствующая или недоступная иконка не ломает каталог. */ }
        finally { gate.Release(); }
    }

    async Task LoadPopularModsAsync()
    {
        try
        {
            status.Text = "Загрузка популярных модов Modrinth…";
            var facets = Uri.EscapeDataString(JsonSerializer.Serialize(new[] { new[] { "project_type:mod" } }));
            var url = "https://api.modrinth.com/v2/search?facets=" + facets + "&index=downloads&limit=8";
            var root = JsonNode.Parse(await http.GetStringAsync(url));
            var hits = (root?["hits"]?.AsArray() ?? new JsonArray()).ToList();
            popularFeed.SuspendLayout();
            popularFeed.Controls.Clear();
            using var gate = new SemaphoreSlim(3);
            var iconTasks = new List<Task>();
            foreach (var hit in hits)
            {
                var card = CreateProjectCard(hit!, out var picture, "mod");
                popularFeed.Controls.Add(card);
                iconTasks.Add(LoadProjectIconAsync(picture, hit?["icon_url"]?.ToString(), gate));
            }
            popularFeed.ResumeLayout();
            await Task.WhenAll(iconTasks);
            status.Text = hits.Count == 0 ? "Популярные проекты не найдены" : "Популярные моды загружены: " + hits.Count;
        }
        catch (Exception ex)
        {
            status.Text = "Лента Modrinth временно недоступна";
            WriteLog("Лента популярных модов: " + ex.Message);
            popularFeed.Controls.Clear();
            var errorPanel = new Panel { Width = 720, Height = 120, BackColor = Color.FromArgb(27, 30, 41), Padding = new Padding(18) };
            errorPanel.Controls.Add(new Label { Text = "Лента Modrinth сейчас недоступна", AutoSize = true, ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 11, FontStyle.Bold), Location = new Point(18, 16) });
            errorPanel.Controls.Add(new Label { Text = "Каталог не исчез — не удалось получить данные по сети. Нажми «Повторить».", AutoSize = true, ForeColor = Color.Silver, Location = new Point(18, 46) });
            var retry = new Button { Text = "↻  Повторить", Width = 120, Height = 32, Location = new Point(18, 74), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(83, 47, 174), ForeColor = Color.White };
            retry.FlatAppearance.BorderSize = 0;
            retry.Click += async (_, _) => await LoadPopularModsAsync();
            errorPanel.Controls.Add(retry);
            popularFeed.Controls.Add(errorPanel);
        }
    }

    async Task LoadModFeedAsync(bool reset)
    {
        if (modFeedLoading) return;
        modFeedLoading = true;
        try
        {
            status.Text = modFeedOffset == 0 ? "Загружаю моды Modrinth…" : "Загружаю ещё моды…";
            var loaderFacet = settings.ModLoader switch
            {
                "forge" => "categories:forge",
                "quilt" => "categories:quilt",
                _ => "categories:fabric"
            };
            var selectedVersion = version.SelectedItem?.ToString();
            var facetGroups = new List<string[]> { new[] { "project_type:mod" }, new[] { loaderFacet } };
            if (!string.IsNullOrWhiteSpace(selectedVersion))
                facetGroups.Add(new[] { "versions:" + selectedVersion });
            var facets = Uri.EscapeDataString(JsonSerializer.Serialize(facetGroups));
            var query = string.IsNullOrWhiteSpace(modFeedQuery) ? "" : "&query=" + Uri.EscapeDataString(modFeedQuery);
            var url = "https://api.modrinth.com/v2/search?limit=24&offset=" + modFeedOffset +
                      "&index=" + Uri.EscapeDataString(modFeedSort) + query + "&facets=" + facets;
            var root = JsonNode.Parse(await http.GetStringAsync(url));
            var hits = (root?["hits"]?.AsArray() ?? new JsonArray()).ToList();

            if (reset) modCards.Controls.Clear();

            using var gate = new SemaphoreSlim(4);
            var iconTasks = new List<Task>();
            foreach (var hit in hits)
            {
                var card = CreateProjectCard(hit!, out var picture, "mod");
                modCards.Controls.Add(card);
                iconTasks.Add(LoadProjectIconAsync(picture, hit?["icon_url"]?.ToString(), gate));
            }

            modFeedOffset += hits.Count;
            await Task.WhenAll(iconTasks);
            status.Text = hits.Count == 0
                ? "Больше проектов не найдено"
                : $"Показано модов: {modFeedOffset}";
        }
        catch (Exception ex)
        {
            Fail("Не удалось загрузить каталог модов Modrinth", ex);
        }
        finally
        {
            modFeedLoading = false;
        }
    }

    async Task SearchModsAsync(string query) => await SearchProjectsAsync(query, "mod", modCards);

    async Task SearchProjectsAsync(string query, string projectType, FlowLayoutPanel target)
    {
        if (string.IsNullOrWhiteSpace(query)) return;
        try
        {
            status.Text = "Поиск в каталоге Modrinth…";
            var facetGroups = new List<string[]> { new[] { "project_type:" + projectType } };
            if (projectType == "mod")
            {
                var loaderFacet = settings.ModLoader switch
                {
                    "forge" => "categories:forge",
                    "quilt" => "categories:quilt",
                    _ => "categories:fabric"
                };
                facetGroups.Add(new[] { loaderFacet });
                var selectedVersion = version.SelectedItem?.ToString();
                if (!string.IsNullOrWhiteSpace(selectedVersion))
                    facetGroups.Add(new[] { "versions:" + selectedVersion });
            }
            var facets = Uri.EscapeDataString(JsonSerializer.Serialize(facetGroups));
            var url = "https://api.modrinth.com/v2/search?query=" + Uri.EscapeDataString(query) + "&facets=" + facets + "&index=relevance&limit=30";
            var root = JsonNode.Parse(await http.GetStringAsync(url));
            var hits = (root?["hits"]?.AsArray() ?? new JsonArray()).ToList();
            target.SuspendLayout();
            target.Controls.Clear();
            using var gate = new SemaphoreSlim(3);
            var iconTasks = new List<Task>();
            foreach (var hit in hits)
            {
                var card = CreateProjectCard(hit!, out var picture, projectType);
                target.Controls.Add(card);
                iconTasks.Add(LoadProjectIconAsync(picture, hit?["icon_url"]?.ToString(), gate));
            }
            target.ResumeLayout();
            await Task.WhenAll(iconTasks);
            status.Text = "Найдено проектов: " + hits.Count;
        }
        catch (Exception ex) { Fail("Каталог Modrinth недоступен", ex); }
    }

    async Task DownloadCatalogArchiveAsync(string projectId, string title, string projectType, string? worldDirectory = null)
    {
        try
        {
            var game = version.SelectedItem?.ToString()
                ?? throw new InvalidOperationException("Сначала выбери версию Minecraft на главной странице.");
            status.Text = "Получение версии проекта…";
            var gameFilter = Uri.EscapeDataString(JsonSerializer.Serialize(new[] { game }));
            var endpoint = $"https://api.modrinth.com/v2/project/{Uri.EscapeDataString(projectId)}/version?game_versions={gameFilter}";
            var versions = JsonNode.Parse(await http.GetStringAsync(endpoint))?.AsArray();
            var file = versions?.FirstOrDefault()?[ "files" ]?.AsArray()?.FirstOrDefault()
                ?? throw new InvalidOperationException("Для выбранной версии Minecraft не найден совместимый архив.");
            var download = file["url"]?.ToString()
                ?? throw new InvalidOperationException("В метаданных проекта отсутствует ссылка на файл.");
            if (!Uri.TryCreate(download, UriKind.Absolute, out var uri) ||
                uri.Scheme != "https" || uri.Host != "cdn.modrinth.com")
                throw new SecurityException("Разрешена загрузка только с официального CDN cdn.modrinth.com.");
            var filename = Path.GetFileName(file["filename"]?.ToString() ?? "");
            if (string.IsNullOrWhiteSpace(filename) || !filename.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Modrinth не предоставил ZIP-архив нужного типа.");
            var expected = file["hashes"]?["sha512"]?.ToString();
            if (string.IsNullOrWhiteSpace(expected) || expected.Length != 128 || !expected.All(Uri.IsHexDigit))
                throw new InvalidDataException("У архива отсутствует корректный SHA-512. Загрузка отменена.");
            string folderName;
            string targetDir;
            if (projectType == "resourcepack")
            {
                folderName = "resourcepacks";
                targetDir = Path.Combine(settings.GameDirectory, folderName);
            }
            else if (projectType == "shader")
            {
                folderName = "shaderpacks";
                targetDir = Path.Combine(settings.GameDirectory, folderName);
            }
            else if (projectType == "datapack")
            {
                if (string.IsNullOrWhiteSpace(worldDirectory) || !File.Exists(Path.Combine(worldDirectory, "level.dat")))
                    throw new InvalidOperationException("Сначала выбери папку мира Minecraft с файлом level.dat.");
                folderName = "datapacks";
                targetDir = Path.Combine(worldDirectory, folderName);
            }
            else throw new InvalidOperationException("Этот тип проекта нельзя установить напрямую.");
            Directory.CreateDirectory(targetDir);
            var destination = Path.Combine(targetDir, filename);
            if (File.Exists(destination))
                throw new IOException("Такой файл уже установлен: " + filename);
            var approval = MessageBox.Show(
                "Проект: " + title + "\nФайл: " + filename + "\nMinecraft: " + game +
                "\nКаталог: " + folderName + "\nИсточник: Modrinth CDN\nПроверка: SHA-512.\n\nПродолжить загрузку?",
                "Подтверждение установки", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (approval != DialogResult.Yes) return;
            await DownloadService.DownloadVerifiedAsync(
                http, uri, expected, targetDir, filename, new[] { ".zip" },
                250L * 1024 * 1024, null, CancellationToken.None, _ => true);
            WriteLog("Установлен архив Modrinth: " + destination);
            status.Text = "Архив установлен: " + filename;
        }
        catch (Exception ex) { Fail("Не удалось установить проект Modrinth", ex); }
    }

    async Task DownloadSelectedModAsync()
    {
        if (string.IsNullOrWhiteSpace(selectedModProject)) return;
        try
        {
            if (settings.ModLoader == "vanilla")
                throw new InvalidOperationException("Для установки модов выбери Fabric, Quilt или Forge.");
            var game = version.SelectedItem?.ToString() ?? throw new InvalidOperationException("Сначала выбери версию Minecraft.");
            var loaderName = settings.ModLoader;
            var gameFilter = Uri.EscapeDataString(JsonSerializer.Serialize(new[] { game }));
            var loaderFilter = Uri.EscapeDataString(JsonSerializer.Serialize(new[] { loaderName }));
            var url = $"https://api.modrinth.com/v2/project/{Uri.EscapeDataString(selectedModProject)}/version?game_versions={gameFilter}&loaders={loaderFilter}&include_changelog=false";
            var arr = JsonNode.Parse(await http.GetStringAsync(url))?.AsArray();
            var selectedVersion = arr?.FirstOrDefault() ?? throw new InvalidOperationException("Совместимая версия мода не найдена.");
            var projectTitle = selectedModTitle ?? selectedModProject;
            var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { selectedModProject };
            var files = new List<(JsonNode File, string Title)>();
            var mainFile = selectedVersion["files"]?.AsArray()?.FirstOrDefault(f => f?["primary"]?.GetValue<bool>() == true)
                           ?? selectedVersion["files"]?.AsArray()?.FirstOrDefault()
                           ?? throw new InvalidOperationException("У версии мода отсутствует файл.");
            files.Add((mainFile, projectTitle));

            var dependencies = selectedVersion["dependencies"]?.AsArray() ?? new JsonArray();
            foreach (var dependency in dependencies)
            {
                var type = dependency?["dependency_type"]?.ToString();
                if (!string.Equals(type, "required", StringComparison.OrdinalIgnoreCase)) continue;
                var versionId = dependency?["version_id"]?.ToString();
                var projectId = dependency?["project_id"]?.ToString();
                if (string.IsNullOrWhiteSpace(versionId) && string.IsNullOrWhiteSpace(projectId)) continue;

                JsonNode? depVersion = null;
                if (!string.IsNullOrWhiteSpace(versionId))
                    depVersion = JsonNode.Parse(await http.GetStringAsync("https://api.modrinth.com/v2/version/" + Uri.EscapeDataString(versionId)));

                if (depVersion == null && !string.IsNullOrWhiteSpace(projectId))
                {
                    var depUrl = $"https://api.modrinth.com/v2/project/{Uri.EscapeDataString(projectId)}/version?game_versions={gameFilter}&loaders={loaderFilter}&include_changelog=false";
                    depVersion = JsonNode.Parse(await http.GetStringAsync(depUrl))?.AsArray()?.FirstOrDefault();
                }

                if (depVersion == null) throw new InvalidOperationException("Не удалось подобрать обязательную зависимость мода.");
                var depProject = depVersion["project_id"]?.ToString() ?? projectId ?? "";
                if (string.IsNullOrWhiteSpace(depProject) || !installed.Add(depProject)) continue;
                var depFile = depVersion["files"]?.AsArray()?.FirstOrDefault(f => f?["primary"]?.GetValue<bool>() == true)
                              ?? depVersion["files"]?.AsArray()?.FirstOrDefault();
                if (depFile != null)
                    files.Add((depFile, depProject));
            }

            var summary = string.Join(Environment.NewLine, files.Select(x => "• " + x.Title + " — " + (x.File["filename"]?.ToString() ?? "файл")));
            var approval = MessageBox.Show(
                "Будут установлены:" + Environment.NewLine + summary +
                Environment.NewLine + Environment.NewLine +
                "Minecraft: " + game + "\nЗагрузчик: " + loaderName +
                "\nИсточник: Modrinth CDN\nФайлы проверяются по SHA-512." +
                "\n\nПродолжить?",
                "Установка мода и обязательных зависимостей",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (approval != DialogResult.Yes) return;

            var modsDir = Path.Combine(settings.GameDirectory, "mods");
            Directory.CreateDirectory(modsDir);
            foreach (var entry in files)
            {
                var file = entry.File;
                var download = file["url"]?.ToString() ?? throw new InvalidOperationException("Ссылка на файл отсутствует.");
                if (!Uri.TryCreate(download, UriKind.Absolute, out var downloadUri) ||
                    downloadUri.Scheme != "https" || downloadUri.Host != "cdn.modrinth.com")
                    throw new SecurityException("Загрузка разрешена только с официального CDN Modrinth.");

                var name = Path.GetFileName(file["filename"]?.ToString() ?? "");
                var expectedSha512 = file["hashes"]?["sha512"]?.ToString();
                if (string.IsNullOrWhiteSpace(name) || !name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Modrinth вернул некорректное имя JAR.");
                if (string.IsNullOrWhiteSpace(expectedSha512) || expectedSha512.Length != 128 || !expectedSha512.All(Uri.IsHexDigit))
                    throw new InvalidDataException("У файла отсутствует корректный SHA-512.");

                var destination = Path.Combine(modsDir, name);
                if (File.Exists(destination)) continue;

                await DownloadService.DownloadVerifiedAsync(
                    http, downloadUri, expectedSha512, modsDir, name, new[] { ".jar" },
                    250L * 1024 * 1024, null, CancellationToken.None,
                    _ => true);
                WriteLog("Установлен мод/зависимость: " + name);
            }

            status.Text = files.Count == 1 ? "Мод установлен" : $"Мод установлен вместе с {files.Count - 1} обязательной зависимостью.";
        }
        catch (Exception ex) { Fail("Не удалось установить мод и зависимости", ex); }
    }

    void SaveSettings()
    {
        if (string.IsNullOrWhiteSpace(gameDir.Text))
            throw new InvalidOperationException("Выбери папку Minecraft в настройках.");
        var activeProfile = settings.OfflineProfiles?.FirstOrDefault(x => x.Id == settings.ActiveProfileId);
        if (activeProfile == null)
            throw new InvalidOperationException("Активный профиль не найден.");
        var nicknameValidation = NickValidator.Validate(activeProfile.Username);
        if (!nicknameValidation.ok)
            throw new InvalidOperationException(nicknameValidation.error);
        settings.Username = activeProfile.Username;
        settings.MaximumRamMb = (int)ram.Value;
        settings.FpsBoost = boost.Checked;
        settings.GameDirectory = gameDir.Text.Trim();
        settings.LastVersion = version.SelectedItem?.ToString() ?? settings.LastVersion;
        settings.ModLoader = gameLoader.SelectedItem?.ToString() switch { "Forge" => "forge", "Quilt" => "quilt", "Vanilla" => "vanilla", _ => "fabric" };
        settings.ScreenWidth = (int)(widthInput?.Value ?? settings.ScreenWidth);
        settings.ScreenHeight = (int)(heightInput?.Value ?? settings.ScreenHeight);
        settings.FullScreen = fullScreenInput?.Checked ?? settings.FullScreen;
        settings.CloseAfterLaunch = closeAfterLaunchInput?.Checked ?? settings.CloseAfterLaunch;
        settings.AutoRepair = autoRepairInput?.Checked ?? settings.AutoRepair;
        settings.ExtraJvmArguments = jvmArgsInput?.Text.Trim() ?? settings.ExtraJvmArguments;
        Directory.CreateDirectory(settings.GameDirectory);
        launcher = null;
        SettingsService.Save(settings);
        status.Text = "Настройки сохранены";
    }

    MinecraftLauncher GetLauncher()
    {
        if (launcher != null) return launcher;

        if (string.IsNullOrWhiteSpace(settings.GameDirectory))
            throw new InvalidOperationException("Не выбрана папка Minecraft.");

        Directory.CreateDirectory(settings.GameDirectory);
        launcher = new MinecraftLauncher(new MinecraftPath(settings.GameDirectory));
        return launcher;
    }

    void SetBusy(bool busy,string? message=null){play.Enabled=!busy;install.Enabled=!busy;Cursor=busy?Cursors.WaitCursor:Cursors.Default;if(message!=null)status.Text=message;}
    bool IsKnownLaunchError(Exception ex)
    {
        var message = ex.ToString();
        if (message.Contains("jvm.cfg", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Java executable", StringComparison.OrdinalIgnoreCase))
            return false;
        return message.Contains("Could not create Java Virtual Machine", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("Cannot find version", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("checksum", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("NoClassDefFoundError", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("Could not find or load main class", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("Invalid maximum heap size", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("Could not reserve enough space for object heap", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("UnsupportedClassVersionError", StringComparison.OrdinalIgnoreCase);
    }

    async Task RunAutoRepairAsync(Label result)
    {
        try
        {
            SetBusy(true, "Проверяю настройки и файлы…");
            var notes = new List<string>();
            Directory.CreateDirectory(settings.GameDirectory);
            var testFile = Path.Combine(settings.GameDirectory, ".novacraft-write-test");
            await File.WriteAllTextAsync(testFile, "ok");
            File.Delete(testFile);
            notes.Add("Папка игры доступна для записи.");

            notes.Add((await CheckJavaAsync()).Replace("\n", " "));

            var availableMb = (int)(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024 / 1024);
            var safeLimit = Math.Clamp((int)(availableMb * 0.55), 1024, 8192);
            if (settings.MaximumRamMb > safeLimit)
            {
                settings.MaximumRamMb = safeLimit;
                ram.Value = Math.Clamp(safeLimit, (int)ram.Minimum, (int)ram.Maximum);
                notes.Add("Лимит памяти уменьшен до безопасного значения для доступной памяти.");
            }
            else notes.Add("Лимит памяти выглядит допустимым.");

            var selected = version.SelectedItem?.ToString() ?? settings.LastVersion;
            if (!string.IsNullOrWhiteSpace(selected))
            {
                await InstallSelectedLoaderAsync(selected);
                notes.Add("Официальные файлы выбранной версии проверены и недостающие компоненты восстановлены.");
            }
            settings.LastVersion = selected;
            SettingsService.Save(settings);
            result.Text = "Проверка завершена.\n\n" + string.Join("\n", notes);
            status.Text = "Проверка и восстановление завершены";
            WriteLog("Автопроверка: " + string.Join(" | ", notes));
        }
        catch (Exception ex)
        {
            WriteLog("Автоисправление: " + ex);
            result.Text = "Не все проблемы удалось исправить автоматически.\n" + FriendlyError(ex) + "\n\nПодробности доступны в техническом отчёте.";
            status.Text = "Нужна дополнительная проверка";
        }
        finally { SetBusy(false); }
    }

    string FriendlyError(Exception ex)
    {
        var message = ex.ToString();
        if (message.Contains("jvm.cfg", StringComparison.OrdinalIgnoreCase))
            return "Повреждена конфигурация Java. Повтори установку выбранной версии Minecraft; если ошибка останется, приложи технический отчёт.";
        if (message.Contains("Could not create Java Virtual Machine", StringComparison.OrdinalIgnoreCase))
            return "Java не смогла выделить память. Уменьши лимит ОЗУ и закрой другие программы перед запуском.";
        if (message.Contains("Cannot find version", StringComparison.OrdinalIgnoreCase))
            return "Файлы выбранной версии не найдены или повреждены. Запусти проверку и восстановление.";
        if (message.Contains("timed out", StringComparison.OrdinalIgnoreCase) || message.Contains("Name or service not known", StringComparison.OrdinalIgnoreCase))
            return "Не удалось связаться с сервером загрузки. Проверь интернет-соединение и повтори попытку.";
        if (message.Contains("Access to the path", StringComparison.OrdinalIgnoreCase) || message.Contains("UnauthorizedAccess", StringComparison.OrdinalIgnoreCase))
            return "Нет доступа к папке игры. Выбери другую папку, в которую разрешена запись.";
        return "Произошла ошибка. Технические подробности сохранены во внутреннем отчёте.";
    }

    void WriteLog(string text) => log.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + text + Environment.NewLine);

    void Fail(string message, Exception ex)
    {
        WriteLog(message + ": " + ex);
        status.Text = message;
        MessageBox.Show(FriendlyError(ex), "NovaCraft — проблема", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

}

static class AppDiagnostics
{
    static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NovaCraft",
        "logs");

    public static void Report(Exception ex, string source)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            var file = Path.Combine(DirectoryPath, "startup-errors.log");
            File.AppendAllText(
                file,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}\n{ex}\n\n");
        }
        catch
        {
            // Ошибка журналирования не должна мешать закрытию приложения.
        }
    }
}

static class SettingsService
{
    static readonly string PathName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NovaCraft", "settings.json");

    public static LauncherSettings Load()
    {
        if (!File.Exists(PathName)) return new LauncherSettings();
        try
        {
            var loaded = JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(PathName)) ?? new LauncherSettings();
            loaded.SchemaVersion = Math.Max(loaded.SchemaVersion, 2);
            if (string.IsNullOrWhiteSpace(loaded.GameDirectory))
                loaded.GameDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NovaCraft", "minecraft");
            loaded.MaximumRamMb = Math.Clamp(loaded.MaximumRamMb, 1024, 8192);
            loaded.ScreenWidth = Math.Clamp(loaded.ScreenWidth, 800, 3840);
            loaded.ScreenHeight = Math.Clamp(loaded.ScreenHeight, 600, 2160);
            if (loaded.ModLoader is not ("vanilla" or "fabric" or "forge" or "quilt"))
                loaded.ModLoader = "fabric";
            if (string.IsNullOrWhiteSpace(loaded.Username))
                loaded.Username = "NovaPlayer";
            loaded.SavedBuilds ??= new List<string>();
            loaded.OfflineProfiles ??= new List<OfflineProfile>();
            if (loaded.OfflineProfiles.Count == 0)
            {
                var profileName = string.IsNullOrWhiteSpace(loaded.Username) ? "NovaPlayer" : loaded.Username;
                loaded.OfflineProfiles.Add(new OfflineProfile { Username = profileName, Uuid = OfflineUuid(profileName).ToString() });
            }
            if (string.IsNullOrWhiteSpace(loaded.ActiveProfileId) || !loaded.OfflineProfiles.Any(x => x.Id == loaded.ActiveProfileId))
                loaded.ActiveProfileId = loaded.OfflineProfiles[0].Id;
            loaded.Username = loaded.OfflineProfiles.First(x => x.Id == loaded.ActiveProfileId).Username;
            foreach (var profile in loaded.OfflineProfiles)
            {
                if (!Guid.TryParse(profile.Uuid, out _))
                    profile.Uuid = OfflineProfileRules.OfflineUuid(profile.Username);
            }
            return loaded;
        }
        catch (Exception ex)
        {
            var backupMessage = "Не удалось сохранить резервную копию.";
            try
            {
                var backup = PathName + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                File.Copy(PathName, backup, false);
                backupMessage = "Повреждённый файл сохранён как " + backup;
            }
            catch { }
            MessageBox.Show("Файл настроек повреждён или имеет неверный формат. NovaCraft запустится с настройками по умолчанию.\n" + backupMessage + "\n\nПричина: " + ex.Message,
                "Восстановление настроек", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return new LauncherSettings();
        }
    }

    public static void Save(LauncherSettings s)
    {
        var directory = Path.GetDirectoryName(PathName)!;
        Directory.CreateDirectory(directory);
        var temporary = PathName + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, PathName, true);
    }
}
