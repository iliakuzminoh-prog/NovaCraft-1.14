using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security;
using System.Security.Cryptography;
using System.IO.Compression;
using System.Diagnostics;
using CmlLib.Core;
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
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }
}

internal sealed class LauncherSettings
{
    public string GameDirectory { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NovaCraft", "minecraft");
    public string Username { get; set; } = "NovaPlayer";
    public int MaximumRamMb { get; set; } = 1024;
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
}

internal sealed class MainForm : Form
{
    readonly LauncherSettings settings;
    MinecraftLauncher launcher;
    readonly TextBox username = new();
    readonly ComboBox version = new();
    readonly ComboBox gameLoader = new();
    readonly NumericUpDown ram = new();
    readonly TextBox gameDir = new();
    readonly CheckBox boost = new();
    readonly RichTextBox log = new();
    readonly Label status = new();
    readonly Panel pageHost = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(18, 19, 23) };
    readonly List<TabPage> navigationPages = new();
    readonly List<Button> navigationButtons = new();
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
    readonly HttpClient http = new(new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All }) { Timeout = TimeSpan.FromMinutes(3) };

    public MainForm()
    {
        settings = SettingsService.Load();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("NovaCraft/1.14 (https://github.com/iliakuzminoh-prog/NovaCraft-1.14)");
        Directory.CreateDirectory(settings.GameDirectory);
        launcher = new MinecraftLauncher(new MinecraftPath(settings.GameDirectory));

        Text = "NovaCraft 1.14";
        Width = 1180; Height = 760; MinimumSize = new Size(980, 650);
        BackColor = Color.FromArgb(18, 19, 23); ForeColor = Color.White;
        Font = new Font("Segoe UI", 10); StartPosition = FormStartPosition.CenterScreen;

        BuildUi();
        Shown += async (_, _) => { await RefreshVersionsAsync(); await LoadPopularModsAsync(); };
    }

    void BuildUi()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = Color.FromArgb(24, 26, 34), Padding = new Padding(18, 8, 18, 8) };
        header.Controls.Add(new Label { Text = "◆  NOVACRAFT", Font = new Font("Segoe UI Semibold", 19, FontStyle.Bold), AutoSize = true, Location = new Point(18, 13), ForeColor = Color.White });
        header.Controls.Add(new Label { Text = "Minecraft Java · лаунчер без рекламы и фоновых служб", AutoSize = true, Location = new Point(220, 23), ForeColor = Color.FromArgb(160, 170, 190) });

        var sidebar = new Panel { Dock = DockStyle.Left, Width = 188, BackColor = Color.FromArgb(22, 24, 32), Padding = new Padding(10, 14, 10, 10) };
        var navScroll = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Color.Transparent };
        sidebar.Controls.Add(navScroll);

        var pages = new (string Name, TabPage Page)[]
        {
            ("⌂  Главная", HomePage()),
            ("▣  Сборки", BuildsPage()),
            ("✦  Моды", ModsPage()),
            ("▧  Ресурспаки", CatalogPage("Ресурспаки Modrinth", "resourcepack")),
            ("✧  Шейдеры", CatalogPage("Шейдеры Modrinth", "shader")),
            ("▦  Датапаки", CatalogPage("Датапаки Modrinth", "datapack")),
            ("▤  Каталог", CatalogPage("Сборки Modrinth", "modpack")),
            ("♫  Музыка", MusicPage()),
            ("♙  Аккаунт", AccountPage()),
            ("⚙  Настройки", SettingsPage()),
            ("✓  Исправление", DiagnosticsPage())
        };

        foreach (var item in pages)
        {
            item.Page.Dock = DockStyle.Fill;
            item.Page.Visible = false;
            navigationPages.Add(item.Page);
            pageHost.Controls.Add(item.Page);

            var button = new Button
            {
                Text = item.Name,
                Width = 166,
                Height = 42,
                Margin = new Padding(0, 2, 0, 4),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(12, 0, 4, 0),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(22, 24, 32),
                ForeColor = Color.FromArgb(190, 198, 215),
                Font = new Font("Segoe UI", 9.5f)
            };
            button.FlatAppearance.BorderSize = 0;
            var index = navigationPages.Count - 1;
            button.Click += (_, _) => ShowNavigationPage(index);
            navigationButtons.Add(button);
            navScroll.Controls.Add(button);
        }

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
        ShowNavigationPage(0);
    }

    void ShowNavigationPage(int index)
    {
        if (index < 0 || index >= navigationPages.Count) return;
        for (var i = 0; i < navigationPages.Count; i++)
        {
            navigationPages[i].Visible = i == index;
            if (i == index) navigationPages[i].BringToFront();
            navigationButtons[i].BackColor = i == index ? Color.FromArgb(83, 47, 174) : Color.FromArgb(22, 24, 32);
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
                button.BackColor = button.Text.Contains("ИГРАТЬ", StringComparison.OrdinalIgnoreCase)
                    ? Color.FromArgb(38, 190, 143) : Color.FromArgb(38, 44, 56);
                button.ForeColor = Color.White;
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
        var p = new TabPage("Главная") { BackColor = Color.FromArgb(18, 19, 23), Padding = new Padding(12) };
        var hero = new Panel { Dock = DockStyle.Top, Height = 250, BackColor = Color.FromArgb(35, 29, 58), Margin = new Padding(0, 0, 0, 12) };
        var artwork = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(35, 29, 58), Image = LoadHeroArt() };
        hero.Controls.Add(artwork);

        var lower = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(18, 19, 23), Padding = new Padding(0, 12, 0, 0) };
        var launchPanel = new Panel { Dock = DockStyle.Left, Width = 350, BackColor = Color.FromArgb(27, 30, 40), Padding = new Padding(18) };
        var feedPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(18, 19, 23), Padding = new Padding(12, 0, 0, 0) };

        launchPanel.Controls.Add(new Label { Text = "БЫСТРЫЙ ЗАПУСК", Font = new Font("Segoe UI Semibold", 14, FontStyle.Bold), AutoSize = true, Location = new Point(18, 15), ForeColor = Color.White });
        username.Text = settings.Username;
        launchPanel.Controls.Add(Labeled("Имя игрока", username, 18, 50, 300));

        version.DropDownStyle = ComboBoxStyle.DropDownList;
        launchPanel.Controls.Add(Labeled("Версия Minecraft", version, 18, 118, 300));
        gameLoader.DropDownStyle = ComboBoxStyle.DropDownList;
        gameLoader.Items.Clear();
        gameLoader.Items.AddRange(new object[] { "Vanilla", "Fabric", "Quilt", "Forge" });
        gameLoader.SelectedItem = settings.ModLoader switch { "forge" => "Forge", "quilt" => "Quilt", "vanilla" => "Vanilla", _ => "Fabric" };
        gameLoader.SelectedIndexChanged += (_, _) =>
        {
            settings.ModLoader = gameLoader.SelectedItem?.ToString() switch { "Forge" => "forge", "Quilt" => "quilt", "Vanilla" => "vanilla", _ => "fabric" };
            if (modLoader.Items.Contains(gameLoader.SelectedItem)) modLoader.SelectedItem = gameLoader.SelectedItem;
            SettingsService.Save(settings);
        };
        launchPanel.Controls.Add(Labeled("Загрузчик", gameLoader, 18, 186, 300));

        boost.Text = "Профиль оптимизации JVM";
        boost.Checked = settings.FpsBoost;
        boost.AutoSize = true;
        boost.Location = new Point(20, 258);
        launchPanel.Controls.Add(boost);

        play.Text = "▶  ИГРАТЬ";
        play.Font = new Font("Segoe UI Semibold", 12, FontStyle.Bold);
        play.SetBounds(18, 292, 300, 48);
        play.Click += async (_, _) => await LaunchAsync();
        launchPanel.Controls.Add(play);

        install.Text = "Установить / восстановить версию";
        install.SetBounds(18, 350, 300, 38);
        install.Click += async (_, _) => await InstallAsync();
        launchPanel.Controls.Add(install);

        var ramInfo = new Label { Text = $"Память игры: {settings.MaximumRamMb} МБ  ·  Подробнее в настройках", AutoSize = true, ForeColor = Color.Silver, Location = new Point(18, 402) };
        launchPanel.Controls.Add(ramInfo);

        feedPanel.Controls.Add(new Label { Text = "ПОПУЛЯРНЫЕ МОДЫ", Font = new Font("Segoe UI Semibold", 15, FontStyle.Bold), AutoSize = true, Location = new Point(12, 2), ForeColor = Color.White });
        var seeAll = new Button { Text = "Весь каталог →", Width = 120, Height = 30, Anchor = AnchorStyles.Top | AnchorStyles.Right };
        seeAll.Location = new Point(Math.Max(12, feedPanel.ClientSize.Width - 140), 0);
        seeAll.Click += (_, _) => ShowNavigationPage(2);
        feedPanel.Resize += (_, _) => seeAll.Location = new Point(Math.Max(12, feedPanel.ClientSize.Width - 140), 0);
        feedPanel.Controls.Add(seeAll);
        popularFeed.Dock = DockStyle.Fill;
        popularFeed.Padding = new Padding(4, 40, 4, 4);
        feedPanel.Controls.Add(popularFeed);

        lower.Controls.Add(feedPanel);
        lower.Controls.Add(launchPanel);
        p.Controls.Add(lower);
        p.Controls.Add(hero);
        return p;
    }

    Image? LoadHeroArt()
    {
        try
        {
            var assembly = typeof(Program).Assembly;
            var name = assembly.GetManifestResourceNames().FirstOrDefault(x => x.EndsWith("novacraft-hero.webp", StringComparison.OrdinalIgnoreCase));
            if (name == null) return null;
            using var stream = assembly.GetManifestResourceStream(name);
            if (stream == null) return null;
            using var source = Image.FromStream(stream);
            return new Bitmap(source);
        }
        catch { return null; }
    }

    TabPage VersionsPage()
    {
        var p = new TabPage("Версии") { BackColor = Color.FromArgb(18,19,23) };
        p.Controls.Add(new Label { Text = "Релизные версии из официального манифеста Minecraft.", AutoSize = true, Location = new Point(22,20), ForeColor = Color.LightGray });
        var list = new ListBox { Location = new Point(22,55), Size = new Size(1050,520), BackColor = Color.FromArgb(25,27,32), ForeColor = Color.White, BorderStyle = BorderStyle.None };
        list.DoubleClick += (_,_) => { if (list.SelectedItem is string s) { version.Text=s; status.Text="Выбрана версия "+s; } };
        p.Controls.Add(list); version.Tag = list; return p;
    }

    TabPage ModsPage()
    {
        var p = new TabPage("Моды") { BackColor = Color.FromArgb(18, 19, 23) };
        var toolbar = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = Color.FromArgb(18, 19, 23), Padding = new Padding(12) };
        var search = new TextBox { PlaceholderText = "Поиск модов Modrinth…", Width = 390, Height = 34, Location = new Point(10, 14), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        var btn = new Button { Text = "Найти", Location = new Point(410, 13), Width = 90, Height = 34 };
        modLoader.DropDownStyle = ComboBoxStyle.DropDownList;
        modLoader.Items.Clear();
        modLoader.Items.AddRange(new object[] { "Fabric", "Quilt", "Forge" });
        modLoader.SelectedItem = settings.ModLoader switch { "forge" => "Forge", "quilt" => "Quilt", _ => "Fabric" };
        modLoader.Location = new Point(510, 14); modLoader.Width = 125; modLoader.Height = 34;
        var hint = new Label { Text = "Автор · описание · загрузки · иконка из Modrinth", AutoSize = true, ForeColor = Color.Silver, Location = new Point(650, 21) };
        toolbar.Controls.AddRange(new Control[] { search, btn, modLoader, hint });
        toolbar.Resize += (_, _) => { search.Width = Math.Max(180, toolbar.ClientSize.Width - 760); btn.Left = search.Right + 10; modLoader.Left = btn.Right + 10; hint.Left = modLoader.Right + 14; };
        btn.Click += async (_, _) => await SearchModsAsync(search.Text);
        search.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await SearchModsAsync(search.Text); } };
        modLoader.SelectedIndexChanged += (_, _) =>
        {
            settings.ModLoader = modLoader.SelectedItem?.ToString() switch { "Forge" => "forge", "Quilt" => "quilt", _ => "fabric" };
            if (gameLoader.Items.Contains(modLoader.SelectedItem)) gameLoader.SelectedItem = modLoader.SelectedItem;
            SettingsService.Save(settings);
        };
        modCards.Dock = DockStyle.Fill;
        p.Controls.Add(modCards);
        p.Controls.Add(toolbar);
        return p;
    }

    TabPage CatalogPage(string title, string projectType)
    {
        var p = new TabPage(title) { BackColor = Color.FromArgb(18, 19, 23) };
        var toolbar = new Panel { Dock = DockStyle.Top, Height = 64, Padding = new Padding(12), BackColor = Color.FromArgb(18, 19, 23) };
        var search = new TextBox { PlaceholderText = "Найти: название, автор или описание…", Location = new Point(12, 14), Width = 430, Height = 34 };
        var find = new Button { Text = "Найти", Location = new Point(452, 13), Width = 95, Height = 34 };
        var caption = new Label { Text = "Каталог Modrinth · авторы и описания из источника", AutoSize = true, ForeColor = Color.Silver, Location = new Point(565, 21) };
        if (projectType == "datapack")
        {
            var chooseWorld = new Button { Text = "Папка мира…", Location = new Point(565, 13), Width = 125, Height = 34 };
            chooseWorld.Click += (_, _) =>
            {
                using var dialog = new FolderBrowserDialog { Description = "Выбери папку мира Minecraft, в которой находится level.dat", SelectedPath = selectedDatapackWorldDirectory ?? settings.GameDirectory };
                if (dialog.ShowDialog() != DialogResult.OK) return;
                if (!File.Exists(Path.Combine(dialog.SelectedPath, "level.dat")))
                {
                    MessageBox.Show("В выбранной папке не найден level.dat. Нужна папка конкретного мира Minecraft.", "Не папка мира", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                selectedDatapackWorldDirectory = dialog.SelectedPath;
                caption.Text = "Мир: " + Path.GetFileName(dialog.SelectedPath);
            };
            toolbar.Controls.Add(chooseWorld);
            caption.Location = new Point(700, 21);
        }
        var results = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(8), BackColor = Color.FromArgb(18, 19, 23) };
        find.Click += async (_, _) => await SearchProjectsAsync(search.Text, projectType, results);
        search.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await SearchProjectsAsync(search.Text, projectType, results); } };
        toolbar.Controls.AddRange(new Control[] { search, find, caption });
        p.Controls.Add(results);
        p.Controls.Add(toolbar);
        return p;
    }

    TabPage BuildsPage()
    {
        var p = new TabPage("Сборки") { BackColor = Color.FromArgb(18, 19, 23), Padding = new Padding(24) };
        p.Controls.Add(new Label { Text = "МОИ СБОРКИ", Font = new Font("Segoe UI Semibold", 19, FontStyle.Bold), AutoSize = true, ForeColor = Color.White, Location = new Point(24, 22) });
        p.Controls.Add(new Label { Text = "Сохраняй отдельные профили версий и загрузчиков.", AutoSize = true, ForeColor = Color.Silver, Location = new Point(26, 60) });
        var name = new TextBox { PlaceholderText = "Название новой сборки", Location = new Point(26, 102), Width = 300, Height = 34 };
        var add = new Button { Text = "Сохранить текущий профиль", Location = new Point(340, 101), Width = 210, Height = 36 };
        var list = new ListBox { Location = new Point(26, 156), Size = new Size(780, 360), BackColor = Color.FromArgb(27, 31, 39), ForeColor = Color.White, BorderStyle = BorderStyle.None };
        void RefreshBuildList()
        {
            list.Items.Clear();
            foreach (var saved in settings.SavedBuilds)
            {
                var parts = saved.Split('|');
                list.Items.Add(parts.Length >= 3 ? $"{parts[0]}   ·   Minecraft {parts[1]}   ·   {parts[2]}" : saved);
            }
        }
        add.Click += (_, _) =>
        {
            var buildName = string.IsNullOrWhiteSpace(name.Text) ? "Моя сборка " + (settings.SavedBuilds.Count + 1) : name.Text.Trim();
            var gameVersion = version.SelectedItem?.ToString() ?? settings.LastVersion;
            var loader = gameLoader.SelectedItem?.ToString() ?? "Fabric";
            settings.SavedBuilds.Add(buildName + "|" + gameVersion + "|" + loader);
            SettingsService.Save(settings);
            RefreshBuildList();
            status.Text = "Сборка сохранена";
        };
        var launchBuild = new Button { Text = "Запустить выбранную", Location = new Point(26, 535), Width = 190, Height = 40 };
        launchBuild.Click += async (_, _) =>
        {
            if (list.SelectedIndex < 0 || list.SelectedIndex >= settings.SavedBuilds.Count) return;
            var parts = settings.SavedBuilds[list.SelectedIndex].Split('|');
            if (parts.Length < 3) return;
            settings.LastVersion = parts[1];
            if (gameLoader.Items.Contains(parts[2])) gameLoader.SelectedItem = parts[2];
            version.SelectedItem = parts[1];
            SettingsService.Save(settings);
            ShowNavigationPage(0);
            await LaunchAsync();
        };
        var remove = new Button { Text = "Удалить профиль", Location = new Point(230, 535), Width = 150, Height = 40 };
        remove.Click += (_, _) =>
        {
            if (list.SelectedIndex < 0 || list.SelectedIndex >= settings.SavedBuilds.Count) return;
            settings.SavedBuilds.RemoveAt(list.SelectedIndex);
            SettingsService.Save(settings);
            RefreshBuildList();
        };
        p.Controls.AddRange(new Control[] { name, add, list, launchBuild, remove });
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
        var p = new TabPage("Скин") { BackColor = Color.FromArgb(18, 19, 23), Padding = new Padding(26) };
        p.AutoScroll = true;
        p.Controls.Add(new Label { Text = "МОЙ СКИН", Font = new Font("Segoe UI Semibold", 19, FontStyle.Bold), AutoSize = true, ForeColor = Color.White, Location = new Point(24, 24) });
        p.Controls.Add(new Label { Text = "Выбери PNG-файл скина. Он сохранится в профиле NovaCraft на этом компьютере.", AutoSize = false, Size = new Size(790, 42), ForeColor = Color.Silver, Location = new Point(26, 68) });
        var preview = new PictureBox { Location = new Point(26, 125), Size = new Size(256, 256), BackColor = Color.FromArgb(27, 30, 40), SizeMode = PictureBoxSizeMode.Zoom };
        var skinStatus = new Label { Text = "Скин не выбран.", Location = new Point(305, 128), Size = new Size(470, 100), ForeColor = Color.Silver };
        var choose = new Button { Text = "Выбрать PNG…", Location = new Point(305, 240), Width = 170, Height = 40 };
        var remove = new Button { Text = "Убрать скин", Location = new Point(485, 240), Width = 140, Height = 40 };
        var model = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(305, 292), Width = 180 };
        model.Items.AddRange(new object[] { "Classic (обычные руки)", "Slim (узкие руки)" });
        model.SelectedIndex = 0;
        string skinFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NovaCraft", "skins");
        string activeSkin = Path.Combine(skinFolder, "active.png");
        void LoadPreview(string path)
        {
            using var stream = File.OpenRead(path);
            using var loaded = Image.FromStream(stream);
            var copy = new Bitmap(loaded);
            var old = preview.Image;
            preview.Image = copy;
            old?.Dispose();
        }
        if (File.Exists(activeSkin))
        {
            try { LoadPreview(activeSkin); skinStatus.Text = "Активный скин: " + activeSkin; }
            catch (Exception ex) { skinStatus.Text = "Не удалось открыть сохранённый скин: " + ex.Message; }
        }
        choose.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog { Title = "Выбери PNG-скин Minecraft", Filter = "PNG изображение (*.png)|*.png", CheckFileExists = true };
            if (dialog.ShowDialog() != DialogResult.OK) return;
            try
            {
                var info = new FileInfo(dialog.FileName);
                if (info.Length is < 1 or > 5_000_000) throw new InvalidDataException("Размер PNG должен быть от 1 байта до 5 МБ.");
                using var stream = File.OpenRead(dialog.FileName);
                using var image = Image.FromStream(stream, false, true);
                bool validSize = (image.Width == 64 && image.Height == 64) || (image.Width == 64 && image.Height == 32) ||
                    (image.Width == image.Height && image.Width is 128 or 256 or 512 or 1024) ||
                    (image.Width == image.Height * 2 && image.Width is 128 or 256 or 512 or 1024);
                if (!validSize) throw new InvalidDataException("Нужен PNG-скин 64×64 или 64×32 (допускается масштабированный вариант).");
                Directory.CreateDirectory(skinFolder);
                File.Copy(dialog.FileName, activeSkin, true);
                LoadPreview(activeSkin);
                skinStatus.Text = "Скин выбран и сохранён:\n" + activeSkin;
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Некорректный скин", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        };
        remove.Click += (_, _) =>
        {
            try
            {
                if (File.Exists(activeSkin)) File.Delete(activeSkin);
                var old = preview.Image; preview.Image = null; old?.Dispose();
                skinStatus.Text = "Локальный скин удалён.";
            }
            catch (Exception ex) { skinStatus.Text = "Не удалось удалить файл: " + ex.Message; }
        };
        p.Controls.AddRange(new Control[] { preview, skinStatus, choose, remove, model });
        p.Controls.Add(new Label { Text = "ВИДИМОСТЬ НА ПИРАТСКИХ СЕРВЕРАХ", Font = new Font("Segoe UI Semibold", 13, FontStyle.Bold), AutoSize = true, ForeColor = Color.White, Location = new Point(26, 415) });
        p.Controls.Add(new Label { Text = "Чтобы другие игроки увидели твой PNG, сервер должен поддерживать смену скинов, например через SkinsRestorer, и разрешать команду /skin url. Лаунчер не может сам записать скин на чужой сервер.", AutoSize = false, Size = new Size(790, 70), ForeColor = Color.Silver, Location = new Point(26, 450) });
        var openStudio = new Button { Text = "Открыть официальный Skin Studio", Location = new Point(26, 535), Width = 260, Height = 40 };
        openStudio.Click += (_, _) => GenOpenSkinStudio();
        p.Controls.Add(openStudio);
        p.Controls.Add(new Label { Text = "В Skin Studio загрузи active.png и сгенерируй команду /skin url. Выполнить её можно только на сервере с поддержкой SkinsRestorer и разрешением на эту команду. На остальных серверах другие игроки скин не увидят.", AutoSize = false, Size = new Size(790, 82), ForeColor = Color.FromArgb(190, 175, 145), Location = new Point(26, 590) });
        p.Controls.Add(new Label { Text = "КОМАНДА ДЛЯ СЕРВЕРА (если уже есть URL текстуры)", AutoSize = true, ForeColor = Color.White, Location = new Point(26, 690) });
        var textureUrl = new TextBox { Location = new Point(26, 720), Width = 560, Height = 34, PlaceholderText = "https://textures.minecraft.net/texture/…" };
        var copyCommand = new Button { Text = "Скопировать /skin url", Location = new Point(600, 718), Width = 190, Height = 38 };
        copyCommand.Click += (_, _) =>
        {
            var url = textureUrl.Text.Trim();
            if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) || parsed.Scheme != Uri.UriSchemeHttps)
            {
                MessageBox.Show("Вставь корректный HTTPS URL текстуры.", "Проверка URL", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var variant = model.SelectedIndex == 1 ? " slim" : " classic";
            Clipboard.SetText("/skin url \"" + url.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"" + variant);
            skinStatus.Text = "Команда скопирована. Используй её только на сервере с поддержкой SkinsRestorer.";
        };
        p.Controls.AddRange(new Control[] { textureUrl, copyCommand });
        return p;
    }

    static void GenOpenSkinStudio()
    {
        try { Process.Start(new ProcessStartInfo("https://skinsrestorer.net/skin-upload") { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show("Не удалось открыть Skin Studio: " + ex.Message); }
    }

    TabPage SettingsPage()
    {
        var p = new TabPage("Настройки") { BackColor = Color.FromArgb(18, 19, 23), Padding = new Padding(22) };
        p.AutoScroll = true;
        p.Controls.Add(new Label { Text = "НАСТРОЙКИ ИГРЫ", Font = new Font("Segoe UI Semibold", 19, FontStyle.Bold), AutoSize = true, ForeColor = Color.White, Location = new Point(22, 18) });

        p.Controls.Add(new Label { Text = "Папка Minecraft", AutoSize = true, ForeColor = Color.Silver, Location = new Point(24, 68) });
        gameDir.Text = settings.GameDirectory;
        gameDir.SetBounds(24, 94, 590, 34);
        var browse = new Button { Text = "Выбрать…", Location = new Point(625, 93), Width = 110, Height = 36 };
        browse.Click += (_, _) =>
        {
            using var d = new FolderBrowserDialog { Description = "Выберите папку Minecraft", SelectedPath = gameDir.Text };
            if (d.ShowDialog() == DialogResult.OK) gameDir.Text = d.SelectedPath;
        };

        p.Controls.Add(new Label { Text = "Оперативная память для Minecraft (МБ)", AutoSize = true, ForeColor = Color.Silver, Location = new Point(24, 145) });
        ram.Minimum = 1024; ram.Maximum = 8192; ram.Increment = 256;
        ram.Value = Math.Clamp(settings.MaximumRamMb, 1024, 8192);
        ram.SetBounds(24, 171, 180, 34);
        p.Controls.Add(new Label { Text = "CmlLib отвечает за подбор Java при запуске; ручной путь Java в NovaCraft не настраивается.", AutoSize = false, Size = new Size(720, 38), ForeColor = Color.FromArgb(150, 190, 165), Location = new Point(24, 210) });

        p.Controls.Add(new Label { Text = "Разрешение окна", AutoSize = true, ForeColor = Color.Silver, Location = new Point(245, 145) });
        widthInput = new NumericUpDown { Minimum = 800, Maximum = 3840, Increment = 80, Value = Math.Clamp(settings.ScreenWidth, 800, 3840), Location = new Point(245, 171), Width = 100 };
        heightInput = new NumericUpDown { Minimum = 600, Maximum = 2160, Increment = 60, Value = Math.Clamp(settings.ScreenHeight, 600, 2160), Location = new Point(355, 171), Width = 100 };
        fullScreenInput = new CheckBox { Text = "Полный экран", Checked = settings.FullScreen, AutoSize = true, Location = new Point(480, 176), ForeColor = Color.White };
        closeAfterLaunchInput = new CheckBox { Text = "Закрывать лаунчер после запуска игры", Checked = settings.CloseAfterLaunch, AutoSize = true, Location = new Point(24, 230), ForeColor = Color.White };
        autoRepairInput = new CheckBox { Text = "Автоматически исправлять типовые ошибки запуска", Checked = settings.AutoRepair, AutoSize = true, Location = new Point(24, 264), ForeColor = Color.White };

        p.Controls.Add(new Label { Text = "Дополнительные JVM-параметры (необязательно)", AutoSize = true, ForeColor = Color.Silver, Location = new Point(24, 310) });
        jvmArgsInput = new TextBox { Text = settings.ExtraJvmArguments, Location = new Point(24, 406), Width = 710, Height = 34 };
        var note = new Label { Text = "Не меняй параметры, если не знаешь их назначения: неверные аргументы могут помешать запуску.", AutoSize = false, Size = new Size(720, 38), ForeColor = Color.FromArgb(155, 165, 184), Location = new Point(24, 445) };
        var save = new Button { Text = "Сохранить настройки", Location = new Point(24, 500), Width = 190, Height = 42 };
        save.Click += (_, _) =>
        {
            try { SaveSettings(); }
            catch (Exception ex) { Fail("Не удалось сохранить настройки", ex); }
        };

        p.Controls.AddRange(new Control[] { gameDir, browse, ram, widthInput, heightInput, fullScreenInput, closeAfterLaunchInput, autoRepairInput, jvmArgsInput, note, save });
        return p;
    }

    TabPage DiagnosticsPage()
    {
        var p = new TabPage("Исправление") { BackColor = Color.FromArgb(18, 19, 23), Padding = new Padding(24) };
        p.Controls.Add(new Label { Text = "ПРОВЕРКА И АВТОИСПРАВЛЕНИЕ", Font = new Font("Segoe UI Semibold", 19, FontStyle.Bold), AutoSize = true, ForeColor = Color.White, Location = new Point(24, 22) });
        p.Controls.Add(new Label { Text = "Проверяются папка игры, доступ на запись, параметры памяти и файлы выбранной версии. Отдельная диагностика Java не выполняется. Повреждённые или отсутствующие официальные файлы можно восстановить повторной установкой.", AutoSize = false, Size = new Size(760, 70), ForeColor = Color.Silver, Location = new Point(26, 70) });
        var result = new Label { Text = "Проверка ещё не запускалась.", AutoSize = false, Size = new Size(760, 90), ForeColor = Color.FromArgb(190, 205, 220), Location = new Point(26, 155), Padding = new Padding(10), BackColor = Color.FromArgb(27, 31, 39) };
        var repair = new Button { Text = "Проверить и исправить", Location = new Point(26, 265), Width = 220, Height = 44 };
        repair.Click += async (_, _) => await RunAutoRepairAsync(result);
        var copy = new Button { Text = "Скопировать технический отчёт", Location = new Point(260, 265), Width = 235, Height = 44 };
        copy.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(log.Text)) Clipboard.SetText(log.Text); status.Text = "Отчёт скопирован"; };
        p.Controls.AddRange(new Control[] { result, repair, copy });
        return p;
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
            var all=await launcher.GetAllVersionsAsync();
            var releases=all?.Where(v=>string.Equals(v?.Type?.ToString(),"release",StringComparison.OrdinalIgnoreCase))
                .Select(v=>v!.Name).Distinct().Take(100).ToList() ?? new List<string>();
            version.Items.Clear(); foreach(var v in releases) version.Items.Add(v);
            version.SelectedItem = releases.Contains(settings.LastVersion) ? settings.LastVersion : releases.FirstOrDefault();
            if(version.Tag is ListBox list){list.Items.Clear();list.Items.AddRange(releases.ToArray());}
            WriteLog("Версий получено: "+releases.Count); status.Text="Готов";
        } catch(Exception ex){Fail("Не удалось получить список версий",ex);}
    }

    async Task<string> InstallSelectedLoaderAsync(string vanillaVersion)
    {
        var loaderName = settings.ModLoader;
        if (loaderName == "vanilla")
        {
            await launcher.InstallAsync(vanillaVersion);
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
            var forge = new ForgeInstaller(launcher);
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

        await launcher.InstallAsync(installedId);
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

    MLaunchOption CreateLaunchOptions()
    {
        var option = new MLaunchOption
        {
            Session = MSession.CreateOfflineSession(username.Text.Trim()),
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
        var playerName = username.Text.Trim();
        if (playerName.Length is < 3 or > 16 || playerName.Any(c => !(c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_')))
        {
            MessageBox.Show("Имя игрока должно содержать от 3 до 16 символов: только латинские буквы, цифры и знак подчёркивания.", "Некорректный ник", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        string launchVersion = v;
        try
        {
            SaveSettings();
            SetBusy(true, "Подготовка игры…");
            launchVersion = await InstallSelectedLoaderAsync(v);
            var process = await launcher.BuildProcessAsync(launchVersion, CreateLaunchOptions());
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
                        settings.MaximumRamMb = Math.Max(1024, Math.Min(settings.MaximumRamMb - 512, 2048));
                        ram.Value = Math.Clamp(settings.MaximumRamMb, (int)ram.Minimum, (int)ram.Maximum);
                    }
                    SettingsService.Save(settings);
                    await launcher.InstallAsync(launchVersion);
                    var repaired = await launcher.BuildProcessAsync(launchVersion, CreateLaunchOptions());
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
        var card = new Panel { Width = 326, Height = 174, Margin = new Padding(7), BackColor = Color.FromArgb(29, 33, 42), BorderStyle = BorderStyle.FixedSingle, Padding = new Padding(10) };
        icon = new PictureBox { Location = new Point(10, 10), Size = new Size(58, 58), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(42, 48, 60), BorderStyle = BorderStyle.FixedSingle };
        icon.Controls.Add(new Label { Text = projectType.ToUpperInvariant(), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.FromArgb(155, 166, 185), Font = new Font("Segoe UI Semibold", 8, FontStyle.Bold) });
        var name = new Label { Text = title, Location = new Point(78, 9), Size = new Size(224, 39), ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 11, FontStyle.Bold), AutoEllipsis = true };
        var by = new Label { Text = "Автор: " + author, Location = new Point(80, 49), Size = new Size(220, 22), ForeColor = Color.FromArgb(164, 177, 198), AutoEllipsis = true };
        var desc = new Label { Text = description, Location = new Point(10, 78), Size = new Size(294, 48), ForeColor = Color.FromArgb(211, 216, 225), AutoEllipsis = true };
        var stats = new Label { Text = downloads + " загрузок", Location = new Point(10, 137), Size = new Size(165, 25), ForeColor = Color.FromArgb(153, 166, 187) };
        var canInstallHere = projectType is "mod" or "resourcepack" or "shader" or "datapack";
        var action = new Button { Text = canInstallHere ? "Установить" : "Открыть", Location = new Point(190, 133), Size = new Size(114, 30), Tag = projectId };
        if (projectType == "mod")
        {
            action.Click += async (_, _) => { selectedModProject = projectId; selectedModTitle = title; await DownloadSelectedModAsync(); };
        }
        else if (projectType is "resourcepack" or "shader")
        {
            action.Click += async (_, _) => await DownloadCatalogArchiveAsync(projectId, title, projectType);
        }
        else if (projectType == "datapack")
        {
            action.Click += async (_, _) => await DownloadCatalogArchiveAsync(projectId, title, projectType, selectedDatapackWorldDirectory);
        }
        else
        {
            action.Click += (_, _) =>
            {
                try { Process.Start(new ProcessStartInfo("https://modrinth.com/" + projectType + "/" + Uri.EscapeDataString(slug)) { UseShellExecute = true }); }
                catch (Exception ex) { Fail("Не удалось открыть страницу проекта", ex); }
            };
        }
        card.Controls.AddRange(new Control[] { icon, name, by, desc, stats, action });
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
            popularFeed.Controls.Add(new Label { Text = "Не удалось загрузить ленту Modrinth. Проверь подключение и повтори попытку позже.", AutoSize = false, Size = new Size(520, 60), ForeColor = Color.Silver, Padding = new Padding(12) });
        }
    }

    async Task SearchModsAsync(string query) => await SearchProjectsAsync(query, "mod", modCards);

    async Task SearchProjectsAsync(string query, string projectType, FlowLayoutPanel target)
    {
        if (string.IsNullOrWhiteSpace(query)) return;
        try
        {
            status.Text = "Поиск в каталоге Modrinth…";
            var facets = Uri.EscapeDataString(JsonSerializer.Serialize(new[] { new[] { "project_type:" + projectType } }));
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
                "Проект: " + title + "\\nФайл: " + filename + "\\nMinecraft: " + game +
                "\\nКаталог: " + folderName + "\\nИсточник: Modrinth CDN\\nПроверка: SHA-512.\\n\\nПродолжить загрузку?",
                "Подтверждение установки", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (approval != DialogResult.Yes) return;
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long length && length > 250L * 1024 * 1024)
                throw new InvalidDataException("Архив превышает защитный лимит 250 МБ.");
            var bytes = await ReadBoundedContentAsync(response.Content, 250 * 1024 * 1024);
            if (bytes.Length < 4 || bytes.Length > 250 * 1024 * 1024 || bytes[0] != (byte)'P' || bytes[1] != (byte)'K')
                throw new InvalidDataException("Полученный файл не является допустимым ZIP-архивом.");
            var actual = SHA512.HashData(bytes);
            if (!CryptographicOperations.FixedTimeEquals(actual, Convert.FromHexString(expected)))
                throw new CryptographicException("SHA-512 не совпадает с метаданными Modrinth. Файл не установлен.");
            var temporary = destination + ".download";
            try
            {
                await File.WriteAllBytesAsync(temporary, bytes);
                File.Move(temporary, destination);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            WriteLog("Установлен архив Modrinth: " + destination);
            status.Text = "Архив установлен: " + filename;
        }
        catch (Exception ex) { Fail("Не удалось установить проект Modrinth", ex); }
    }

    async Task DownloadSelectedModAsync()
    {
        if (string.IsNullOrWhiteSpace(selectedModProject)) return;
        var project = selectedModProject;
        try{
            if (settings.ModLoader == "vanilla")
                throw new InvalidOperationException("Для установки модов выбери Fabric, Quilt или Forge. В Vanilla моды не загружаются.");
            status.Text="Получение версии мода…";
            var game=version.SelectedItem?.ToString()??throw new InvalidOperationException("Сначала выберите версию Minecraft.");
            var loaderName = settings.ModLoader;
            var gameFilter = Uri.EscapeDataString(JsonSerializer.Serialize(new[] { game }));
            var loaderFilter = Uri.EscapeDataString(JsonSerializer.Serialize(new[] { loaderName }));
            var url=$"https://api.modrinth.com/v2/project/{Uri.EscapeDataString(project)}/version?game_versions={gameFilter}&loaders={loaderFilter}";
            var arr=JsonNode.Parse(await http.GetStringAsync(url))?.AsArray();
            var file=arr?.FirstOrDefault()?["files"]?.AsArray()?.FirstOrDefault()
                ?? throw new InvalidOperationException("Совместимый файл не найден.");
            var download=file["url"]?.ToString() ?? throw new InvalidOperationException("Ссылка отсутствует.");
            if(!Uri.TryCreate(download,UriKind.Absolute,out var downloadUri) || downloadUri.Scheme!="https" || downloadUri.Host!="cdn.modrinth.com")
                throw new SecurityException("Загрузка разрешена только с официального CDN cdn.modrinth.com.");
            var name=Path.GetFileName(file["filename"]?.ToString()??(project+".jar"));
            if(string.IsNullOrWhiteSpace(name) || !name.EndsWith(".jar",StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Modrinth вернул некорректное имя файла.");
            var expectedSha512=file["hashes"]?["sha512"]?.ToString();
            if(string.IsNullOrWhiteSpace(expectedSha512) || expectedSha512.Length!=128 || !expectedSha512.All(Uri.IsHexDigit))
                throw new InvalidDataException("У файла отсутствует корректный SHA-512 в метаданных Modrinth. Загрузка отменена.");
            var projectTitle = selectedModTitle ?? project;
            var approval=MessageBox.Show(
                "Мод: "+projectTitle+"\nФайл: "+name+"\nВерсия Minecraft: "+game+"\nЗагрузчик: "+loaderName+"\nИсточник: api.modrinth.com / cdn.modrinth.com\nПроверка: SHA-512 по метаданным Modrinth.\n\nМоды — сторонний исполняемый код. Даже успешная проверка хеша не доказывает, что мод безопасен. Продолжить загрузку?",
                "Подтверждение загрузки стороннего мода", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if(approval!=DialogResult.Yes) return;
            var dir=Path.Combine(settings.GameDirectory,"mods"); Directory.CreateDirectory(dir);
            var destination=Path.Combine(dir,name);
            if(File.Exists(destination)) throw new IOException("Этот файл уже есть в папке mods: "+name);
            using var response=await http.GetAsync(downloadUri,HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            if(response.Content.Headers.ContentLength is long declaredLength && declaredLength>250L*1024*1024)
                throw new InvalidDataException("Файл превышает защитный лимит 250 МБ.");
            var bytes=await ReadBoundedContentAsync(response.Content, 250 * 1024 * 1024);
            if(bytes.Length<4 || bytes.Length>250*1024*1024 || bytes[0]!=(byte)'P' || bytes[1]!=(byte)'K')
                throw new InvalidDataException("Файл не является допустимым JAR/ZIP или превышает лимит 250 МБ.");
            var actualSha512=SHA512.HashData(bytes);
            if(!CryptographicOperations.FixedTimeEquals(actualSha512,Convert.FromHexString(expectedSha512)))
                throw new CryptographicException("SHA-512 не совпадает с метаданными Modrinth. Файл не будет установлен.");
            var temporary=destination+".download";
            try { await File.WriteAllBytesAsync(temporary,bytes); File.Move(temporary,destination); }
            finally { if(File.Exists(temporary)) File.Delete(temporary); }
            WriteLog("Мод скачан и проверен SHA-512: "+name);
            status.Text="Хеш совпал. Мод сохранён; безопасность содержимого не гарантируется.";
        }catch(Exception ex){Fail("Не удалось скачать мод",ex);}
    }

    void SaveSettings()
    {
        if (string.IsNullOrWhiteSpace(gameDir.Text))
            throw new InvalidOperationException("Выбери папку Minecraft в настройках.");
        settings.Username = username.Text.Trim();
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
        launcher = new MinecraftLauncher(new MinecraftPath(settings.GameDirectory));
        SettingsService.Save(settings);
        status.Text = "Настройки сохранены";
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

            notes.Add("Отдельная проверка Java не выполняется; её подбор при запуске выполняет CmlLib.");

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

static class SettingsService
{
    static readonly string PathName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NovaCraft", "settings.json");

    public static LauncherSettings Load()
    {
        if (!File.Exists(PathName)) return new LauncherSettings();
        try
        {
            var loaded = JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(PathName)) ?? new LauncherSettings();
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
