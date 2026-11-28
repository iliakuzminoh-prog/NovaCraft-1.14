using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.IO.Compression;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using NovaCraft.UI;
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
    public int SchemaVersion { get; set; } = 3;
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
    public List<BuildProfile> BuildProfiles { get; set; } = new();
    public string ActiveBuildId { get; set; } = "";
    public bool UseDesktopResolution { get; set; } = true;
    public List<string> FavoriteServers { get; set; } = new();
    public List<string> MusicPlaylist { get; set; } = new();
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
    readonly RichTextBox log = new();
    readonly Label status = new();
    readonly HiddenTabControl pageHost = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(18, 19, 23) };
    readonly List<TabPage> navigationPages = new();
    readonly List<Button> navigationButtons = new();
    readonly List<Func<TabPage>> navigationFactories = new();
    readonly List<bool> navigationInitialized = new();
    readonly List<Button> navigationGroupButtons = new();
    readonly List<FlowLayoutPanel> navigationGroupPanels = new();
    readonly List<int> navigationPageGroups = new();
    int activeNavigationGroup;
    string navigationFilter = "";
    TextBox? customMusicPath;
    NumericUpDown? widthInput;
    NumericUpDown? heightInput;
    CheckBox? fullScreenInput;
    CheckBox? closeAfterLaunchInput;
    CheckBox? autoRepairInput;
    CheckBox? useDesktopResolutionInput;
    TextBox? jvmArgsInput;
    readonly AnimatedActionButton play = new();
    readonly AnimatedActionButton install = new();
    readonly AnimatedActionButton profileChip = new();
    readonly Label currentPageCaption = new();
    readonly ComboBox buildSelector = new();
    string pendingModSearch = "";
    readonly List<LauncherDownloadTask> downloadTasks = new();
    FlowLayoutPanel? downloadTaskList;
    Label? downloadSummary;
    Panel? downloadEmptyPanel;
    readonly Dictionary<string, DownloadTaskVisual> downloadTaskVisuals = new();
    ComboBox? quickVersionSelector;
    ComboBox? quickLoaderSelector;
    bool updatingBuildSelector;
    bool isBusy;
    bool isImportingPack;
    bool pageTransitionRunning;
    int lastNavigationIndex = -1;
    HeroScene3D? homeHeroScene;
    Label? homeVersionValue;
    readonly FlowLayoutPanel popularFeed = new() { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(8), BackColor = Color.FromArgb(18, 19, 23) };
    readonly FlowLayoutPanel modCards = new() { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(8), BackColor = Color.FromArgb(18, 19, 23) };
    readonly ComboBox modLoader = new();
    string? selectedModProject;
    string? selectedModTitle;
    string? selectedDatapackWorldDirectory;
    Label? selectedDatapackWorldLabel;
    string modFeedQuery = "";
    string modFeedSort = "downloads";
    int modFeedOffset;
    bool modFeedLoading;
    bool modFeedReloadPending;
    bool showInstalledMods;
    Action? refreshInstalledModsView;
    readonly HttpClient http = new(new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All }) { Timeout = TimeSpan.FromMinutes(3) };
    LocalMusicPlayer? localMusicPlayer;
    System.Windows.Forms.Timer? localMusicTimer;

    public MainForm()
    {
        settings = SettingsService.Load();
        version.SelectedIndexChanged += (_, _) =>
        {
            if (version.SelectedItem is string selectedVersion &&
                !string.IsNullOrWhiteSpace(selectedVersion))
            {
                settings.LastVersion = selectedVersion;
                UpdateHomeHeroMetadata();
            }
        };
        BuildProfileRules.EnsureProfiles(settings);
        gameLoader.Items.AddRange(new object[] { "Vanilla", "Fabric", "Forge", "Quilt" });
        gameLoader.SelectedItem = settings.ModLoader switch
        {
            "vanilla" => "Vanilla",
            "forge" => "Forge",
            "quilt" => "Quilt",
            _ => "Fabric"
        };
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
            var fallbackDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NovaCraft",
                "minecraft-fallback-" + ActiveBuild.Id);

            if (settings.BuildProfiles.Any(p => p.Id != ActiveBuild.Id &&
                string.Equals(Path.GetFullPath(p.GameDirectory), Path.GetFullPath(fallbackDirectory), StringComparison.OrdinalIgnoreCase)))
                fallbackDirectory = BuildProfileRules.CreateInstanceDirectory(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NovaCraft", "minecraft"),
                    ActiveBuild.Id);

            try
            {
                Directory.CreateDirectory(fallbackDirectory);
                ActiveBuild.GameDirectory = Path.GetFullPath(fallbackDirectory);
                settings.GameDirectory = ActiveBuild.GameDirectory;
                SettingsService.Save(settings);
            }
            catch (Exception fallbackError)
            {
                AppDiagnostics.Report(fallbackError, "Settings.GameDirectory.Fallback");
                MessageBox.Show(
                    "Не удалось открыть сохранённую папку сборки или создать резервную. Открой настройки и выбери доступную папку, прежде чем устанавливать или запускать Minecraft.\n\n" + fallbackError.Message,
                    "Папка сборки недоступна",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "NovaCraft 1.14";
        username.ReadOnly = true;
        username.TabStop = false;
        Width = 1180; Height = 760; MinimumSize = new Size(980, 650);
        try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        BackColor = Color.FromArgb(18, 19, 23); ForeColor = Color.White;
        Font = new Font("Segoe UI", 10); StartPosition = FormStartPosition.CenterScreen;

        BuildUi();
        FormClosed += (_, _) =>
        {
            localMusicTimer?.Stop();
            localMusicTimer?.Dispose();
            localMusicPlayer?.Dispose();
            profileChip.Image?.Dispose();
        };

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

    LauncherDownloadTask BeginDownloadTask(string title, string fileName, string source, string? targetBuildName = null)
    {
        var task = new LauncherDownloadTask
        {
            Title = title,
            FileName = fileName,
            Source = source,
            BuildName = targetBuildName ?? ActiveBuild.Name,
            Status = "Загружается",
            Detail = settings.LastVersion + " · " + settings.ModLoader.ToUpperInvariant()
        };
        downloadTasks.Insert(0, task);
        if (downloadTasks.Count > 100)
            downloadTasks.RemoveAll(t => downloadTasks.IndexOf(t) >= 80 &&
                t.Status is "Установлено" or "Ошибка" or "Уже установлено");
        RefreshDownloadTaskPage();
        return task;
    }

    void UpdateDownloadTask(LauncherDownloadTask task, string statusText, double? progress = null, string? detail = null)
    {
        task.Status = statusText;
        if (progress.HasValue)
            task.Progress = Math.Clamp(progress.Value, 0, 1);
        if (detail != null)
            task.Detail = detail;
        RefreshDownloadTaskPage();
    }

    void CompleteDownloadTask(LauncherDownloadTask task, string detail)
    {
        task.Status = "Установлено";
        task.Progress = 1;
        task.Detail = detail;
        RefreshDownloadTaskPage();
    }

    void FailDownloadTask(LauncherDownloadTask task, Exception exception)
    {
        task.Status = "Ошибка";
        task.Detail = exception.Message;
        RefreshDownloadTaskPage();
    }

    sealed class DownloadTaskVisual
    {
        public Panel Card { get; }
        public Label Title { get; }
        public Label State { get; }
        public Label Detail { get; }
        public ProgressBar Progress { get; }

        public DownloadTaskVisual(Panel card, Label title, Label state, Label detail, ProgressBar progress)
        {
            Card = card;
            Title = title;
            State = state;
            Detail = detail;
            Progress = progress;
        }
    }

    DownloadTaskVisual CreateDownloadTaskVisual()
    {
        var cardWidth = Math.Max(560, downloadTaskList!.ClientSize.Width - 36);
        var textWidth = Math.Max(200, cardWidth - 244);
        var card = new Panel
        {
            Width = cardWidth,
            Height = 108,
            Margin = new Padding(4,4,8,8),
            Padding = new Padding(14),
            BackColor = Color.FromArgb(24,27,42),
            BorderStyle = BorderStyle.FixedSingle
        };
        var title = new Label
        {
            Location = new Point(14,10),
            Size = new Size(textWidth,24),
            AutoEllipsis = true,
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold",10.5f,FontStyle.Bold)
        };
        var state = new Label
        {
            Location = new Point(cardWidth - 214,12),
            Size = new Size(190,22),
            TextAlign = ContentAlignment.MiddleRight,
            Font = new Font("Segoe UI Semibold",9,FontStyle.Bold)
        };
        var detail = new Label
        {
            Location = new Point(16,39),
            Size = new Size(cardWidth - 32,32),
            ForeColor = Color.FromArgb(157,169,193),
            AutoEllipsis = true
        };
        var bar = new ProgressBar
        {
            Location = new Point(16,78),
            Size = new Size(cardWidth - 32,12),
            Minimum = 0,
            Maximum = 100,
            Style = ProgressBarStyle.Continuous
        };
        card.Controls.AddRange(new Control[] { title,state,detail,bar });
        return new DownloadTaskVisual(card,title,state,detail,bar);
    }

    void UpdateDownloadTaskVisual(LauncherDownloadTask task, DownloadTaskVisual visual)
    {
        var cardWidth = Math.Max(560, downloadTaskList!.ClientSize.Width - 36);
        visual.Title.Text = task.Title;
        visual.Title.Size = new Size(Math.Max(200, cardWidth - 244),24);
        visual.State.Text = task.Status;
        visual.State.Location = new Point(cardWidth - 214,12);
        visual.State.ForeColor = task.Status switch
        {
            "Установлено" or "Уже установлено" => Color.FromArgb(85,220,150),
            "Ошибка" => Color.FromArgb(248,118,126),
            "Проверка" or "Устанавливается" => Color.FromArgb(181,157,255),
            _ => Color.FromArgb(110,180,255)
        };
        visual.Detail.Text = $"{task.Source}  ·  {task.BuildName}  ·  {task.FileName}\n{task.Detail}";
        visual.Detail.Size = new Size(cardWidth - 32,32);
        visual.Progress.Value = Math.Clamp((int)(task.Progress * 100),0,100);
        visual.Progress.Size = new Size(cardWidth - 32,12);
        visual.Card.Width = cardWidth;
    }

    void RefreshDownloadTaskPage()
    {
        if (downloadTaskList == null || downloadTaskList.IsDisposed)
            return;

        if (downloadTaskList.InvokeRequired)
        {
            try { downloadTaskList.BeginInvoke(new Action(RefreshDownloadTaskPage)); } catch { }
            return;
        }

        downloadTaskList.SuspendLayout();
        var active = downloadTasks.Count(t => t.Status is "Загружается" or "Устанавливается" or "Проверка");
        var installed = downloadTasks.Count(t => t.Status is "Установлено" or "Уже установлено");
        var failed = downloadTasks.Count(t => t.Status == "Ошибка");
        if (downloadSummary != null)
            downloadSummary.Text = $"{active} выполняется    ·    {installed} установлено    ·    {failed} ошибок";

        if (downloadTasks.Count == 0)
        {
            foreach (var visual in downloadTaskVisuals.Values)
                visual.Card.Dispose();
            downloadTaskVisuals.Clear();

            if (downloadEmptyPanel == null || downloadEmptyPanel.IsDisposed)
            {
                downloadEmptyPanel = new Panel
                {
                    Width = Math.Max(560, downloadTaskList.ClientSize.Width - 40),
                    Height = 150,
                    BackColor = Color.FromArgb(24,27,42),
                    Padding = new Padding(20)
                };
                downloadEmptyPanel.Controls.Add(new Label
                {
                    Text = "Здесь появятся загрузки и установки.\n\nКаждый файл будет привязан к активной сборке и получит отдельный статус.",
                    AutoSize = true,
                    Location = new Point(20,25),
                    ForeColor = Color.FromArgb(170,180,200),
                    Font = new Font("Segoe UI", 10)
                });
                downloadTaskList.Controls.Clear();
                downloadTaskList.Controls.Add(downloadEmptyPanel);
            }
            downloadTaskList.ResumeLayout();
            return;
        }

        if (downloadEmptyPanel != null)
        {
            downloadTaskList.Controls.Remove(downloadEmptyPanel);
            downloadEmptyPanel.Dispose();
            downloadEmptyPanel = null;
        }

        var liveIds = downloadTasks.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var staleId in downloadTaskVisuals.Keys.Where(id => !liveIds.Contains(id)).ToList())
        {
            var stale = downloadTaskVisuals[staleId];
            downloadTaskList.Controls.Remove(stale.Card);
            stale.Card.Dispose();
            downloadTaskVisuals.Remove(staleId);
        }

        for (var index = 0; index < downloadTasks.Count; index++)
        {
            var task = downloadTasks[index];
            if (!downloadTaskVisuals.TryGetValue(task.Id, out var visual))
            {
                visual = CreateDownloadTaskVisual();
                downloadTaskVisuals.Add(task.Id, visual);
                downloadTaskList.Controls.Add(visual.Card);
            }

            UpdateDownloadTaskVisual(task, visual);
            if (downloadTaskList.Controls.GetChildIndex(visual.Card) != index)
                downloadTaskList.Controls.SetChildIndex(visual.Card, index);
        }

        downloadTaskList.ResumeLayout();
    }

    BuildProfile ActiveBuild
        => settings.BuildProfiles.First(p => string.Equals(p.Id, settings.ActiveBuildId, StringComparison.OrdinalIgnoreCase));

    void RefreshBuildSelector()
    {
        updatingBuildSelector = true;
        try
        {
            buildSelector.BeginUpdate();
            buildSelector.Items.Clear();
            foreach (var profile in settings.BuildProfiles)
                buildSelector.Items.Add(profile);
            buildSelector.SelectedItem = settings.BuildProfiles.FirstOrDefault(p => p.Id == settings.ActiveBuildId)
                ?? settings.BuildProfiles.FirstOrDefault();
        }
        finally
        {
            buildSelector.EndUpdate();
            updatingBuildSelector = false;
        }

        UpdateHomeHeroMetadata();
    }

    void ActivateBuild(BuildProfile profile, bool persist = true)
    {
        if (profile == null) return;
        if (isBusy)
        {
            status.Text = "Сначала дождись завершения текущей операции со сборкой.";
            RefreshBuildSelector();
            return;
        }
        if (string.Equals(settings.ActiveBuildId, profile.Id, StringComparison.OrdinalIgnoreCase))
            return;

        settings.ActiveBuildId = profile.Id;
        settings.GameDirectory = profile.GameDirectory;
        selectedDatapackWorldDirectory = null;
        if (selectedDatapackWorldLabel != null)
            selectedDatapackWorldLabel.Text = "Мир не выбран";
        settings.LastVersion = profile.MinecraftVersion;
        settings.ModLoader = BuildProfileRules.NormalizeLoader(profile.Loader);
        launcher = null;

        Directory.CreateDirectory(settings.GameDirectory);
        version.Items.Clear();
        version.Items.Add(settings.LastVersion);
        version.SelectedIndex = 0;
        gameLoader.SelectedItem = settings.ModLoader switch
        {
            "vanilla" => "Vanilla",
            "forge" => "Forge",
            "quilt" => "Quilt",
            _ => "Fabric"
        };
        if (modLoader.Items.Count > 0)
            modLoader.SelectedItem = settings.ModLoader switch
            {
                "forge" => "Forge",
                "quilt" => "Quilt",
                _ => "Fabric"
            };

        if (!string.IsNullOrWhiteSpace(gameDir.Text))
            gameDir.Text = settings.GameDirectory;
        if (quickVersionSelector != null)
        {
            quickVersionSelector.Items.Clear();
            quickVersionSelector.Items.Add(settings.LastVersion);
            quickVersionSelector.SelectedIndex = 0;
        }
        if (quickLoaderSelector != null)
            quickLoaderSelector.SelectedItem = settings.ModLoader switch
            {
                "vanilla" => "Vanilla",
                "forge" => "Forge",
                "quilt" => "Quilt",
                _ => "Fabric"
            };

        if (persist)
            SettingsService.Save(settings);

        status.Text = "Активная сборка: " + profile.Name;
        RefreshBuildSelector();

        // The mod feed is version/loader dependent. Discard its old result and
        // reload it for the newly selected build if its page is currently open.
        if (navigationInitialized.Count > 3 && navigationInitialized[3])
        {
            modCards.Controls.Clear();
            modFeedOffset = 0;
            modFeedQuery = "";
            modFeedSort = "downloads";
            if (pageHost.SelectedIndex == 3)
            {
                if (showInstalledMods)
                    refreshInstalledModsView?.Invoke();
                else if (modFeedLoading)
                    modFeedReloadPending = true;
                else
                    _ = LoadModFeedAsync(true);
            }
        }
    }

    static bool IsDirectoryWithin(string rootDirectory, string candidateDirectory)
    {
        var root = Path.GetFullPath(rootDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                   + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(candidateDirectory);
        return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    static Size GetDesktopResolution()
    {
        try
        {
            var primary = Screen.PrimaryScreen;
            if (primary != null)
            {
                var bounds = primary.Bounds;
                if (bounds.Width >= 800 && bounds.Height >= 600)
                    return new Size(bounds.Width, bounds.Height);
            }
        }
        catch { }

        return new Size(1280, 720);
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
            Padding = new Padding(18),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        RoundCorners(card, 16);
        AppTheme.StyleCard(card);

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
        var b = new AnimatedActionButton
        {
            Text = text,
            Width = width,
            Height = height,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(92, 48, 224),
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
            Cursor = Cursors.Hand,
            Padding = new Padding(10, 0, 10, 0),
            Tag = "novacraft-primary"
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

    static Bitmap CreateProfileIcon(string username)
    {
        var safeName = string.IsNullOrWhiteSpace(username) ? "N" : username.Trim();
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(safeName));
        var first = Color.FromArgb(255, 74 + digest[0] % 80, 62 + digest[1] % 68, 150 + digest[2] % 70);
        var second = Color.FromArgb(255, 34 + digest[3] % 36, 36 + digest[4] % 35, 72 + digest[5] % 52);
        var bitmap = new Bitmap(34, 34);

        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using (var background = new LinearGradientBrush(new Rectangle(0, 0, 34, 34), first, second, 45f))
            graphics.FillEllipse(background, 1, 1, 32, 32);
        using (var border = new Pen(Color.FromArgb(100, 220, 220, 255), 1f))
            graphics.DrawEllipse(border, 1, 1, 32, 32);

        var initial = char.ToUpperInvariant(safeName[0]).ToString();
        using var font = new Font("Segoe UI Semibold", 14, FontStyle.Bold);
        using var brush = new SolidBrush(Color.White);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        graphics.DrawString(initial, font, brush, new RectangleF(1, 1, 32, 32), format);

        return bitmap;
    }

    void RefreshProfileChip()
    {
        var activeProfile = settings.OfflineProfiles?.FirstOrDefault(profile =>
            string.Equals(profile.Id, settings.ActiveProfileId, StringComparison.OrdinalIgnoreCase));
        var usernameValue = activeProfile?.Username ?? settings.Username;
        if (string.IsNullOrWhiteSpace(usernameValue))
            usernameValue = "NovaPlayer";

        profileChip.Image?.Dispose();
        profileChip.Image = CreateProfileIcon(usernameValue);
        profileChip.Text = usernameValue;
        profileChip.AccessibleName = "Активный профиль: " + usernameValue;
        UpdateHomeHeroMetadata();
    }

    void UpdateHomeHeroMetadata()
    {
        if (homeHeroScene != null && !homeHeroScene.IsDisposed)
        {
            var activeProfile = settings.OfflineProfiles?.FirstOrDefault(profile =>
                string.Equals(profile.Id, settings.ActiveProfileId, StringComparison.OrdinalIgnoreCase));
            var usernameValue = activeProfile?.Username ?? settings.Username;
            homeHeroScene.GameVersion = string.IsNullOrWhiteSpace(settings.LastVersion)
                ? "—"
                : settings.LastVersion;
            homeHeroScene.ProfileName = string.IsNullOrWhiteSpace(usernameValue)
                ? "OFFLINE PROFILE"
                : usernameValue.Trim().ToUpperInvariant();
            homeHeroScene.Invalidate();
        }

        if (homeVersionValue != null && !homeVersionValue.IsDisposed)
            homeVersionValue.Text = "⬛  " + settings.LastVersion;
    }

    void BuildUi()
    {
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 88,
            BackColor = AppTheme.TopBar,
            Padding = new Padding(16, 8, 16, 8)
        };
        header.Paint += (_, e) =>
        {
            using var pen = new Pen(AppTheme.Border, 1f);
            e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
        };

        var headerLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34f));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 17f));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 11f));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 13f));

        var brand = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Margin = new Padding(2, 0, 8, 0)
        };
        brand.Controls.Add(new BrandCube3D
        {
            Size = new Size(34, 34),
            Location = new Point(0, 7),
            BackColor = Color.Transparent
        });
        brand.Controls.Add(new Label
        {
            Text = "NOVACRAFT",
            AutoSize = true,
            Location = new Point(38, 12),
            ForeColor = AppTheme.Text,
            Font = new Font("Segoe UI Semibold", 18, FontStyle.Bold)
        });
        currentPageCaption.Text = "MINECRAFT JAVA  ·  WINDOWS";
        currentPageCaption.AutoSize = true;
        currentPageCaption.Location = new Point(40, 43);
        currentPageCaption.ForeColor = AppTheme.Muted;
        currentPageCaption.Font = new Font("Segoe UI Semibold", 7.5f, FontStyle.Bold);
        brand.Controls.Add(currentPageCaption);

        var buildPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Padding = new Padding(8, 0, 8, 0),
            Margin = new Padding(2, 0, 2, 0)
        };
        buildPanel.Controls.Add(new Label
        {
            Text = "АКТИВНАЯ СБОРКА",
            AutoSize = true,
            Location = new Point(4, 2),
            ForeColor = AppTheme.Muted,
            Font = new Font("Segoe UI Semibold", 7.5f, FontStyle.Bold)
        });

        buildSelector.DropDownStyle = ComboBoxStyle.DropDownList;
        buildSelector.Dock = DockStyle.Bottom;
        buildSelector.Height = 36;
        buildSelector.BackColor = AppTheme.Input;
        buildSelector.ForeColor = AppTheme.Text;
        buildSelector.Font = new Font("Segoe UI Semibold", 9.5f);
        buildSelector.SelectedIndexChanged += (_, _) =>
        {
            if (updatingBuildSelector || buildSelector.SelectedItem is not BuildProfile selectedBuild) return;
            ActivateBuild(selectedBuild);
        };
        buildPanel.Controls.Add(buildSelector);

        profileChip.Text = "Профиль";
        profileChip.Dock = DockStyle.Fill;
        profileChip.Margin = new Padding(5, 6, 5, 6);
        profileChip.Padding = new Padding(8, 0, 8, 0);
        profileChip.TextAlign = ContentAlignment.MiddleLeft;
        profileChip.ImageAlign = ContentAlignment.MiddleLeft;
        profileChip.TextImageRelation = TextImageRelation.ImageBeforeText;
        profileChip.FlatStyle = FlatStyle.Flat;
        profileChip.FlatAppearance.BorderSize = 1;
        profileChip.FlatAppearance.BorderColor = AppTheme.Border;
        profileChip.BackColor = AppTheme.SurfaceRaised;
        profileChip.ForeColor = AppTheme.Text;
        profileChip.Font = new Font("Segoe UI Semibold", 9f);
        profileChip.Cursor = Cursors.Hand;
        profileChip.AccessibleRole = AccessibleRole.PushButton;
        profileChip.AccessibleDescription = "Открыть управление офлайн-профилями и скинами";
        RoundCorners(profileChip, 12);
        profileChip.Click += (_, _) => ShowNavigationPage(10);

        install.Text = "Установить";
        install.Dock = DockStyle.Fill;
        install.Margin = new Padding(5, 6, 5, 6);
        install.FlatStyle = FlatStyle.Flat;
        install.FlatAppearance.BorderSize = 1;
        install.FlatAppearance.BorderColor = AppTheme.Border;
        install.BackColor = AppTheme.SurfaceRaised;
        install.ForeColor = AppTheme.Text;
        install.Font = new Font("Segoe UI Semibold", 8.5f, FontStyle.Bold);
        install.Cursor = Cursors.Hand;
        install.AccessibleName = "Установить выбранную версию Minecraft";
        RoundCorners(install, 12);
        install.Click += async (_, _) => await InstallAsync();

        play.Text = "▶  Играть";
        play.Dock = DockStyle.Fill;
        play.Margin = new Padding(5, 6, 5, 6);
        play.FlatStyle = FlatStyle.Flat;
        play.FlatAppearance.BorderSize = 0;
        play.FlatAppearance.MouseOverBackColor = AppTheme.AccentHover;
        play.BackColor = AppTheme.Accent;
        play.ForeColor = Color.White;
        play.Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold);
        play.Cursor = Cursors.Hand;
        play.Tag = "novacraft-primary";
        play.AccessibleName = "Запустить выбранную сборку Minecraft";
        RoundCorners(play, 12);
        play.Click += async (_, _) => await LaunchAsync();

        headerLayout.Controls.Add(brand, 0, 0);
        headerLayout.Controls.Add(buildPanel, 1, 0);
        headerLayout.Controls.Add(profileChip, 2, 0);
        headerLayout.Controls.Add(install, 3, 0);
        headerLayout.Controls.Add(play, 4, 0);
        header.Controls.Add(headerLayout);

        RefreshBuildSelector();
        RefreshProfileChip();

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
            ("✦  Моды Modrinth", ModsPage),
            ("✦  Моды CurseForge", CurseForgeCatalogPage),
            ("▧  Ресурспаки", () => CatalogPage("Ресурспаки Modrinth", "resourcepack")),
            ("✧  Шейдеры", () => CatalogPage("Шейдеры Modrinth", "shader")),
            ("▦  Датапаки", () => CatalogPage("Датапаки Modrinth", "datapack")),
            ("▤  Каталог", () => CatalogPage("Сборки Modrinth", "modpack")),
            ("♫  Музыка", MusicPage),
            ("♙  Скины", AccountPage),
            ("⚡  Производительность", PerformancePage),
            ("◆  Сервера", ServersPage),
            ("⚙  Настройки", SettingsPage),
            ("✓  Исправление", DiagnosticsPage),
            ("⇩  Загрузки", DownloadsPage)
        };

        var groups = new (string Name, string Glyph, int[] PageIndices)[]
        {
            ("Главная", "⌂", new[] { 0, 1, 2 }),
            ("Каталог", "▦", new[] { 3, 4, 5, 6, 7, 8 }),
            ("Профили и скины", "♙", new[] { 10 }),
            ("Настройки", "⚙", new[] { 13, 11, 12, 9 }),
            ("Журнал и диагностика", "▤", new[] { 14, 15 })
        };

        var pageButtons = new Button?[pages.Length];
        var pageTabs = new TabPage?[pages.Length];
        var pageGroups = new int[pages.Length];

        for (var groupIndex = 0; groupIndex < groups.Length; groupIndex++)
        {
            var group = groups[groupIndex];

            var groupButton = new Button
            {
                Text = group.Name,
                Tag = groupIndex,
                Width = 226,
                Height = 44,
                Margin = new Padding(0, 4, 0, 2),
                TextAlign = ContentAlignment.MiddleLeft,
                Image = CreateNavIcon(group.Glyph),
                ImageAlign = ContentAlignment.MiddleLeft,
                TextImageRelation = TextImageRelation.ImageBeforeText,
                Padding = new Padding(12, 0, 8, 0),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(22, 24, 35),
                ForeColor = Color.FromArgb(198, 204, 220),
                Font = new Font("Segoe UI Semibold", 9.5f),
                AccessibleRole = AccessibleRole.PushButton,
                AccessibleName = "Раздел " + group.Name
            };
            groupButton.FlatAppearance.BorderSize = 0;
            groupButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(39, 34, 61);

            var childPanel = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Width = 226,
                Margin = new Padding(0, 0, 0, 5),
                Padding = new Padding(0, 0, 0, 0),
                BackColor = Color.Transparent,
                Visible = groupIndex == 0
            };

            foreach (var pageIndex in group.PageIndices)
            {
                if (pageIndex < 0 || pageIndex >= pages.Length)
                    throw new InvalidOperationException($"Некорректный индекс страницы навигации: {pageIndex}.");

                var item = pages[pageIndex];
                var page = new TabPage(item.Name)
                {
                    BackColor = Color.FromArgb(13, 15, 25),
                    Padding = new Padding(24)
                };

                if (pageTabs[pageIndex] != null)
                    throw new InvalidOperationException($"Страница {pageIndex} назначена двум группам навигации.");

                pageTabs[pageIndex] = page;
                pageGroups[pageIndex] = groupIndex;

                var parts = item.Name.Split(new[] { "  " }, StringSplitOptions.None);
                var glyph = parts.Length > 1 ? parts[0] : "◆";
                var caption = parts.Length > 1 ? parts[1] : item.Name;
                var pageButton = new AnimatedNavigationButton
                {
                    Text = caption,
                    Tag = pageIndex,
                    Width = 214,
                    Height = 40,
                    Margin = new Padding(8, 2, 0, 3),
                    TextAlign = ContentAlignment.MiddleLeft,
                    Image = CreateNavIcon(glyph),
                    ImageAlign = ContentAlignment.MiddleLeft,
                    TextImageRelation = TextImageRelation.ImageBeforeText,
                    Padding = new Padding(12, 0, 8, 0),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(15, 17, 27),
                    ForeColor = Color.FromArgb(190, 198, 215),
                    Font = new Font("Segoe UI", 9.25f),
                    AccessibleRole = AccessibleRole.PushButton,
                    AccessibleName = caption
                };
                pageButton.FlatAppearance.BorderSize = 0;
                pageButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(39, 34, 61);

                var tooltip = new ToolTip
                {
                    AutoPopDelay = 5000,
                    InitialDelay = 350,
                    ReshowDelay = 100
                };
                tooltip.SetToolTip(pageButton, caption);

                var selectedPageIndex = pageIndex;
                pageButton.Click += (_, _) => ShowNavigationPage(selectedPageIndex);

                pageButtons[pageIndex] = pageButton;
                childPanel.Controls.Add(pageButton);
            }

            var selectedGroupIndex = groupIndex;
            groupButton.Click += (_, _) =>
            {
                activeNavigationGroup = selectedGroupIndex;
                ApplyNavigationGroups();

                var firstVisiblePage = childPanel.Controls
                    .OfType<Button>()
                    .FirstOrDefault(button => button.Visible);

                if (firstVisiblePage?.Tag is int targetPageIndex)
                    ShowNavigationPage(targetPageIndex);
            };

            navigationGroupButtons.Add(groupButton);
            navigationGroupPanels.Add(childPanel);
            navScroll.Controls.Add(groupButton);
            navScroll.Controls.Add(childPanel);
        }

        for (var pageIndex = 0; pageIndex < pages.Length; pageIndex++)
        {
            var page = pageTabs[pageIndex]
                ?? throw new InvalidOperationException($"Страница {pageIndex} не назначена группе навигации.");
            var pageButton = pageButtons[pageIndex]
                ?? throw new InvalidOperationException($"Для страницы {pageIndex} не создана кнопка навигации.");

            navigationFactories.Add(pages[pageIndex].Factory);
            navigationInitialized.Add(false);
            navigationPages.Add(page);
            pageHost.TabPages.Add(page);
            navigationButtons.Add(pageButton);
            navigationPageGroups.Add(pageGroups[pageIndex]);
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
        navSearch.TextChanged += (_, _) =>
        {
            navigationFilter = navSearch.Text.Trim();
            ApplyNavigationGroups();
        };
        ApplyNavigationGroups();
        ShowNavigationPage(0);
    }

    void ApplyNavigationGroups()
    {
        var searching = !string.IsNullOrWhiteSpace(navigationFilter);

        for (var groupIndex = 0; groupIndex < navigationGroupPanels.Count; groupIndex++)
        {
            var groupPanel = navigationGroupPanels[groupIndex];
            var matchingPages = groupPanel.Controls
                .OfType<Button>()
                .Where(button => !searching ||
                    button.Text.Contains(navigationFilter, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            foreach (var child in groupPanel.Controls.OfType<Button>())
                child.Visible = !searching ||
                    child.Text.Contains(navigationFilter, StringComparison.OrdinalIgnoreCase);

            var groupHasMatches = matchingPages.Length > 0;
            var groupHeaderVisible = !searching || groupHasMatches;
            var groupPanelVisible = searching
                ? groupHasMatches
                : groupIndex == activeNavigationGroup;

            navigationGroupButtons[groupIndex].Visible = groupHeaderVisible;
            groupPanel.Visible = groupPanelVisible;
            navigationGroupButtons[groupIndex].BackColor =
                groupIndex == activeNavigationGroup
                    ? Color.FromArgb(70, 47, 134)
                    : Color.FromArgb(22, 24, 35);
            navigationGroupButtons[groupIndex].ForeColor =
                groupIndex == activeNavigationGroup
                    ? Color.White
                    : Color.FromArgb(198, 204, 220);
        }
    }

    void ShowNavigationPage(int index)
    {
        if (index < 0 || index >= navigationPages.Count) return;

        var oldIndex = pageHost.SelectedIndex;
        var shouldAnimate = oldIndex >= 0 &&
            oldIndex != index &&
            !pageTransitionRunning &&
            IsHandleCreated &&
            pageHost.IsHandleCreated &&
            pageHost.ClientSize.Width > 160 &&
            pageHost.ClientSize.Height > 160 &&
            (long)pageHost.ClientSize.Width * pageHost.ClientSize.Height <= 6_000_000;

        Bitmap? outgoing = null;

        if (shouldAnimate)
        {
            try
            {
                outgoing = CapturePageSurface();
            }
            catch (Exception ex)
            {
                AppDiagnostics.Report(ex, "UI.Transition.CaptureOutgoing");
                outgoing = null;
                shouldAnimate = false;
            }
        }

        ShowNavigationPageCore(index);

        if (!shouldAnimate || outgoing == null)
        {
            lastNavigationIndex = index;
            return;
        }

        Bitmap? incoming = null;
        try
        {
            incoming = CapturePageSurface();
            StartPageDepthTransition(outgoing, incoming, index > oldIndex);
            lastNavigationIndex = index;
        }
        catch (Exception ex)
        {
            AppDiagnostics.Report(ex, "UI.Transition.CaptureIncoming");
            outgoing.Dispose();
            incoming?.Dispose();
            lastNavigationIndex = index;
        }
    }

    void ShowNavigationPageCore(int index)
    {
        if (index < 0 || index >= navigationPages.Count) return;

        if (index < navigationPageGroups.Count)
        {
            activeNavigationGroup = navigationPageGroups[index];
            ApplyNavigationGroups();
        }

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
            AppTheme.Apply(loaded);
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
        var pageTitleParts = navigationPages[index].Text.Split(new[] { "  " }, StringSplitOptions.None);
        var pageTitle = pageTitleParts.Length > 1 ? pageTitleParts[^1] : navigationPages[index].Text;
        currentPageCaption.Text = pageTitle.ToUpperInvariant();

        for (var i = 0; i < navigationPages.Count; i++)
        {
            if (navigationButtons[i] is AnimatedNavigationButton animatedButton)
            {
                animatedButton.Selected = i == index;
            }
            else
            {
                navigationButtons[i].BackColor = i == index
                    ? Color.FromArgb(101, 61, 235)
                    : Color.FromArgb(22, 24, 35);
                navigationButtons[i].ForeColor = i == index
                    ? Color.White
                    : Color.FromArgb(190, 198, 215);
            }
        }
    }

    Bitmap CapturePageSurface()
    {
        var width = Math.Max(1, pageHost.ClientSize.Width);
        var height = Math.Max(1, pageHost.ClientSize.Height);
        var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        pageHost.DrawToBitmap(bitmap, new Rectangle(0, 0, width, height));
        return bitmap;
    }

    void StartPageDepthTransition(Bitmap outgoing, Bitmap incoming, bool forward)
    {
        if (pageTransitionRunning)
        {
            outgoing.Dispose();
            incoming.Dispose();
            return;
        }

        if (pageHost.Parent is not Control host)
        {
            outgoing.Dispose();
            incoming.Dispose();
            return;
        }

        PageDepthTransition? overlay = null;
        EventHandler? completed = null;

        try
        {
            overlay = new PageDepthTransition(outgoing, incoming, forward, 320)
            {
                Bounds = pageHost.Bounds,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };

            completed = (_, _) =>
            {
                if (overlay == null)
                    return;

                if (!overlay.IsDisposed)
                {
                    overlay.Completed -= completed;
                    if (host.Controls.Contains(overlay))
                        host.Controls.Remove(overlay);
                    overlay.Dispose();
                }

                pageHost.Visible = true;
                pageHost.BringToFront();
                pageTransitionRunning = false;
            };

            overlay.Completed += completed;
            pageTransitionRunning = true;
            pageHost.Visible = false;
            host.Controls.Add(overlay);
            overlay.BringToFront();
        }
        catch
        {
            if (overlay != null)
            {
                if (completed != null)
                    overlay.Completed -= completed;
                if (host.Controls.Contains(overlay))
                    host.Controls.Remove(overlay);
                overlay.Dispose();
            }
            else
            {
                outgoing.Dispose();
                incoming.Dispose();
            }

            pageHost.Visible = true;
            pageHost.BringToFront();
            pageTransitionRunning = false;
            throw;
        }
    }

    static void ApplyDarkTheme(Control root) => AppTheme.Apply(root);

    TabPage HomePage()
    {
        var p = new TabPage("Главная") { BackColor = Color.FromArgb(13, 15, 25), Padding = new Padding(18) };

        var top = new Panel { Dock = DockStyle.Top, Height = 52, BackColor = Color.FromArgb(13, 15, 25) };
        var search = new TextBox
        {
            PlaceholderText = "Поиск модов Modrinth… нажми Enter",
            Font = new Font("Segoe UI", 10.5f),
            ForeColor = AppTheme.Text,
            BackColor = AppTheme.Input,
            BorderStyle = BorderStyle.FixedSingle,
            Location = new Point(8, 7),
            Width = 590,
            Height = 36,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        search.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            pendingModSearch = search.Text.Trim();
            ShowNavigationPage(3);
        };
        top.Controls.Add(search);
        var settingsButton = new Button { Text = "⚙", Width = 42, Height = 36, Location = new Point(610, 7), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(29,31,49), ForeColor = Color.White };
        settingsButton.FlatAppearance.BorderSize = 0;
        settingsButton.Click += (_, _) => ShowNavigationPage(13);
        top.Controls.Add(settingsButton);

        void LayoutHomeSearch()
        {
            settingsButton.Left = Math.Max(220, top.ClientSize.Width - settingsButton.Width - 8);
            search.Width = Math.Max(180, settingsButton.Left - search.Left - 10);
        }
        top.Resize += (_, _) => LayoutHomeSearch();
        LayoutHomeSearch();

        var body = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(13, 15, 25), Padding = new Padding(0, 8, 0, 0), AutoScroll = true };

        var hero = new Panel
        {
            Dock = DockStyle.Top,
            Height = 270,
            BackColor = Color.FromArgb(12, 16, 31),
            Padding = Padding.Empty
        };
        void LayoutHomeHero()
        {
            var availableWidth = Math.Max(1, body.ClientSize.Width);
            hero.Height = Math.Clamp((int)Math.Round(availableWidth * 0.28125f), 150, 300);
        }
        body.Resize += (_, _) => LayoutHomeHero();
        LayoutHomeHero();
        var artworkButton = new HeroScene3D
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(12, 16, 31)
        };
        homeHeroScene = artworkButton;
        UpdateHomeHeroMetadata();
        artworkButton.PlayRequested += async (_, _) => await LaunchAsync();
        hero.Controls.Add(artworkButton);
        RoundCorners(hero, 16);

        var content = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0, 14, 0, 30), BackColor = Color.FromArgb(13,15,25) };

        var latest = ModernCard(440, 185, "ПОСЛЕДНЯЯ ВЕРСИЯ", Color.FromArgb(25,28,45));
        homeVersionValue = new Label
        {
            Text = "⬛  " + settings.LastVersion,
            AutoSize = true,
            Location = new Point(18, 45),
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 22, FontStyle.Bold)
        };
        latest.Controls.Add(homeVersionValue);
        latest.Controls.Add(new Label { Text = "●  Установлена", AutoSize = true, Location = new Point(20, 86), ForeColor = Color.FromArgb(50, 225, 140), Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold) });
        var check = ModernButton("↻  Проверить обновления", 230, 38);
        check.Location = new Point(18, 124);
        check.Click += async (_, _) => await RefreshVersionsAsync();
        latest.Controls.Add(check);

        var quick = ModernCard(440, 185, "БЫСТРЫЙ ЗАПУСК", Color.FromArgb(25,28,45));
        var quickVersion = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(18, 42), Width = 250, Height = 32, BackColor = Color.FromArgb(34,37,58), ForeColor = Color.White };
        quickVersion.Items.Add(settings.LastVersion);
        quickVersion.SelectedIndex = 0;
        quickVersionSelector = quickVersion;
        var quickLoader = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(18, 82), Width = 250, Height = 32, BackColor = Color.FromArgb(34,37,58), ForeColor = Color.White };
        quickLoader.Items.AddRange(new object[]{"Fabric","Forge","Quilt","Vanilla"});
        quickLoader.SelectedItem = gameLoader.SelectedItem?.ToString() ?? "Fabric";
        quickLoaderSelector = quickLoader;
        var quickPlay = ModernButton("▶  Запустить", 250, 38);
        quickPlay.Location = new Point(18, 126);
        quickPlay.Click += async (_, _) =>
        {
            if (quickVersion.SelectedItem is string v) version.SelectedItem = v;
            if (quickLoader.SelectedItem is string l && gameLoader.Items.Contains(l)) gameLoader.SelectedItem = l;
            await LaunchAsync();
        };
        quick.Controls.AddRange(new Control[]{quickVersion,quickLoader,quickPlay});

        var buildsCard = ModernCard(440, 185, "МОИ СБОРКИ", Color.FromArgb(25,28,45));
        var buildFlow = new FlowLayoutPanel { Location = new Point(14, 43), Size = new Size(408, 120), AutoScroll = true, WrapContents = false, BackColor = Color.Transparent };
        if (settings.BuildProfiles.Count == 0)
        {
            buildFlow.Controls.Add(new Label { Text = "Создай отдельную сборку или импортируй Modrinth / CurseForge.", AutoSize = true, ForeColor = Color.Silver, Padding = new Padding(10,15,0,0) });
        }
        else
        {
            foreach (var build in settings.BuildProfiles.Take(4))
            {
                var isActive = string.Equals(build.Id, settings.ActiveBuildId, StringComparison.OrdinalIgnoreCase);
                var b = new Panel
                {
                    Width = 175,
                    Height = 100,
                    BackColor = Color.FromArgb(33,37,61),
                    Margin = new Padding(4),
                    Cursor = Cursors.Hand,
                    AccessibleRole = AccessibleRole.PushButton,
                    AccessibleName = "Выбрать сборку " + build.Name
                };
                RoundCorners(b, 12);
                AppTheme.StyleCard(b, isActive);
                var buildGlyph = new Label
                {
                    Text = build.Loader.ToUpperInvariant() switch { "FABRIC" => "F", "FORGE" => "✦", "QUILT" => "Q", _ => "V" },
                    Location = new Point(12, 12),
                    Size = new Size(34, 34),
                    TextAlign = ContentAlignment.MiddleCenter,
                    BackColor = isActive ? Color.FromArgb(87, 67, 151) : Color.FromArgb(47, 56, 82),
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI Semibold", 12, FontStyle.Bold)
                };
                RoundCorners(buildGlyph, 10);
                var buildName = new Label
                {
                    Text = build.Name,
                    Location = new Point(54, 12),
                    Size = new Size(108, 36),
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
                    AutoEllipsis = true
                };
                var buildVersion = new Label
                {
                    Text = build.MinecraftVersion + " · " + build.Loader.ToUpperInvariant(),
                    Location = new Point(12, 62),
                    Size = new Size(150, 20),
                    ForeColor = Color.FromArgb(170,180,205),
                    Font = new Font("Segoe UI", 8.25f)
                };
                b.Controls.AddRange(new Control[] { buildGlyph, buildName, buildVersion });
                void SelectBuildFromTile()
                {
                    ActivateBuild(build);
                    status.Text = "Активная сборка: " + build.Name;
                    ShowNavigationPage(0);
                }
                b.Click += (_, _) => SelectBuildFromTile();
                buildGlyph.Click += (_, _) => SelectBuildFromTile();
                buildName.Click += (_, _) => SelectBuildFromTile();
                buildVersion.Click += (_, _) => SelectBuildFromTile();
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

        var featuredModsCard = ModernCard(880, 540, "ПОПУЛЯРНЫЕ МОДЫ MODRINTH", AppTheme.Surface);
        popularFeed.Dock = DockStyle.None;
        popularFeed.Location = new Point(14, 48);
        popularFeed.Size = new Size(850, 470);
        popularFeed.BackColor = Color.Transparent;
        popularFeed.Padding = new Padding(2, 4, 12, 18);
        featuredModsCard.Controls.Add(popularFeed);

        var refreshFeatured = ModernButton("↻", 38, 34);
        refreshFeatured.Location = new Point(820, 11);
        refreshFeatured.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        refreshFeatured.AccessibleName = "Обновить популярные моды";
        refreshFeatured.Click += async (_, _) => await LoadPopularModsAsync();
        featuredModsCard.Controls.Add(refreshFeatured);

        void LayoutFeaturedMods()
        {
            featuredModsCard.Width = Math.Max(560, content.ClientSize.Width - 18);
            popularFeed.SetBounds(
                14,
                48,
                Math.Max(520, featuredModsCard.ClientSize.Width - 28),
                Math.Max(260, featuredModsCard.ClientSize.Height - 62));
            refreshFeatured.Left = featuredModsCard.ClientSize.Width - refreshFeatured.Width - 16;
        }
        content.Resize += (_, _) => LayoutFeaturedMods();
        LayoutFeaturedMods();

        content.Controls.AddRange(new Control[] { latest, quick, buildsCard, modsCard, featuredModsCard });
        body.Controls.Add(content);
        body.Controls.Add(hero);
        p.Controls.Add(body);
        p.Controls.Add(top);

        p.Enter += async (_, _) =>
        {
            if (popularFeed.Controls.Count == 0)
                await LoadPopularModsAsync();
        };

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
            View = View.Tile,
            TileSize = new Size(800,72),
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
        refresh.Click += async (_, _) =>
        {
            await RefreshVersionsAsync();
            Render(search.Text);
        };

        void LayoutVersionsPage()
        {
            var width = Math.Max(420, p.ClientSize.Width);
            var searchWidth = Math.Clamp(width - 284, 220, 420);
            search.SetBounds(27, 96, searchWidth, 38);
            refresh.SetBounds(search.Right + 8, 96, 110, 38);
            selected.SetBounds(refresh.Right + 12, 103, Math.Max(100, width - refresh.Right - 39), 24);
            list.SetBounds(27, 150, Math.Max(360, width - 54), Math.Max(240, p.ClientSize.Height - 174));
            list.TileSize = new Size(Math.Max(300, list.ClientSize.Width - 24), 72);
        }

        p.Resize += (_, _) => LayoutVersionsPage();
        p.Controls.AddRange(new Control[] { search, refresh, selected, list });
        LayoutVersionsPage();
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

        top.Controls.AddRange(new Control[] { search, searchButton, modLoader, sort, installedViewButton });

        var installedViewButton = ModernButton("Установленные", 152, 34);
        installedViewButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        installedViewButton.Location = new Point(780, 6);

        var installedPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Visible = false,
            BackColor = Color.FromArgb(12, 14, 23)
        };
        var installedToolbar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 58,
            BackColor = Color.FromArgb(12, 14, 23)
        };
        var installedTitle = new Label
        {
            Text = "Установленные моды",
            AutoSize = true,
            Location = new Point(5, 4),
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 14, FontStyle.Bold)
        };
        var installedSummary = new Label
        {
            Text = "Включение и отключение без удаления JAR-файлов.",
            AutoSize = true,
            Location = new Point(7, 32),
            ForeColor = Color.FromArgb(153, 163, 184),
            Font = new Font("Segoe UI", 9f)
        };
        var refreshInstalled = ModernButton("↻ Обновить", 122, 32);
        refreshInstalled.Location = new Point(780, 12);
        refreshInstalled.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        installedToolbar.Controls.AddRange(new Control[] { installedTitle, installedSummary, refreshInstalled });

        var installedList = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            WrapContents = false,
            FlowDirection = FlowDirection.TopDown,
            Padding = new Padding(5, 8, 14, 24),
            BackColor = Color.FromArgb(12, 14, 23)
        };
        installedPanel.Controls.Add(installedList);
        installedPanel.Controls.Add(installedToolbar);

        void LayoutModToolbar()
        {
            var availableWidth = Math.Max(560, top.ClientSize.Width - 8);
            const int buttonWidth = 88;
            const int loaderWidth = 112;
            const int sortWidth = 140;
            const int gap = 8;
            var searchWidth = Math.Clamp(
                availableWidth - buttonWidth - loaderWidth - sortWidth - gap * 3,
                180,
                420);
            search.SetBounds(4, 78, searchWidth, 38);
            searchButton.SetBounds(search.Right + gap, 78, buttonWidth, 38);
            modLoader.SetBounds(searchButton.Right + gap, 78, loaderWidth, 38);
            sort.SetBounds(modLoader.Right + gap, 78, sortWidth, 38);
            installedViewButton.Left = Math.Max(4, top.ClientSize.Width - installedViewButton.Width - 4);
        }

        top.Resize += (_, _) => LayoutModToolbar();
        LayoutModToolbar();

        modCards.Dock = DockStyle.Fill;
        modCards.Padding = new Padding(4, 8, 10, 40);
        modCards.BackColor = Color.FromArgb(12,14,23);
        modCards.WrapContents = true;

        void RenderInstalledMods()
        {
            installedList.SuspendLayout();
            installedList.Controls.Clear();
            try
            {
                var entries = InstalledModService.GetInstalledMods(ActiveBuild.GameDirectory);
                var enabledCount = entries.Count(entry => entry.Enabled);
                var disabledCount = entries.Count - enabledCount;
                installedSummary.Text = $"{entries.Count} файлов · {enabledCount} включено · {disabledCount} отключено";

                if (entries.Count == 0)
                {
                    installedList.Controls.Add(new Label
                    {
                        Text = "Установленных JAR-файлов пока нет.\nЗагруженные моды появятся здесь после установки.",
                        AutoSize = true,
                        ForeColor = Color.FromArgb(165, 176, 198),
                        Padding = new Padding(18)
                    });
                    return;
                }

                foreach (var entry in entries)
                {
                    var cardWidth = Math.Max(560, installedList.ClientSize.Width - 34);
                    var card = new Panel
                    {
                        Width = cardWidth,
                        Height = 82,
                        Margin = new Padding(3, 4, 6, 5),
                        BackColor = Color.FromArgb(24, 27, 42),
                        Padding = new Padding(12)
                    };
                    RoundCorners(card, 12);
                    AppTheme.StyleCatalogCard(card);

                    var glyph = new Label
                    {
                        Text = "JAR",
                        Location = new Point(12, 17),
                        Size = new Size(46, 46),
                        TextAlign = ContentAlignment.MiddleCenter,
                        BackColor = Color.FromArgb(50, 56, 82),
                        ForeColor = Color.FromArgb(206, 211, 255),
                        Font = new Font("Segoe UI Semibold", 10, FontStyle.Bold)
                    };
                    RoundCorners(glyph, 9);

                    var fileName = new Label
                    {
                        Text = entry.FileName,
                        Location = new Point(72, 13),
                        Size = new Size(Math.Max(180, cardWidth - 250), 25),
                        ForeColor = Color.White,
                        Font = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold),
                        AutoEllipsis = true
                    };
                    var state = new Label
                    {
                        Text = (entry.Enabled ? "Включён" : "Отключён") + "  ·  " +
                            (entry.SizeBytes / 1024d / 1024d).ToString("0.00") + " МБ",
                        Location = new Point(73, 42),
                        AutoSize = true,
                        ForeColor = entry.Enabled ? Color.FromArgb(91, 218, 157) : Color.FromArgb(172, 180, 199)
                    };
                    var toggle = ModernButton(entry.Enabled ? "Отключить" : "Включить", 126, 34);
                    toggle.Location = new Point(cardWidth - 146, 23);
                    toggle.Click += (_, _) =>
                    {
                        if (isBusy)
                        {
                            status.Text = "Дождись завершения текущей операции.";
                            return;
                        }

                        try
                        {
                            var changed = InstalledModService.SetEnabled(
                                ActiveBuild.GameDirectory,
                                entry.FileName,
                                !entry.Enabled);
                            status.Text = changed.Enabled
                                ? "Мод включён: " + changed.FileName
                                : "Мод отключён без удаления: " + changed.FileName;
                            RenderInstalledMods();
                        }
                        catch (Exception ex)
                        {
                            Fail("Не удалось изменить состояние мода", ex);
                        }
                    };

                    card.Controls.AddRange(new Control[] { glyph, fileName, state, toggle });
                    installedList.Controls.Add(card);
                }
            }
            catch (Exception ex)
            {
                installedSummary.Text = "Не удалось прочитать папку mods.";
                installedList.Controls.Add(new Label
                {
                    Text = ex.Message,
                    AutoSize = true,
                    ForeColor = Color.FromArgb(245, 132, 142),
                    Padding = new Padding(18)
                });
            }
            finally
            {
                installedList.ResumeLayout();
            }
        }

        void ShowInstalledMods()
        {
            showInstalledMods = true;
            refreshInstalledModsView = RenderInstalledMods;
            installedViewButton.Text = "← Каталог";
            search.Visible = false;
            searchButton.Visible = false;
            modLoader.Visible = false;
            sort.Visible = false;
            modCards.Visible = false;
            installedPanel.Visible = true;
            RenderInstalledMods();
            installedPanel.BringToFront();
            top.BringToFront();
        }

        void ShowModCatalog()
        {
            showInstalledMods = false;
            refreshInstalledModsView = null;
            installedViewButton.Text = "Установленные";
            installedPanel.Visible = false;
            modCards.Visible = true;
            search.Visible = true;
            searchButton.Visible = true;
            modLoader.Visible = true;
            sort.Visible = true;
            modCards.BringToFront();
            top.BringToFront();
        }

        installedViewButton.Click += (_, _) =>
        {
            if (showInstalledMods)
                ShowModCatalog();
            else
                ShowInstalledMods();
        };
        refreshInstalled.Click += (_, _) =>
        {
            if (isBusy)
                return;
            RenderInstalledMods();
        };

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
            if (isBusy)
            {
                var currentLoader = settings.ModLoader switch
                {
                    "forge" => "Forge",
                    "quilt" => "Quilt",
                    "vanilla" => "Vanilla",
                    _ => "Fabric"
                };
                if (!string.Equals(modLoader.SelectedItem?.ToString(), currentLoader, StringComparison.Ordinal))
                    modLoader.SelectedItem = currentLoader;
                status.Text = "Нельзя менять загрузчик во время установки Minecraft.";
                return;
            }
            var nextLoader = modLoader.SelectedItem?.ToString() switch
            {
                "Forge" => "forge",
                "Quilt" => "quilt",
                _ => "fabric"
            };
            if (!string.Equals(settings.ModLoader, nextLoader, StringComparison.OrdinalIgnoreCase))
                ActiveBuild.LoaderVersion = "";
            settings.ModLoader = nextLoader;
            ActiveBuild.Loader = nextLoader;
            if (gameLoader.Items.Contains(modLoader.SelectedItem)) gameLoader.SelectedItem = modLoader.SelectedItem;
            SettingsService.Save(settings);
            RefreshBuildSelector();
        };

        p.Controls.Add(modCards);
        p.Controls.Add(installedPanel);
        p.Controls.Add(top);
        if (showInstalledMods)
            ShowInstalledMods();
        else
            ShowModCatalog();
        p.Enter += async (_, _) =>
        {
            if (showInstalledMods)
            {
                RenderInstalledMods();
                return;
            }

            if (!string.IsNullOrWhiteSpace(pendingModSearch))
            {
                search.Text = pendingModSearch;
                modFeedQuery = pendingModSearch;
                pendingModSearch = "";
                modFeedSort = sort.SelectedIndex switch { 1 => "newest", 2 => "updated", _ => "downloads" };
                modFeedOffset = 0;
                await LoadModFeedAsync(true);
                return;
            }

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

    TabPage CurseForgeCatalogPage()
    {
        var p = new TabPage("CurseForge") { BackColor = Color.FromArgb(12, 14, 23), Padding = new Padding(20) };
        var toolbar = new Panel { Dock = DockStyle.Top, Height = 162, BackColor = Color.FromArgb(12, 14, 23) };
        var heading = new Label
        {
            Text = "CURSEFORGE MOD CATALOG",
            AutoSize = true,
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 23, FontStyle.Bold),
            Location = new Point(4, 2)
        };
        var description = new Label
        {
            Text = "Официальный каталог CurseForge · фильтр по активной сборке · разрешение автора проверяется перед загрузкой",
            AutoSize = false,
            Size = new Size(900, 22),
            AutoEllipsis = true,
            ForeColor = Color.FromArgb(145, 155, 178),
            Location = new Point(7, 40)
        };
        var keyLabel = new Label
        {
            Text = "API-ключ",
            AutoSize = true,
            ForeColor = Color.FromArgb(181, 190, 207),
            Location = new Point(5, 76)
        };
        var keyInput = new TextBox
        {
            PlaceholderText = "Вставь одобренный API-ключ CurseForge",
            UseSystemPasswordChar = true,
            Location = new Point(82, 71),
            Width = 390,
            Height = 34
        };
        var connect = ModernButton("Подключить", 120, 34);
        connect.Location = new Point(480, 70);
        var getKey = new Button
        {
            Text = "Где получить ключ?",
            Location = new Point(610, 70),
            Width = 155,
            Height = 34,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(38, 43, 63),
            ForeColor = Color.White
        };
        getKey.FlatAppearance.BorderSize = 0;
        var search = new TextBox
        {
            PlaceholderText = "⌕  Поиск модов CurseForge…",
            Location = new Point(4, 116),
            Width = 430,
            Height = 36
        };
        var find = ModernButton("Найти", 90, 36);
        find.Location = new Point(442, 116);
        var sort = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(542, 116),
            Width = 165,
            Height = 36,
            BackColor = Color.FromArgb(27, 30, 47),
            ForeColor = Color.White
        };
        sort.Items.AddRange(new object[] { "Популярные", "Обновлённые", "Новые релизы" });
        sort.SelectedIndex = 0;
        var activeBuildCaption = new Label
        {
            Text = "",
            AutoSize = false,
            Location = new Point(720, 119),
            Size = new Size(300, 28),
            ForeColor = Color.FromArgb(145, 155, 178),
            AutoEllipsis = true
        };
        toolbar.Controls.AddRange(new Control[] { heading, description, keyLabel, keyInput, connect, getKey, search, find, sort, activeBuildCaption });

        void LayoutToolbar()
        {
            var width = Math.Max(560, toolbar.ClientSize.Width - 8);
            description.Size = new Size(width - 8, 22);
            keyInput.Width = Math.Max(180, Math.Min(390, width - 580));
            connect.Left = keyInput.Right + 8;
            getKey.Left = connect.Right + 8;
            search.Width = Math.Max(180, Math.Min(430, width - 620));
            find.Left = search.Right + 8;
            sort.Left = find.Right + 8;
            activeBuildCaption.Left = sort.Right + 12;
            activeBuildCaption.Width = Math.Max(170, width - activeBuildCaption.Left + 4);
        }
        toolbar.Resize += (_, _) => LayoutToolbar();
        LayoutToolbar();

        var results = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(4, 8, 10, 60),
            BackColor = Color.FromArgb(12, 14, 23)
        };

        string apiKey = "";
        string query = "";
        var offset = 0;
        var totalCount = 0L;
        var loading = false;
        var connected = false;

        int CurrentSortField() => sort.SelectedIndex switch { 1 => 3, 2 => 11, _ => 2 };

        async Task LoadPage(bool reset)
        {
            if (loading) return;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                status.Text = "Вставь API-ключ и нажми «Подключить». Ключ хранится только в памяти текущего запуска.";
                return;
            }

            loading = true;
            try
            {
                if (reset)
                {
                    offset = 0;
                    totalCount = 0;
                    results.Controls.Clear();
                }

                var build = ActiveBuild;
                if (build.Loader == "vanilla")
                    throw new InvalidOperationException("Для каталога модов выбери Fabric, Forge или Quilt в активной сборке.");

                activeBuildCaption.Text = $"Сборка: {build.MinecraftVersion} · {build.Loader.ToUpperInvariant()}";
                status.Text = offset == 0 ? "Подключаюсь к официальному API CurseForge…" : "Загружаю следующую страницу CurseForge…";

                using var curseForgeHttp = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
                {
                    Timeout = TimeSpan.FromMinutes(2)
                };
                var page = await CurseForgeCatalogService.SearchModsAsync(
                    curseForgeHttp,
                    apiKey,
                    query,
                    build.MinecraftVersion,
                    build.Loader,
                    offset,
                    CurrentSortField(),
                    CancellationToken.None);

                if (reset)
                    totalCount = page.TotalCount;

                if (page.Projects.Count == 0)
                {
                    status.Text = offset == 0 ? "Ничего не найдено для этой версии и загрузчика." : "Больше результатов нет.";
                    return;
                }

                using var iconGate = new SemaphoreSlim(3);
                var iconTasks = new List<Task>();

                foreach (var project in page.Projects)
                {
                    var card = ModernCard(410, 176, project.Name, Color.FromArgb(22, 25, 39));
                    card.Margin = new Padding(7);
                    var iconBox = new PictureBox
                    {
                        Location = new Point(18, 50),
                        Size = new Size(62, 62),
                        BackColor = Color.FromArgb(31, 35, 50),
                        SizeMode = PictureBoxSizeMode.Zoom,
                        Image = CreateProjectFallbackIcon(project.Name, "mod"),
                        AccessibleName = "Иконка проекта CurseForge: " + project.Name
                    };
                    var summary = new Label
                    {
                        Text = string.IsNullOrWhiteSpace(project.Summary) ? "Описание отсутствует." : project.Summary,
                        Location = new Point(92, 48),
                        Size = new Size(300, 56),
                        ForeColor = Color.FromArgb(190, 198, 215),
                        AutoEllipsis = true
                    };
                    var metrics = new Label
                    {
                        Text = $"Загрузки: {project.DownloadCount:N0} · ID: {project.Id}",
                        Location = new Point(92, 107),
                        Size = new Size(300, 22),
                        ForeColor = Color.FromArgb(139, 151, 175),
                        AutoEllipsis = true
                    };
                    var install = ModernButton(project.AllowsDistribution ? "Установить в эту сборку" : "Раздача запрещена автором", 205, 34);
                    install.Location = new Point(18, 130);
                    install.Enabled = project.AllowsDistribution && project.IsAvailable;
                    var openSite = new Button
                    {
                        Text = "Страница проекта",
                        Location = new Point(232, 130),
                        Size = new Size(160, 34),
                        FlatStyle = FlatStyle.Flat,
                        BackColor = Color.FromArgb(38, 43, 63),
                        ForeColor = Color.White
                    };
                    openSite.FlatAppearance.BorderSize = 0;

                    install.Click += async (_, _) =>
                    {
                        if (isBusy || string.IsNullOrWhiteSpace(apiKey)) return;
                        if (!project.AllowsDistribution)
                        {
                            MessageBox.Show("Автор проекта не разрешил стороннюю раздачу. NovaCraft не будет обходить это ограничение.", "CurseForge", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            return;
                        }

                        var targetBuild = ActiveBuild;
                        if (targetBuild.Loader == "vanilla")
                        {
                            MessageBox.Show("Сначала выбери Fabric, Forge или Quilt.", "Загрузчик не выбран", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }

                        LauncherDownloadTask? downloadTask = null;
                        try
                        {
                            SetBusy(true, "Проверяю совместимые файлы " + project.Name + "…");
                            using var downloadHttp = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
                            {
                                Timeout = TimeSpan.FromMinutes(3)
                            };

                            var files = await CurseForgeCatalogService.GetCompatibleFilesAsync(
                                downloadHttp,
                                apiKey,
                                project.Id,
                                targetBuild.MinecraftVersion,
                                targetBuild.Loader,
                                CancellationToken.None);

                            var selectedFile = files.FirstOrDefault(file =>
                                file.IsAvailable &&
                                file.GameVersions.Contains(targetBuild.MinecraftVersion, StringComparer.OrdinalIgnoreCase) &&
                                file.FileSize > 0 &&
                                file.FileSize <= 250L * 1024 * 1024 &&
                                file.Sha1.Length == 40 &&
                                file.Sha1.All(Uri.IsHexDigit));

                            if (selectedFile == null)
                                throw new InvalidOperationException("CurseForge не вернул доступный JAR с проверенной SHA-1 для активной версии Minecraft и выбранного загрузчика.");

                            if (string.IsNullOrWhiteSpace(selectedFile.DownloadUrl))
                            {
                                selectedFile = selectedFile with
                                {
                                    DownloadUrl = await CurseForgeCatalogService.GetFileDownloadUrlAsync(
                                        downloadHttp, apiKey, project.Id, selectedFile.Id, CancellationToken.None)
                                };
                            }

                            var fileName = selectedFile.FileName;
                            var modsDirectory = Path.Combine(targetBuild.GameDirectory, "mods");
                            Directory.CreateDirectory(modsDirectory);
                            var destination = Path.GetFullPath(Path.Combine(modsDirectory, fileName));
                            if (!destination.StartsWith(Path.GetFullPath(modsDirectory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                                throw new InvalidDataException("Путь файла вышел за пределы папки mods.");
                            if (File.Exists(destination))
                                throw new IOException("Такой файл уже есть в папке mods активной сборки. Существующий файл не перезаписан.");

                            downloadTask = BeginDownloadTask(
                                project.Name,
                                fileName,
                                "CurseForge",
                                targetBuild.Name);
                            var progress = new Progress<double>(value =>
                                UpdateDownloadTask(downloadTask, "Загружается", value, "CurseForge · SHA-1 · " + targetBuild.MinecraftVersion));

                            await MinecraftPackImportService.DownloadCurseForgeFileAsync(
                                downloadHttp,
                                selectedFile.DownloadUrl,
                                apiKey,
                                selectedFile.Sha1,
                                destination,
                                progress,
                                CancellationToken.None);

                            if (new FileInfo(destination).Length != selectedFile.FileSize)
                            {
                                File.Delete(destination);
                                throw new InvalidDataException("Размер загруженного файла не совпал с метаданными CurseForge; файл удалён.");
                            }

                            CompleteDownloadTask(downloadTask, "JAR установлен в mods активной сборки после проверки SHA-1 и ZIP-сигнатуры");
                            WriteLog($"CurseForge: установлен {fileName} в сборку {targetBuild.Name}.");
                            status.Text = "Мод CurseForge установлен. Зависимости, если они нужны, пока не устанавливаются автоматически.";
                            MessageBox.Show(
                                "Мод установлен в папку mods выбранной сборки. Проверь описание проекта: дополнительные зависимости могут требовать отдельной установки.",
                                "Установка CurseForge",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Information);
                        }
                        catch (Exception ex)
                        {
                            if (downloadTask != null) FailDownloadTask(downloadTask, ex);
                            Fail("Не удалось установить мод CurseForge", ex);
                        }
                        finally
                        {
                            SetBusy(false);
                        }
                    };

                    openSite.Click += (_, _) =>
                    {
                        var url = project.WebsiteUrl;
                        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                            uri.Scheme == Uri.UriSchemeHttps &&
                            (uri.Host.Equals("curseforge.com", StringComparison.OrdinalIgnoreCase) ||
                             uri.Host.EndsWith(".curseforge.com", StringComparison.OrdinalIgnoreCase)))
                        {
                            try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
                            catch (Exception ex) { MessageBox.Show("Не удалось открыть CurseForge: " + ex.Message); }
                        }
                    };

                    card.Controls.AddRange(new Control[] { iconBox, summary, metrics, install, openSite });
                    results.Controls.Add(card);
                    if (!string.IsNullOrWhiteSpace(project.ThumbnailUrl))
                        iconTasks.Add(LoadCurseForgeProjectIconAsync(iconBox, project.ThumbnailUrl, iconGate));
                }

                offset += page.Projects.Count;
                await Task.WhenAll(iconTasks);
                status.Text = $"CurseForge: показано {Math.Min(offset, totalCount):N0} из {totalCount:N0}. Активная сборка: {ActiveBuild.Name}.";
            }
            catch (Exception ex)
            {
                if (ex is UnauthorizedAccessException)
                {
                    apiKey = "";
                    connected = false;
                    status.Text = "CurseForge не принял ключ. Проверь его; значение очищено из памяти страницы.";
                }
                else
                {
                    status.Text = "Каталог CurseForge недоступен: " + ex.Message;
                }
                Fail("Ошибка каталога CurseForge", ex);
            }
            finally
            {
                loading = false;
            }
        }

        async Task ConnectAndSearch()
        {
            var candidate = keyInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(candidate))
            {
                status.Text = "Введи свой одобренный API-ключ CurseForge.";
                return;
            }

            apiKey = candidate;
            connected = true;
            query = search.Text.Trim();
            offset = 0;
            await LoadPage(true);
            if (!string.IsNullOrWhiteSpace(apiKey))
                status.Text = "Ключ принят для текущего запуска. Он не записывается в settings.json.";
        }

        connect.Click += async (_, _) => await ConnectAndSearch();
        getKey.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo("https://console.curseforge.com/") { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show("Не удалось открыть консоль CurseForge: " + ex.Message); }
        };
        find.Click += async (_, _) =>
        {
            query = search.Text.Trim();
            offset = 0;
            await LoadPage(true);
        };
        search.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            query = search.Text.Trim();
            offset = 0;
            await LoadPage(true);
        };
        sort.SelectedIndexChanged += async (_, _) =>
        {
            if (!connected) return;
            offset = 0;
            await LoadPage(true);
        };

        results.Scroll += async (_, e) =>
        {
            if (e.ScrollOrientation != ScrollOrientation.VerticalScroll || loading || offset >= totalCount)
                return;
            var nearBottom = results.VerticalScroll.Value >=
                Math.Max(0, results.VerticalScroll.Maximum - results.ClientSize.Height - 220);
            if (nearBottom)
                await LoadPage(false);
        };

        p.Enter += (_, _) =>
        {
            var build = ActiveBuild;
            activeBuildCaption.Text = $"Сборка: {build.MinecraftVersion} · {build.Loader.ToUpperInvariant()}";
            if (!connected)
                status.Text = "Введи одобренный API-ключ CurseForge. Он используется только в памяти и не сохраняется.";
        };

        p.Controls.Add(results);
        p.Controls.Add(toolbar);
        return p;
    }

    TabPage CatalogPage(string title, string projectType)
    {
        var p = new TabPage(title) { BackColor = Color.FromArgb(12,14,23), Padding = new Padding(20) };
        var toolbar = new Panel { Dock = DockStyle.Top, Height = projectType == "datapack" ? 152 : 116, BackColor = Color.FromArgb(12,14,23) };

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
            AutoSize = false,
            Size = new Size(800, 22),
            AutoEllipsis = true,
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
            worldButton = new Button { Text = "Папка мира…", Width = 125, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(38,43,63), ForeColor = Color.White };
            worldButton.FlatAppearance.BorderSize = 0;
            worldCaption = new Label { Text = "Мир не выбран", AutoSize = false, ForeColor = Color.FromArgb(145,155,178) };
            selectedDatapackWorldLabel = worldCaption;
            worldButton.Click += (_, _) =>
            {
                var savesRoot = Path.Combine(settings.GameDirectory, "saves");
                Directory.CreateDirectory(savesRoot);
                using var dialog = new FolderBrowserDialog
                {
                    Description = "Выбери мир внутри папки saves активной сборки (там должен находиться level.dat)",
                    SelectedPath = selectedDatapackWorldDirectory ?? savesRoot
                };
                if (dialog.ShowDialog() != DialogResult.OK) return;
                if (!IsDirectoryWithin(savesRoot, dialog.SelectedPath))
                {
                    MessageBox.Show("Этот мир находится вне активной сборки. Выбери папку внутри её saves.", "Другая сборка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (!File.Exists(Path.Combine(dialog.SelectedPath, "level.dat")))
                {
                    MessageBox.Show("В выбранной папке нет level.dat.", "Не папка мира", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                selectedDatapackWorldDirectory = Path.GetFullPath(dialog.SelectedPath);
                worldCaption!.Text = "Мир: " + Path.GetFileName(dialog.SelectedPath);
            };
        }

        toolbar.Controls.AddRange(new Control[] { heading, description, search, find, sort });
        if (worldButton != null) toolbar.Controls.Add(worldButton);
        if (worldCaption != null) toolbar.Controls.Add(worldCaption);

        void LayoutCatalogToolbar()
        {
            var availableWidth = Math.Max(560, toolbar.ClientSize.Width - 8);
            const int findWidth = 84;
            const int sortWidth = 145;
            const int gap = 8;
            var searchWidth = Math.Clamp(availableWidth - findWidth - sortWidth - gap * 2, 180, 420);
            description.Size = new Size(Math.Max(220, availableWidth - 8), 22);
            search.SetBounds(4, 72, searchWidth, 36);
            find.SetBounds(search.Right + gap, 72, findWidth, 36);
            sort.SetBounds(find.Right + gap, 72, sortWidth, 36);

            if (worldButton != null && worldCaption != null)
            {
                worldButton.SetBounds(4, 114, 125, 32);
                worldCaption.SetBounds(140, 118, Math.Max(160, availableWidth - 148), 24);
            }
        }

        toolbar.Resize += (_, _) => LayoutCatalogToolbar();
        LayoutCatalogToolbar();

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
                var projectFacet = projectType == "datapack" ? "all_project_types:datapack" : "project_type:" + projectType;
                var facets = Uri.EscapeDataString(JsonSerializer.Serialize(new[] { new[] { projectFacet } }));
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
                    iconTasks.Add(LoadProjectIconAsync(picture, SelectProjectArtworkUrl(hit), gate));
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
        var p = new TabPage("Сборки")
        {
            BackColor = Color.FromArgb(13,15,25),
            Padding = new Padding(20)
        };

        var toolbar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 142,
            BackColor = Color.FromArgb(13,15,25)
        };
        toolbar.Controls.Add(new Label
        {
            Text = "Мои сборки",
            AutoSize = true,
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 25, FontStyle.Bold),
            Location = new Point(4, 2)
        });
        toolbar.Controls.Add(new Label
        {
            Text = "Каждая сборка хранит собственные моды, настройки, миры и файлы Minecraft.",
            AutoSize = true,
            ForeColor = Color.FromArgb(151,161,183),
            Location = new Point(7, 42)
        });

        var name = new TextBox
        {
            PlaceholderText = "Название новой сборки",
            Location = new Point(4, 82),
            Width = 260,
            Height = 36
        };
        var create = ModernButton("+  Создать пустую", 160, 36);
        create.Location = new Point(274, 82);
        var import = ModernButton("Импорт Modrinth / CurseForge", 245, 36);
        import.Location = new Point(444, 82);
        var count = new Label
        {
            AutoSize = true,
            Location = new Point(705, 91),
            ForeColor = Color.FromArgb(155,165,190),
            Text = settings.BuildProfiles.Count + " сборок"
        };
        toolbar.Controls.AddRange(new Control[] { name, create, import, count });

        var list = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(2, 10, 14, 24),
            BackColor = Color.FromArgb(13,15,25)
        };

        var progressPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 62,
            BackColor = Color.FromArgb(22,25,39),
            Padding = new Padding(12)
        };
        var progressLabel = new Label
        {
            Text = "Загрузки и установка появятся здесь.",
            AutoSize = false,
            Location = new Point(12, 8),
            Size = new Size(850, 20),
            ForeColor = Color.FromArgb(195,204,220)
        };
        var progressBar = new ProgressBar
        {
            Location = new Point(12, 33),
            Size = new Size(850, 14),
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Style = ProgressBarStyle.Continuous
        };
        progressPanel.Controls.AddRange(new Control[] { progressLabel, progressBar });

        async Task DuplicateProfileAsync(BuildProfile sourceProfile)
        {
            if (isBusy || isImportingPack)
            {
                status.Text = "Сначала заверши текущую операцию.";
                return;
            }

            using var dialog = new Form
            {
                Text = "Копировать сборку",
                Width = 520,
                Height = 410,
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                BackColor = Color.FromArgb(20,23,35),
                ForeColor = Color.White
            };

            var heading = new Label
            {
                Text = "Создать независимую копию",
                Location = new Point(20, 16),
                Size = new Size(460, 28),
                ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 15, FontStyle.Bold)
            };
            var hint = new Label
            {
                Text = "Оригинальная сборка не изменяется. Версии Minecraft, библиотеки и кэш не дублируются — они установятся в новую папку при первом запуске.",
                Location = new Point(22, 52),
                Size = new Size(455, 52),
                ForeColor = Color.FromArgb(160,174,199)
            };
            var nameLabel = new Label
            {
                Text = "Название копии",
                Location = new Point(22, 112),
                AutoSize = true,
                ForeColor = Color.FromArgb(208,216,231)
            };
            var nameInput = new TextBox
            {
                Text = sourceProfile.Name + " — копия",
                Location = new Point(22, 136),
                Width = 455,
                Height = 32
            };

            CheckBox MakeOption(string text, int x, int y, bool selected)
                => new()
                {
                    Text = text,
                    Location = new Point(x, y),
                    AutoSize = true,
                    Checked = selected,
                    ForeColor = Color.FromArgb(221,226,240),
                    BackColor = Color.Transparent,
                    Font = new Font("Segoe UI", 9.5f)
                };

            var modsOption = MakeOption("Моды (mods)", 24, 184, true);
            var configOption = MakeOption("Конфигурации (config)", 252, 184, true);
            var worldsOption = MakeOption("Миры (saves)", 24, 214, false);
            var gameSettingsOption = MakeOption("Игровые настройки и серверы", 252, 214, true);
            var resourcePacksOption = MakeOption("Пакеты ресурсов", 24, 244, true);
            var shaderPacksOption = MakeOption("Шейдеры", 252, 244, true);
            var screenshotsOption = MakeOption("Скриншоты", 24, 274, false);

            var createCopy = ModernButton("Создать копию", 140, 36);
            createCopy.Location = new Point(332, 326);
            var cancelCopy = new Button
            {
                Text = "Отмена",
                Location = new Point(222, 326),
                Width = 100,
                Height = 36,
                DialogResult = DialogResult.Cancel,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(38,42,57),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            cancelCopy.FlatAppearance.BorderSize = 0;

            createCopy.Click += (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(nameInput.Text))
                {
                    MessageBox.Show(dialog, "Укажи название новой сборки.", "NovaCraft", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (!modsOption.Checked && !configOption.Checked && !worldsOption.Checked &&
                    !gameSettingsOption.Checked && !resourcePacksOption.Checked &&
                    !shaderPacksOption.Checked && !screenshotsOption.Checked)
                {
                    MessageBox.Show(dialog, "Выбери хотя бы одну категорию данных.", "NovaCraft", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                dialog.DialogResult = DialogResult.OK;
                dialog.Close();
            };

            dialog.Controls.AddRange(new Control[]
            {
                heading, hint, nameLabel, nameInput,
                modsOption, configOption, worldsOption, gameSettingsOption,
                resourcePacksOption, shaderPacksOption, screenshotsOption,
                cancelCopy, createCopy
            });
            dialog.AcceptButton = createCopy;
            dialog.CancelButton = cancelCopy;
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            var id = Guid.NewGuid().ToString("N");
            var newProfile = new BuildProfile
            {
                Id = id,
                Name = nameInput.Text.Trim(),
                MinecraftVersion = sourceProfile.MinecraftVersion,
                Loader = BuildProfileRules.NormalizeLoader(sourceProfile.Loader),
                LoaderVersion = sourceProfile.LoaderVersion,
                GameDirectory = BuildProfileRules.CreateInstanceDirectory(settings.GameDirectory, id),
                SourcePlatform = "local",
                SourceProjectId = "",
                InstallState = "Данные скопированы"
            };
            var options = new BuildCopyOptions
            {
                IncludeMods = modsOption.Checked,
                IncludeConfig = configOption.Checked,
                IncludeWorlds = worldsOption.Checked,
                IncludeGameSettings = gameSettingsOption.Checked,
                IncludeResourcePacks = resourcePacksOption.Checked,
                IncludeShaderPacks = shaderPacksOption.Checked,
                IncludeScreenshots = screenshotsOption.Checked
            };

            try
            {
                SetBusy(true, "Копирую выбранные данные в отдельную папку…");
                progressBar.Value = 0;
                var progress = new Progress<BuildCopyProgress>(value =>
                {
                    progressLabel.Text = "Копирование: " + value.CurrentItem +
                        "  ·  " + value.CompletedFiles + " / " + value.TotalFiles;
                    progressBar.Value = value.TotalFiles <= 0
                        ? 0
                        : Math.Clamp((int)Math.Round(value.CompletedFiles * 100d / value.TotalFiles), 0, 100);
                });

                var result = await Task.Run(() => BuildCopyService.CopySelectedContent(
                    sourceProfile.GameDirectory,
                    newProfile.GameDirectory,
                    options,
                    progress));

                settings.BuildProfiles.Add(newProfile);
                try
                {
                    SettingsService.Save(settings);
                }
                catch
                {
                    // The copied directory is intentionally preserved if settings cannot be saved.
                    throw new IOException("Файлы копии сохранены в " + newProfile.GameDirectory +
                        ", но NovaCraft не смог записать новую сборку в настройки.");
                }

                RefreshBuildSelector();
                RenderProfiles();
                progressBar.Value = 100;
                progressLabel.Text = "Копия создана: " + result.FilesCopied + " файлов · " +
                    (result.BytesCopied / 1024d / 1024d).ToString("0.0") + " МБ";
                status.Text = "Создана независимая копия «" + newProfile.Name +
                    "». Исходная сборка не менялась; при первом запуске установятся игровые файлы.";
            }
            catch (Exception ex)
            {
                Fail("Не удалось скопировать сборку", ex);
            }
            finally
            {
                SetBusy(false);
            }
        }

        async Task BackupWorldsAsync(BuildProfile sourceProfile)
        {
            if (isBusy || isImportingPack)
            {
                status.Text = "Сначала заверши текущую операцию.";
                return;
            }

            try
            {
                SetBusy(true, "Создаю резервную копию миров…");
                progressBar.Value = 0;
                var progress = new Progress<BuildCopyProgress>(value =>
                {
                    progressLabel.Text = "Архивация мира: " + value.CurrentItem +
                        "  ·  " + value.CompletedFiles + " / " + value.TotalFiles;
                    progressBar.Value = value.TotalFiles <= 0
                        ? 0
                        : Math.Clamp((int)Math.Round(value.CompletedFiles * 100d / value.TotalFiles), 0, 100);
                });

                var backup = await Task.Run(() => BuildCopyService.CreateWorldBackup(sourceProfile.GameDirectory, progress));
                progressBar.Value = 100;
                progressLabel.Text = "Резервная копия готова: " + Path.GetFileName(backup.ArchivePath);
                status.Text = "Миры сохранены в " + backup.ArchivePath +
                    "  ·  " + backup.FilesCopied + " файлов";
                MessageBox.Show(
                    this,
                    "Резервная копия миров создана.\n\n" + backup.ArchivePath +
                    "\n\nИсходные миры остались на месте.",
                    "NovaCraft — резервная копия",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Fail("Не удалось создать резервную копию миров", ex);
            }
            finally
            {
                SetBusy(false);
            }
        }

        void RenderProfiles()
        {
            list.SuspendLayout();
            list.Controls.Clear();
            count.Text = settings.BuildProfiles.Count + " сборок";

            if (settings.BuildProfiles.Count == 0)
            {
                list.Controls.Add(new Label
                {
                    Text = "Сборок пока нет. Создай пустую или импортируй пакет Modrinth / CurseForge.",
                    AutoSize = true,
                    ForeColor = Color.Silver,
                    Padding = new Padding(16)
                });
                list.ResumeLayout();
                return;
            }

            foreach (var profile in settings.BuildProfiles.ToList())
            {
                var selected = string.Equals(profile.Id, settings.ActiveBuildId, StringComparison.OrdinalIgnoreCase);
                var card = new Panel
                {
                    Width = 390,
                    Height = 204,
                    Margin = new Padding(5, 5, 12, 10),
                    Padding = new Padding(16),
                    BackColor = selected ? Color.FromArgb(37,34,65) : Color.FromArgb(24,27,42),
                    BorderStyle = BorderStyle.None
                };
                RoundCorners(card, 16);
                AppTheme.StyleCard(card, selected);

                card.Controls.Add(new Label
                {
                    Text = profile.Name,
                    Location = new Point(14, 12),
                    Size = new Size(345, 26),
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI Semibold", 12, FontStyle.Bold),
                    AutoEllipsis = true
                });

                var sourceLabel = profile.SourcePlatform switch
                {
                    "modrinth" => "MODRINTH",
                    "curseforge" => "CURSEFORGE",
                    "legacy" => "ПЕРЕНЕСЁННАЯ",
                    _ => "ЛОКАЛЬНАЯ"
                };
                card.Controls.Add(new Label
                {
                    Text = sourceLabel,
                    Location = new Point(16, 43),
                    AutoSize = true,
                    ForeColor = Color.FromArgb(110,220,170),
                    Font = new Font("Segoe UI Semibold", 8, FontStyle.Bold)
                });
                card.Controls.Add(new Label
                {
                    Text = "Minecraft " + profile.MinecraftVersion + "   ·   " + profile.Loader.ToUpperInvariant() +
                           (string.IsNullOrWhiteSpace(profile.LoaderVersion) ? "" : " " + profile.LoaderVersion),
                    Location = new Point(16, 65),
                    Size = new Size(345, 22),
                    ForeColor = Color.FromArgb(174,184,205),
                    AutoEllipsis = true
                });
                card.Controls.Add(new Label
                {
                    Text = profile.InstallState + "  ·  " + (Directory.Exists(profile.GameDirectory) ? "Папка создана" : "Папка ещё не создана"),
                    Location = new Point(16, 88),
                    AutoSize = true,
                    ForeColor = profile.InstallState == "Импортирована" ? Color.FromArgb(90,220,150) : Color.FromArgb(142,153,177)
                });
                if (selected)
                {
                    card.Controls.Add(new Label
                    {
                        Text = "АКТИВНАЯ",
                        Location = new Point(290, 12),
                        AutoSize = true,
                        ForeColor = Color.FromArgb(170,150,255),
                        Font = new Font("Segoe UI Semibold", 8, FontStyle.Bold)
                    });
                }

                var select = ModernButton(selected ? "Выбрана" : "Выбрать", 100, 32);
                select.Location = new Point(14, 119);
                select.Enabled = !selected;
                select.Click += (_,_) =>
                {
                    ActivateBuild(profile);
                    RenderProfiles();
                };

                var launch = ModernButton("▶  Играть", 104, 32);
                launch.Location = new Point(122, 119);
                launch.Click += async (_,_) =>
                {
                    if (isBusy) return;
                    ActivateBuild(profile);
                    ShowNavigationPage(0);
                    await LaunchAsync();
                };

                var folder = new Button
                {
                    Text = "Папка",
                    Width = 68,
                    Height = 32,
                    Location = new Point(234, 119),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(38,42,57),
                    ForeColor = Color.White,
                    Cursor = Cursors.Hand
                };
                folder.FlatAppearance.BorderSize = 0;
                folder.Click += (_,_) =>
                {
                    try
                    {
                        Directory.CreateDirectory(profile.GameDirectory);
                        var explorer = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
                        explorer.ArgumentList.Add(profile.GameDirectory);
                        Process.Start(explorer);
                    }
                    catch (Exception ex) { Fail("Не удалось открыть папку сборки", ex); }
                };

                var remove = new Button
                {
                    Text = "×",
                    Width = 32,
                    Height = 32,
                    Location = new Point(312, 119),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(58,36,49),
                    ForeColor = Color.White,
                    Cursor = Cursors.Hand
                };
                remove.FlatAppearance.BorderSize = 0;
                remove.Enabled = !string.Equals(profile.InstallState, "Импортируется", StringComparison.OrdinalIgnoreCase);
                remove.Click += (_,_) =>
                {
                    if (isBusy || string.Equals(profile.InstallState, "Импортируется", StringComparison.OrdinalIgnoreCase))
                    {
                        status.Text = "Нельзя менять сборку во время операции.";
                        return;
                    }
                    if (settings.BuildProfiles.Count <= 1)
                    {
                        MessageBox.Show("Нельзя удалить последнюю сборку.", "NovaCraft", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    var answer = MessageBox.Show(
                        "Убрать сборку «" + profile.Name + "» из списка?\n\nФайлы и миры на диске останутся нетронутыми.",
                        "Удаление записи сборки",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);
                    if (answer != DialogResult.Yes) return;

                    var wasActive = settings.ActiveBuildId == profile.Id;
                    settings.BuildProfiles.Remove(profile);
                    if (wasActive)
                        ActivateBuild(settings.BuildProfiles[0]);
                    else
                    {
                        SettingsService.Save(settings);
                        RefreshBuildSelector();
                    }
                    RenderProfiles();
                };

                var duplicate = ModernButton("Копировать", 145, 32);
                duplicate.Location = new Point(14, 158);
                duplicate.Click += async (_, _) => await DuplicateProfileAsync(profile);

                var backup = new Button
                {
                    Text = "Резервная копия миров",
                    Width = 188,
                    Height = 32,
                    Location = new Point(168, 158),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(38,42,57),
                    ForeColor = Color.White,
                    Cursor = Cursors.Hand
                };
                backup.FlatAppearance.BorderSize = 0;
                backup.Click += async (_, _) => await BackupWorldsAsync(profile);

                card.Controls.AddRange(new Control[] { select, launch, folder, remove, duplicate, backup });
                list.Controls.Add(card);
            }

            list.ResumeLayout();
        }

        create.Click += (_,_) =>
        {
            if (isBusy) return;
            try
            {
                var id = Guid.NewGuid().ToString("N");
                var currentVersion = version.SelectedItem?.ToString() ?? settings.LastVersion;
                var currentLoader = gameLoader.SelectedItem?.ToString() switch
                {
                    "Forge" => "forge",
                    "Quilt" => "quilt",
                    "Vanilla" => "vanilla",
                    _ => "fabric"
                };
                var profile = new BuildProfile
                {
                    Id = id,
                    Name = string.IsNullOrWhiteSpace(name.Text) ? "Новая сборка " + (settings.BuildProfiles.Count + 1) : name.Text.Trim(),
                    MinecraftVersion = currentVersion,
                    Loader = currentLoader,
                    GameDirectory = BuildProfileRules.CreateInstanceDirectory(settings.GameDirectory, id),
                    SourcePlatform = "local",
                    InstallState = "Пустая"
                };
                settings.BuildProfiles.Add(profile);
                Directory.CreateDirectory(profile.GameDirectory);
                SettingsService.Save(settings);
                name.Clear();
                ActivateBuild(profile);
                RenderProfiles();
                status.Text = "Создана отдельная сборка: " + profile.Name;
            }
            catch (Exception ex) { Fail("Не удалось создать сборку", ex); }
        };

        import.Click += async (_,_) =>
        {
            if (isBusy || isImportingPack)
            {
                status.Text = "Заверши текущую операцию перед новым импортом.";
                return;
            }
            using var dialog = new OpenFileDialog
            {
                Title = "Импортировать Modrinth или CurseForge",
                Filter = "Minecraft modpacks (*.mrpack;*.zip)|*.mrpack;*.zip",
                CheckFileExists = true
            };
            if (dialog.ShowDialog() != DialogResult.OK) return;

            isImportingPack = true;
            var activeBuildIdBeforeImport = settings.ActiveBuildId;
            BuildProfile? profile = null;
            LauncherDownloadTask? importTask = null;
            try
            {
                var descriptor = MinecraftPackManifestReader.Read(dialog.FileName);
                if (descriptor.Loader == "neoforge")
                    throw new NotSupportedException("В этом этапе импорт NeoForge ещё не поддерживается. NovaCraft не будет автоматически подменять NeoForge на Forge.");

                string? apiKey = null;
                if (descriptor.Platform == "curseforge")
                {
                    apiKey = PromptCurseForgeApiKey();
                    if (string.IsNullOrWhiteSpace(apiKey))
                    {
                        MessageBox.Show(
                            "Импорт CurseForge отменён: нужен API-ключ для загрузки файлов. Ключ не сохраняется в настройках.",
                            "Требуется ключ CurseForge",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                        return;
                    }
                }

                var id = Guid.NewGuid().ToString("N");
                profile = new BuildProfile
                {
                    Id = id,
                    Name = descriptor.Name,
                    MinecraftVersion = descriptor.MinecraftVersion,
                    Loader = descriptor.Loader,
                    LoaderVersion = descriptor.LoaderVersion,
                    GameDirectory = BuildProfileRules.CreateInstanceDirectory(settings.GameDirectory, id),
                    SourcePlatform = descriptor.Platform,
                    SourceProjectId = descriptor.ProjectId,
                    InstallState = "Импортируется"
                };
                settings.BuildProfiles.Add(profile);
                Directory.CreateDirectory(profile.GameDirectory);
                SettingsService.Save(settings);
                RefreshBuildSelector();
                RenderProfiles();
                importTask = BeginDownloadTask(profile.Name, Path.GetFileName(dialog.FileName), descriptor.Platform, profile.Name);

                progressBar.Value = 0;
                progressLabel.Text = "Подготовка импорта " + profile.Name;

                var progress = new Progress<PackImportProgress>(value =>
                {
                    progressLabel.Text = value.Stage + (string.IsNullOrWhiteSpace(value.CurrentFile) ? "" : " · " + value.CurrentFile);
                    progressBar.Value = Math.Clamp((int)(value.FileProgress * 100), 0, 100);
                    status.Text = "Импорт: " + Math.Min(value.CompletedFiles, value.TotalFiles) + " / " + value.TotalFiles;
                    var progressValue = value.TotalFiles <= 0
                        ? 0
                        : Math.Clamp((value.CompletedFiles + value.FileProgress) / value.TotalFiles, 0, 1);
                    var taskStatus = value.Stage.Contains("провер", StringComparison.OrdinalIgnoreCase)
                        ? "Проверка"
                        : "Устанавливается";
                    UpdateDownloadTask(importTask, taskStatus, progressValue, value.Stage + (string.IsNullOrWhiteSpace(value.CurrentFile) ? "" : " · " + value.CurrentFile));
                });

                await MinecraftPackImportService.ImportAsync(
                    dialog.FileName,
                    profile,
                    http,
                    apiKey,
                    progress,
                    CancellationToken.None);

                profile.InstallState = "Импортирована";
                SettingsService.Save(settings);
                CompleteDownloadTask(importTask, "Все обязательные файлы и overrides установлены в отдельную папку");

                var canActivateImportedBuild =
                    !isBusy &&
                    string.Equals(settings.ActiveBuildId, activeBuildIdBeforeImport, StringComparison.OrdinalIgnoreCase);
                if (canActivateImportedBuild)
                    ActivateBuild(profile);

                RefreshBuildSelector();
                RenderProfiles();
                progressLabel.Text = "Импорт завершён: " + profile.Name;
                progressBar.Value = 100;
                status.Text = canActivateImportedBuild
                    ? "Сборка импортирована и выбрана: " + profile.Name
                    : "Сборка импортирована; текущая активная сборка оставлена без изменений.";
            }
            catch (Exception ex)
            {
                if (importTask != null)
                    FailDownloadTask(importTask, ex);
                if (profile != null)
                {
                    profile.InstallState = "Требует исправления";
                    SettingsService.Save(settings);
                    RenderProfiles();
                }
                progressLabel.Text = "Импорт не завершён: " + ex.Message;
                progressBar.Value = 0;
                Fail("Не удалось импортировать сборку", ex);
            }
            finally
            {
                isImportingPack = false;
            }
        };

        p.Controls.Add(list);
        p.Controls.Add(progressPanel);
        p.Controls.Add(toolbar);
        RenderProfiles();
        return p;
    }

    string? PromptCurseForgeApiKey()
    {
        using var dialog = new Form
        {
            Text = "API-ключ CurseForge",
            Width = 520,
            Height = 190,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = Color.FromArgb(20,23,35),
            ForeColor = Color.White
        };
        var label = new Label
        {
            Text = "Введите свой API-ключ CurseForge. NovaCraft использует его только во время импорта и не сохраняет в settings.json.",
            Location = new Point(16, 14),
            Size = new Size(470, 48),
            ForeColor = Color.FromArgb(205,212,226)
        };
        var input = new TextBox
        {
            Location = new Point(16, 70),
            Width = 470,
            UseSystemPasswordChar = true
        };
        var ok = ModernButton("Продолжить", 120, 34);
        ok.Location = new Point(240, 112);
        ok.DialogResult = DialogResult.OK;
        var cancel = new Button
        {
            Text = "Отмена",
            Location = new Point(368, 112),
            Width = 118,
            Height = 34,
            DialogResult = DialogResult.Cancel,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(38,42,57),
            ForeColor = Color.White
        };
        cancel.FlatAppearance.BorderSize = 0;
        dialog.Controls.AddRange(new Control[] { label, input, ok, cancel });
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;
        return dialog.ShowDialog(this) == DialogResult.OK ? input.Text.Trim() : null;
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
        var p = new TabPage("Музыка")
        {
            BackColor = Color.FromArgb(18, 19, 23),
            Padding = new Padding(24),
            AutoScroll = true
        };
        p.Controls.Add(new Label
        {
            Text = "МУЗЫКА И АУДИО",
            Font = new Font("Segoe UI Semibold", 25, FontStyle.Bold),
            AutoSize = true,
            ForeColor = Color.White,
            Location = new Point(24, 18)
        });
        p.Controls.Add(new Label
        {
            Text = "Локальный проигрыватель MP3/WAV и генератор OGG-ресурс-пака для Minecraft.",
            AutoSize = true,
            ForeColor = Color.FromArgb(151, 161, 183),
            Location = new Point(27, 62)
        });

        var playerCard = ModernCard(820, 390, "ПРОИГРЫВАТЕЛЬ NOVACRAFT", Color.FromArgb(22, 25, 39));
        playerCard.Location = new Point(24, 100);
        var trackList = new ListBox
        {
            Location = new Point(18, 50),
            Size = new Size(490, 220),
            BackColor = Color.FromArgb(17, 20, 31),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.None,
            IntegralHeight = false
        };
        var playerState = new Label
        {
            Text = "Выбери MP3 или WAV-файлы. Плейлист сохраняется на этом компьютере.",
            Location = new Point(18, 328),
            Size = new Size(770, 36),
            ForeColor = Color.FromArgb(160, 174, 197),
            AutoEllipsis = true
        };
        var nowPlaying = new Label
        {
            Text = "Сейчас ничего не воспроизводится",
            Location = new Point(18, 282),
            Size = new Size(490, 30),
            ForeColor = Color.White,
            AutoEllipsis = true
        };
        var volumeLabel = new Label
        {
            Text = "Громкость",
            Location = new Point(535, 286),
            AutoSize = true,
            ForeColor = Color.Silver
        };
        var volume = new TrackBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 70,
            TickFrequency = 10,
            Location = new Point(615, 270),
            Size = new Size(170, 45)
        };

        var addTracks = ModernButton("+ Добавить файлы", 145, 36);
        addTracks.Location = new Point(535, 50);
        var removeTrack = ModernButton("Удалить из списка", 145, 36);
        removeTrack.Location = new Point(535, 94);
        removeTrack.BackColor = Color.FromArgb(42, 46, 62);
        var previousTrack = ModernButton("◀ Предыдущая", 145, 36);
        previousTrack.Location = new Point(535, 138);
        previousTrack.BackColor = Color.FromArgb(42, 46, 62);
        var playPause = ModernButton("▶  Играть / пауза", 145, 36);
        playPause.Location = new Point(535, 182);
        var stopTrack = ModernButton("■  Стоп", 145, 36);
        stopTrack.Location = new Point(535, 226);
        stopTrack.BackColor = Color.FromArgb(42, 46, 62);

        settings.MusicPlaylist = MusicPlaylistRules.Normalize(settings.MusicPlaylist);

        void SavePlaylist()
        {
            settings.MusicPlaylist = trackList.Items.Cast<MusicTrackEntry>()
                .Select(x => x.FullPath).ToList();
            SettingsService.Save(settings);
        }

        void RefreshPlaylist(int selectedIndex = -1)
        {
            trackList.BeginUpdate();
            trackList.Items.Clear();
            foreach (var path in settings.MusicPlaylist)
                trackList.Items.Add(new MusicTrackEntry(path));
            if (trackList.Items.Count > 0)
                trackList.SelectedIndex = Math.Clamp(selectedIndex < 0 ? 0 : selectedIndex, 0, trackList.Items.Count - 1);
            trackList.EndUpdate();
            playerState.Text = $"{trackList.Items.Count} трек(ов) в сохранённом плейлисте. Файлы остаются в исходных папках.";
        }

        void StopPlayback()
        {
            if (localMusicPlayer?.IsOpen == true)
            {
                try { localMusicPlayer.Stop(); } catch { }
            }
            if (localMusicTimer != null)
                localMusicTimer.Stop();
        }

        void PlayAt(int index)
        {
            if (index < 0 || index >= trackList.Items.Count)
                return;

            var entry = (MusicTrackEntry)trackList.Items[index];
            try
            {
                localMusicPlayer ??= new LocalMusicPlayer();
                localMusicPlayer.Open(entry.FullPath);
                try { localMusicPlayer.SetVolume(volume.Value); } catch { /* Some Windows audio drivers do not expose MCI volume control. */ }
                localMusicPlayer.Play();
                trackList.SelectedIndex = index;
                nowPlaying.Text = "Сейчас играет: " + entry.ToString();
                playerState.Text = "Воспроизведение. Следующий трек включится автоматически.";
                if (localMusicTimer == null)
                {
                    localMusicTimer = new System.Windows.Forms.Timer { Interval = 900 };
                    localMusicTimer.Tick += (_, _) =>
                    {
                        if (localMusicPlayer?.IsOpen != true || trackList.Items.Count == 0)
                            return;
                        try
                        {
                            if (string.Equals(localMusicPlayer.GetMode(), "stopped", StringComparison.OrdinalIgnoreCase))
                            {
                                var next = trackList.SelectedIndex + 1;
                                PlayAt(next >= trackList.Items.Count ? 0 : next);
                            }
                        }
                        catch (Exception ex)
                        {
                            localMusicTimer.Stop();
                            playerState.Text = "Проигрыватель остановлен: " + ex.Message;
                        }
                    };
                }
                localMusicTimer.Start();
            }
            catch (Exception ex)
            {
                if (localMusicTimer != null)
                    localMusicTimer.Stop();
                nowPlaying.Text = "Не удалось воспроизвести: " + entry.ToString();
                playerState.Text = ex.Message;
            }
        }

        addTracks.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog
            {
                Filter = "Музыка MP3/WAV (*.mp3;*.wav)|*.mp3;*.wav",
                Title = "Добавить музыку в плейлист",
                Multiselect = true
            };
            if (dialog.ShowDialog() != DialogResult.OK)
                return;

            var playlist = settings.MusicPlaylist.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var path in dialog.FileNames)
            {
                try { playlist.Add(MusicPlaylistRules.ValidateExistingFile(path)); }
                catch (Exception ex) { playerState.Text = ex.Message; }
            }
            settings.MusicPlaylist = playlist.ToList();
            RefreshPlaylist(trackList.Items.Count);
            SavePlaylist();
        };

        removeTrack.Click += (_, _) =>
        {
            var index = trackList.SelectedIndex;
            if (index < 0)
                return;
            var removed = (MusicTrackEntry)trackList.Items[index];
            if (string.Equals(localMusicPlayer?.CurrentPath, removed.FullPath, StringComparison.OrdinalIgnoreCase))
            {
                StopPlayback();
                localMusicPlayer?.Dispose();
                localMusicPlayer = null;
                nowPlaying.Text = "Сейчас ничего не воспроизводится";
            }
            settings.MusicPlaylist.RemoveAll(x => string.Equals(x, removed.FullPath, StringComparison.OrdinalIgnoreCase));
            RefreshPlaylist(Math.Max(0, index - 1));
            SavePlaylist();
        };

        previousTrack.Click += (_, _) =>
        {
            if (trackList.Items.Count == 0) return;
            var index = trackList.SelectedIndex <= 0 ? trackList.Items.Count - 1 : trackList.SelectedIndex - 1;
            PlayAt(index);
        };
        playPause.Click += (_, _) =>
        {
            if (localMusicPlayer?.IsOpen != true)
            {
                PlayAt(trackList.SelectedIndex >= 0 ? trackList.SelectedIndex : 0);
                return;
            }
            try
            {
                var mode = localMusicPlayer.GetMode();
                if (string.Equals(mode, "paused", StringComparison.OrdinalIgnoreCase))
                {
                    localMusicPlayer.Resume();
                    playerState.Text = "Воспроизведение продолжено.";
                }
                else if (string.Equals(mode, "playing", StringComparison.OrdinalIgnoreCase))
                {
                    localMusicPlayer.Pause();
                    playerState.Text = "Пауза.";
                }
                else
                {
                    PlayAt(trackList.SelectedIndex >= 0 ? trackList.SelectedIndex : 0);
                }
            }
            catch (Exception ex) { playerState.Text = ex.Message; }
        };
        stopTrack.Click += (_, _) =>
        {
            StopPlayback();
            nowPlaying.Text = "Воспроизведение остановлено";
            playerState.Text = "Нажми «Играть», чтобы продолжить с выбранного трека.";
        };
        volume.Scroll += (_, _) =>
        {
            if (localMusicPlayer?.IsOpen != true) return;
            try { localMusicPlayer.SetVolume(volume.Value); }
            catch (Exception ex) { playerState.Text = "Громкость не поддерживается аудиоустройством: " + ex.Message; }
        };
        trackList.DoubleClick += (_, _) => PlayAt(trackList.SelectedIndex);

        RefreshPlaylist();
        playerCard.Controls.AddRange(new Control[]
        {
            trackList, playerState, nowPlaying, volumeLabel, volume,
            addTracks, removeTrack, previousTrack, playPause, stopTrack
        });

        var packCard = ModernCard(820, 220, "СВОЯ МУЗЫКА В MINECRAFT", Color.FromArgb(22, 25, 39));
        packCard.Location = new Point(24, 510);
        packCard.Controls.Add(new Label
        {
            Text = "Выбери OGG-файл. NovaCraft соберёт ресурс-пак для Minecraft 1.21.1. Исходные файлы игры не перезаписываются.",
            AutoSize = false,
            Size = new Size(760, 42),
            ForeColor = Color.FromArgb(181, 190, 207),
            Location = new Point(18, 48)
        });
        customMusicPath = new TextBox { Location = new Point(18, 100), Width = 520, Height = 34, ReadOnly = true };
        var browse = ModernButton("Выбрать OGG…", 130, 36);
        browse.Location = new Point(550, 98);
        var installPack = ModernButton("Создать ресурс-пак", 190, 38);
        installPack.Location = new Point(18, 148);
        var packResult = new Label
        {
            Text = "Ресурс-пак включается внутри Minecraft в настройках ресурсов.",
            Location = new Point(220, 150),
            Size = new Size(570, 46),
            ForeColor = Color.FromArgb(160, 174, 197),
            AutoEllipsis = true
        };
        browse.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog { Filter = "OGG audio (*.ogg)|*.ogg", Title = "Выбери музыкальный файл" };
            if (dialog.ShowDialog() == DialogResult.OK) customMusicPath.Text = dialog.FileName;
        };
        installPack.Click += (_, _) =>
        {
            string? temp = null;
            try
            {
                var source = customMusicPath.Text;
                if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
                    throw new InvalidOperationException("Сначала выбери существующий файл .ogg.");
                var selectedGameVersion = version.SelectedItem?.ToString() ?? settings.LastVersion;
                if (selectedGameVersion != "1.21.1")
                    throw new InvalidOperationException("Генератор формирует ресурс-пак для Minecraft 1.21.1 (pack_format 34). Выбери эту версию, иначе игра может отклонить ресурс-пак.");
                if (!string.Equals(Path.GetExtension(source), ".ogg", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Для этого ресурс-пака нужен файл OGG.");
                var targetDir = Path.Combine(ActiveBuild.GameDirectory, "resourcepacks");
                Directory.CreateDirectory(targetDir);
                var target = Path.Combine(targetDir, "NovaCraftCustomMusic.zip");
                temp = target + ".tmp";
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
                packResult.Text = "Создано: " + target + ". Включи NovaCraftCustomMusic в настройках ресурс-паков Minecraft.";
            }
            catch (Exception ex)
            {
                if (temp != null && File.Exists(temp))
                    try { File.Delete(temp); } catch { }
                packResult.Text = "Не удалось создать ресурс-пак: " + ex.Message;
            }
        };

        packCard.Controls.AddRange(new Control[] { customMusicPath, browse, installPack, packResult });
        p.Controls.AddRange(new Control[] { playerCard, packCard });
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
            RefreshProfileChip();
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

        void LayoutProfileCard()
        {
            var actionLeft = Math.Max(490, profileCard.ClientSize.Width - 138);
            var availableLeft = Math.Max(210, actionLeft - 30);
            var listWidth = Math.Clamp((int)(availableLeft * 0.56), 210, 330);
            profileList.SetBounds(18, 48, listWidth, 130);

            var detailLeft = profileList.Right + 16;
            var detailWidth = Math.Max(130, actionLeft - detailLeft - 12);
            profileName.SetBounds(detailLeft, 48, detailWidth, 36);
            uuidLabel.SetBounds(detailLeft, 94, detailWidth, 68);

            addProfile.SetBounds(actionLeft, 47, 120, 38);
            renameProfile.SetBounds(actionLeft, 91, 120, 38);
            deleteProfile.SetBounds(actionLeft, 135, 120, 38);
        }
        profileCard.Resize += (_, _) => LayoutProfileCard();
        LayoutProfileCard();

        p.Controls.Add(profileCard);

        var skinCard = ModernCard(860, 305, "ЛОКАЛЬНЫЙ СКИН", Color.FromArgb(22, 25, 39));
        skinCard.Location = new Point(24, 320);
        var preview = new Skin3DPreview { Location = new Point(18, 48), Size = new Size(220, 220) };
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
            preview.SetSkin(image);
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
                preview.Slim = profile.Slim;
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
            preview.Slim = model.SelectedIndex == 1;
            SettingsService.Save(settings);
        };

        bridge.Click += async (_, _) => await InstallCustomSkinLoaderAsync(skinStatus);
        openFolder.Click += (_, _) =>
        {
            try { Directory.CreateDirectory(Path.Combine(settings.GameDirectory, "CustomSkinLoader", "LocalSkin", "skins")); Process.Start(new ProcessStartInfo("explorer.exe", Path.Combine(settings.GameDirectory, "CustomSkinLoader", "LocalSkin", "skins")) { UseShellExecute = true }); }
            catch (Exception ex) { Fail("Не удалось открыть папку скинов", ex); }
        };

        void UpdateSkinPreview()
        {
            if (profileList.SelectedIndex < 0 || profileList.SelectedIndex >= settings.OfflineProfiles.Count)
                return;

            var profile = settings.OfflineProfiles[profileList.SelectedIndex];
            model.SelectedIndex = profile.Slim ? 1 : 0;
            preview.Slim = profile.Slim;
            if (!string.IsNullOrWhiteSpace(profile.SkinPath) && File.Exists(profile.SkinPath))
            {
                try
                {
                    LoadPreview(profile.SkinPath);
                    skinStatus.Text = "3D-просмотр: " + profile.Username;
                }
                catch (Exception ex)
                {
                    preview.SetSkin(null);
                    skinStatus.Text = "Не удалось показать скин: " + ex.Message;
                }
            }
            else
            {
                preview.SetSkin(null);
                skinStatus.Text = "Для этого профиля локальный скин ещё не выбран.";
            }
        }

        profileList.SelectedIndexChanged += (_, _) => UpdateSkinPreview();
        UpdateSkinPreview();

        p.Controls.Add(skinCard);
        skinCard.Controls.AddRange(new Control[] { preview, skinStatus, choose, bridge, model, openFolder });

        void LayoutSkinCard()
        {
            var availableWidth = Math.Max(500, skinCard.ClientSize.Width);
            var textWidth = Math.Max(220, availableWidth - 286);
            skinStatus.SetBounds(260, 55, textWidth, 70);
            choose.SetBounds(260, 140, 160, 40);
            bridge.SetBounds(Math.Max(430, availableWidth - 240), 140, 210, 40);
            model.SetBounds(260, 195, 190, 36);
            openFolder.SetBounds(260, 240, 190, 38);
        }
        skinCard.Resize += (_, _) => LayoutSkinCard();
        LayoutSkinCard();

        var info = new Label
        {
            Text = "Важно: сам Minecraft Java не умеет показывать произвольный локальный PNG в обычном offline-профиле. Поэтому NovaCraft использует официальный для этой задачи механизм CustomSkinLoader: он читает LocalSkin/<USERNAME>.png. Другие игроки увидят такой скин только если их клиент тоже поддерживает этот механизм.",
            AutoSize = false, Size = new Size(850, 90), Location = new Point(24, 645),
            ForeColor = Color.FromArgb(145, 155, 178),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        p.Controls.Add(info);
        p.Resize += (_, _) => info.Width = Math.Max(280, p.ClientSize.Width - info.Left - 24);
        info.Width = Math.Max(280, p.ClientSize.Width - info.Left - 24);
        return p;
    }

    static string OfflineUuid(string username) => OfflineProfileRules.OfflineUuid(username);

    async Task InstallCustomSkinLoaderAsync(Label result)
    {
        if (isBusy) return;

        var targetBuild = ActiveBuild;
        var targetGameDirectory = Path.GetFullPath(targetBuild.GameDirectory);
        var game = version.SelectedItem?.ToString() ?? targetBuild.MinecraftVersion;
        var loader = targetBuild.Loader;
        LauncherDownloadTask? downloadTask = null;
        try
        {
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
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps ||
                !string.Equals(uri.Host, "cdn.modrinth.com", StringComparison.OrdinalIgnoreCase))
                throw new SecurityException("Разрешена загрузка Skin Bridge только с HTTPS cdn.modrinth.com.");

            result.Text = "Скачиваю Skin Bridge и проверяю SHA-512…";
            if (!string.Equals(Path.GetFileName(filename), filename, StringComparison.Ordinal))
                throw new InvalidDataException("CustomSkinLoader вернул небезопасное имя файла.");

            var mods = Path.Combine(targetGameDirectory, "mods");
            Directory.CreateDirectory(mods);
            var destination = Path.Combine(mods, filename);
            downloadTask = BeginDownloadTask("CustomSkinLoader · Skin Bridge", filename, "Modrinth", targetBuild.Name);

            if (File.Exists(destination))
            {
                if (!await DownloadService.HasMatchingSha512Async(destination, sha, CancellationToken.None))
                    throw new CryptographicException("В активной сборке уже есть CustomSkinLoader с другим SHA-512. Файл не перезаписан.");
                downloadTask.Status = "Уже установлено";
                downloadTask.Progress = 1;
                downloadTask.Detail = "Локальный SHA-512 совпадает с Modrinth";
                RefreshDownloadTaskPage();
            }
            else
            {
                var fileProgress = new Progress<double>(value =>
                    UpdateDownloadTask(downloadTask, "Загружается", value, "SHA-512 · " + game + " · " + loader));

                await DownloadService.DownloadVerifiedAsync(
                    http,
                    uri,
                    sha,
                    mods,
                    filename,
                    new[] { ".jar" },
                    50L * 1024 * 1024,
                    fileProgress,
                    CancellationToken.None,
                    name => MessageBox.Show(
                        "Установить Skin Bridge: " + name + "?",
                        "Подтверждение установки",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning) == DialogResult.Yes);

                CompleteDownloadTask(downloadTask, "Skin Bridge проверен по SHA-512 и установлен в mods/");
            }

            foreach (var profile in settings.OfflineProfiles)
            {
                if (string.IsNullOrWhiteSpace(profile.SkinPath) || !File.Exists(profile.SkinPath)) continue;
                var profileSkinDestination = GetProfileSkinPath(profile);
                Directory.CreateDirectory(Path.GetDirectoryName(profileSkinDestination)!);
                if (!string.Equals(Path.GetFullPath(profile.SkinPath), Path.GetFullPath(profileSkinDestination), StringComparison.OrdinalIgnoreCase))
                    File.Copy(profile.SkinPath, profileSkinDestination, true);
            }

            SettingsService.Save(settings);
            CompleteDownloadTask(downloadTask, "Skin Bridge установлен в mods/ активной сборки");
            result.Text = "Skin Bridge установлен. Локальный скин будет загружаться через CustomSkinLoader.";
            status.Text = "Skin Bridge готов";
        }
        catch (Exception ex)
        {
            if (downloadTask != null)
                FailDownloadTask(downloadTask, ex);
            Fail("Не удалось установить Skin Bridge", ex);
        }
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
        var p = new TabPage("Производительность")
        {
            BackColor = Color.FromArgb(13, 15, 25),
            Padding = new Padding(24),
            AutoScroll = true
        };
        p.Controls.Add(new Label
        {
            Text = "ПРОИЗВОДИТЕЛЬНОСТЬ",
            AutoSize = true,
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 25, FontStyle.Bold),
            Location = new Point(24, 18)
        });

        var memory = SystemMemoryStatus.Read();
        var recommendedHeap = PerformanceProfileRules.RecommendedHeapMb(memory);
        var recommended = recommendedHeap ?? settings.MaximumRamMb;
        var memoryInfo = new Label
        {
            Text = PerformanceProfileRules.Describe(memory),
            AutoSize = false,
            Size = new Size(810, 64),
            ForeColor = Color.FromArgb(190, 200, 218),
            Location = new Point(27, 68)
        };
        p.Controls.Add(memoryInfo);

        var autoCard = ModernCard(820, 190, "БЕЗОПАСНЫЙ ЛИМИТ ОЗУ JAVA", Color.FromArgb(23, 27, 42));
        autoCard.Location = new Point(24, 145);
        autoCard.Controls.Add(new Label
        {
            Text = recommendedHeap.HasValue
                ? $"Текущий лимит: {settings.MaximumRamMb} МБ\nРекомендация для текущей свободной памяти: {recommendedHeap.Value} МБ\nЛимит пересчитывается по фактической памяти Windows, а не по выдуманному FPS-профилю."
                : PerformanceProfileRules.IsLowAvailableMemory(memory)
                    ? $"Текущий лимит: {settings.MaximumRamMb} МБ\nСвободной памяти недостаточно для безопасного автоподбора. Закрой тяжёлые программы."
                    : $"Текущий лимит: {settings.MaximumRamMb} МБ\nWindows не предоставила сведения об ОЗУ. Автоподбор выключен.",
            AutoSize = true,
            ForeColor = Color.FromArgb(190, 198, 215),
            Location = new Point(18, 50)
        });
        var apply = ModernButton("Применить рекомендацию", 220, 38);
        apply.Location = new Point(18, 132);
        apply.Enabled = recommendedHeap.HasValue;
        apply.Click += (_, _) =>
        {
            if (!recommendedHeap.HasValue)
                return;
            settings.MaximumRamMb = recommended;
            settings.FpsBoost = false;
            if (ram.Minimum <= recommended && ram.Maximum >= recommended)
                ram.Value = recommended;
            SettingsService.Save(settings);
            status.Text = $"Лимит Java установлен: {recommended} МБ";
            MessageBox.Show(
                "Изменён только лимит памяти Java. Это не FPS-мод и не гарантия прироста кадров. Для тяжёлых сборок может потребоваться больше памяти.",
                "Параметры запуска обновлены",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        };
        autoCard.Controls.Add(apply);

        var modsCard = ModernCard(820, 190, "ОПТИМИЗАЦИЯ ГРАФИКИ И МОДОВ", Color.FromArgb(22, 25, 39));
        modsCard.Location = new Point(24, 355);
        modsCard.Controls.Add(new Label
        {
            Text = "В этой сборке NovaCraft не устанавливает собственный Java-мод и не обещает автоматический прирост FPS.\nДля Fabric обычно рассматривают Sodium (рендеринг), Lithium (логика игры) и FerriteCore (память),\nно совместимость зависит от версии Minecraft, загрузчика и остальных модов.",
            AutoSize = true,
            ForeColor = Color.FromArgb(181, 190, 207),
            Location = new Point(18, 50)
        });
        var openModrinth = ModernButton("Открыть оптимизационные моды на Modrinth", 330, 38);
        openModrinth.Location = new Point(18, 132);
        openModrinth.Click += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo(
                    "https://modrinth.com/mods?g=categories:optimization&v=" + Uri.EscapeDataString(ActiveBuild.MinecraftVersion) +
                    "&l=" + Uri.EscapeDataString(ActiveBuild.Loader))
                { UseShellExecute = true });
            }
            catch (Exception ex) { MessageBox.Show("Не удалось открыть Modrinth: " + ex.Message); }
        };
        modsCard.Controls.Add(openModrinth);

        var safety = new Label
        {
            Text = "NovaCraft не меняет реестр, драйверы и настройки Windows, не отключает защиту и не добавляет себя в автозагрузку. Дополнительные JVM-аргументы задаются вручную в настройках.",
            AutoSize = false,
            Size = new Size(810, 48),
            ForeColor = Color.FromArgb(151, 161, 183),
            Location = new Point(27, 565)
        };
        p.Controls.AddRange(new Control[] { autoCard, modsCard, safety });
        return p;
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

        var game=ModernCard(720,225,"ИГРА",Color.FromArgb(25,28,45)); game.Location=new Point(24,105);
        game.Controls.Add(new Label{Text="Папка Minecraft",AutoSize=true,ForeColor=Color.Silver,Location=new Point(18,44)});
        gameDir.Text=settings.GameDirectory; gameDir.SetBounds(18,68,520,34);
        var browse=new Button{Text="Выбрать…",Width=105,Height=34,Location=new Point(548,68),FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(38,43,63),ForeColor=Color.White};browse.FlatAppearance.BorderSize=0;
        browse.Click+=(_,_)=>{using var d=new FolderBrowserDialog{Description="Выберите папку Minecraft",SelectedPath=gameDir.Text};if(d.ShowDialog()==DialogResult.OK)gameDir.Text=d.SelectedPath;};
        game.Controls.AddRange(new Control[]{gameDir,browse});
        game.Controls.Add(new Label{Text="Оперативная память",AutoSize=true,ForeColor=Color.Silver,Location=new Point(18,116)});
        ram.Minimum=1024;ram.Maximum=8192;ram.Increment=256;ram.Value=Math.Clamp(settings.MaximumRamMb,1024,8192);ram.SetBounds(150,112,170,34);game.Controls.Add(ram);
        game.Controls.Add(new Label{Text="МБ",AutoSize=true,ForeColor=Color.Silver,Location=new Point(325,120)});
        game.Controls.Add(new Label{Text="Разрешение",AutoSize=true,ForeColor=Color.Silver,Location=new Point(390,116)});
        var desktopSize = GetDesktopResolution();
        var screenWidthControl = new NumericUpDown { Minimum=800, Maximum=7680, Value=Math.Clamp(settings.UseDesktopResolution ? desktopSize.Width : settings.ScreenWidth,800,7680), Width=85, Location=new Point(470,112) };
        var screenHeightControl = new NumericUpDown { Minimum=600, Maximum=4320, Value=Math.Clamp(settings.UseDesktopResolution ? desktopSize.Height : settings.ScreenHeight,600,4320), Width=85, Location=new Point(565,112) };
        widthInput = screenWidthControl;
        heightInput = screenHeightControl;
        var desktopResolutionCheck = new CheckBox { Text=$"Использовать разрешение Windows ({desktopSize.Width}×{desktopSize.Height})", Checked=settings.UseDesktopResolution, AutoSize=true, Location=new Point(18,166), ForeColor=Color.White };
        useDesktopResolutionInput = desktopResolutionCheck;
        void UpdateResolutionInputs()
        {
            screenWidthControl.Enabled = !desktopResolutionCheck.Checked;
            screenHeightControl.Enabled = !desktopResolutionCheck.Checked;
        }
        desktopResolutionCheck.CheckedChanged += (_,_) => UpdateResolutionInputs();
        UpdateResolutionInputs();
        game.Controls.AddRange(new Control[]{screenWidthControl,screenHeightControl,desktopResolutionCheck});

        var launch=ModernCard(720,225,"ЗАПУСК",Color.FromArgb(25,28,45));launch.Location=new Point(24,345);
        fullScreenInput=new CheckBox{Text="Полный экран",Checked=settings.FullScreen,AutoSize=true,Location=new Point(18,48),ForeColor=Color.White};
        closeAfterLaunchInput=new CheckBox{Text="Закрывать NovaCraft после запуска Minecraft",Checked=settings.CloseAfterLaunch,AutoSize=true,Location=new Point(18,82),ForeColor=Color.White};
        autoRepairInput=new CheckBox{Text="Автоисправление типовых ошибок",Checked=settings.AutoRepair,AutoSize=true,Location=new Point(18,116),ForeColor=Color.White};
        launch.Controls.AddRange(new Control[]{fullScreenInput,closeAfterLaunchInput,autoRepairInput});
        launch.Controls.Add(new Label{Text="Дополнительные аргументы Java",AutoSize=true,ForeColor=Color.Silver,Location=new Point(18,151)});
        jvmArgsInput=new TextBox{Text=settings.ExtraJvmArguments,Width=650,Location=new Point(18,174),BackColor=Color.FromArgb(34,37,58),ForeColor=Color.White};
        launch.Controls.Add(jvmArgsInput);

        var save=ModernButton("✓  Сохранить настройки",210,42);save.Location=new Point(24,595);save.Click+=(_,_)=>{try{SaveSettings();status.Text="Настройки сохранены";}catch(Exception ex){Fail("Не удалось сохранить настройки",ex);}};p.Controls.AddRange(new Control[]{game,launch,save});
        return p;
    }

    TabPage DownloadsPage()
    {
        var p = new TabPage("Загрузки")
        {
            BackColor = Color.FromArgb(13,15,25),
            Padding = new Padding(20)
        };
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 92,
            BackColor = Color.FromArgb(13,15,25)
        };
        header.Controls.Add(new Label
        {
            Text = "Загрузки",
            AutoSize = true,
            Location = new Point(4,2),
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold",25,FontStyle.Bold)
        });
        downloadSummary = new Label
        {
            Text = "Нет активных загрузок",
            AutoSize = false,
            Location = new Point(7,48),
            ForeColor = Color.FromArgb(150,162,186)
        };
        var clear = new Button
        {
            Text = "Очистить завершённые",
            Location = new Point(650,42),
            Width = 190,
            Height = 34,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(38,42,57),
            ForeColor = Color.White
        };
        clear.FlatAppearance.BorderSize = 0;
        clear.Click += (_,_) =>
        {
            downloadTasks.RemoveAll(t => t.Status is "Установлено" or "Уже установлено" or "Ошибка");
            RefreshDownloadTaskPage();
        };
        header.Controls.AddRange(new Control[] { downloadSummary, clear });
        void LayoutDownloadHeader()
        {
            var width = Math.Max(560, header.ClientSize.Width);
            clear.SetBounds(Math.Max(350, width - 202), 42, 190, 34);
            downloadSummary!.SetBounds(7, 48, Math.Max(200, clear.Left - 18), 24);
        }
        header.Resize += (_,_) => LayoutDownloadHeader();
        LayoutDownloadHeader();

        downloadTaskList = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(4,8,14,24),
            BackColor = Color.FromArgb(13,15,25)
        };
        p.Controls.Add(downloadTaskList);
        p.Controls.Add(header);
        p.Disposed += (_,_) =>
        {
            downloadTaskList = null;
            downloadSummary = null;
            downloadEmptyPanel = null;
            downloadTaskVisuals.Clear();
        };
        p.Enter += (_,_) => RefreshDownloadTaskPage();
        RefreshDownloadTaskPage();
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
        var requestedBuildId = settings.ActiveBuildId;
        var requestedDirectory = Path.GetFullPath(ActiveBuild.GameDirectory);
        var requestedVersion = ActiveBuild.MinecraftVersion;
        try
        {
            status.Text = "Получение списка версий…";
            var launcherForRequestedBuild = new MinecraftLauncher(new MinecraftPath(requestedDirectory));
            var all = await launcherForRequestedBuild.GetAllVersionsAsync();

            // Do not let a delayed response for an old profile overwrite the newly selected build.
            if (!string.Equals(settings.ActiveBuildId, requestedBuildId, StringComparison.OrdinalIgnoreCase))
                return;

            var releases = all?.Where(v => string.Equals(v?.Type?.ToString(), "release", StringComparison.OrdinalIgnoreCase))
                .Select(v => v!.Name).Distinct().Take(100).ToList() ?? new List<string>();
            version.Items.Clear();
            foreach (var item in releases) version.Items.Add(item);
            version.SelectedItem = releases.Contains(requestedVersion) ? requestedVersion : releases.FirstOrDefault();
            if (version.SelectedItem is string selectedVersion)
                settings.LastVersion = selectedVersion;

            if (version.Tag is ListView list)
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

            WriteLog("Версий получено: " + releases.Count);
            status.Text = "Готов";
        }
        catch (Exception ex)
        {
            if (string.Equals(settings.ActiveBuildId, requestedBuildId, StringComparison.OrdinalIgnoreCase))
                Fail("Не удалось получить список версий", ex);
        }
    }

    static string FormatDownloadBytes(long bytes)
        => bytes < 1024L * 1024
            ? $"{bytes / 1024d:0.0} KB"
            : $"{bytes / (1024d * 1024d):0.0} MB";

    IProgress<ByteProgress>? CreateCmlLibByteProgress(
        LauncherDownloadTask? task,
        string phase,
        double progressStart,
        double progressRange)
    {
        if (task == null) return null;

        return new Progress<ByteProgress>(sample =>
        {
            var totalBytes = Math.Max(0L, sample.TotalBytes);
            var completedBytes = Math.Max(0L, sample.ProgressedBytes);
            var ratio = totalBytes > 0
                ? Math.Clamp(completedBytes / (double)totalBytes, 0d, 1d)
                : 0d;
            var phaseProgress = Math.Clamp(progressStart + ratio * progressRange, 0d, 0.99d);
            var monotonicProgress = Math.Max(task.Progress, phaseProgress);
            var detail = totalBytes > 0
                ? $"{phase} · {FormatDownloadBytes(completedBytes)} / {FormatDownloadBytes(totalBytes)}"
                : phase;
            UpdateDownloadTask(task, "Загружается", monotonicProgress, detail);
        });
    }

    async Task<string> InstallSelectedLoaderAsync(string vanillaVersion, LauncherDownloadTask? downloadTask = null)
    {
        var loaderName = settings.ModLoader;
        var launcherInstance = GetLauncher();

        if (loaderName == "vanilla")
        {
            var vanillaProgress = CreateCmlLibByteProgress(downloadTask, "Загрузка Minecraft", 0.05, 0.90);
            await launcherInstance.InstallAsync(
                vanillaVersion,
                new EmptyProgress<InstallerProgressChangedEventArgs>(),
                vanillaProgress,
                CancellationToken.None);
            return vanillaVersion;
        }

        WriteLog("Установка " + loaderName + " для Minecraft " + vanillaVersion);
        if (downloadTask != null)
            UpdateDownloadTask(downloadTask, "Устанавливается", Math.Max(downloadTask.Progress, 0.03), "Подготовка " + loaderName.ToUpperInvariant());

        string installedId;
        var path = new MinecraftPath(settings.GameDirectory);
        if (loaderName == "fabric")
        {
            var fabric = new FabricInstaller(http);
            installedId = await fabric.Install(vanillaVersion, path);
        }
        else if (loaderName == "forge")
        {
            var forge = new ForgeInstaller(launcherInstance);
            var forgeOptions = new ForgeInstallOptions
            {
                InstallerOutput = new Progress<string>(line => WriteLog(line)),
                ByteProgress = CreateCmlLibByteProgress(downloadTask, "Загрузка Forge", 0.05, 0.40),
                CancellationToken = CancellationToken.None
            };
            installedId = string.IsNullOrWhiteSpace(ActiveBuild.LoaderVersion)
                ? await forge.Install(vanillaVersion, forgeOptions)
                : await forge.Install(vanillaVersion, ActiveBuild.LoaderVersion, forgeOptions);
        }
        else if (loaderName == "quilt")
        {
            var quilt = new QuiltInstaller(http);
            installedId = await quilt.Install(vanillaVersion, path);
        }
        else throw new InvalidOperationException("Неизвестный загрузчик.");

        if (downloadTask != null)
            UpdateDownloadTask(downloadTask, "Устанавливается", Math.Max(downloadTask.Progress, 0.48), "Загрузчик готов · проверка библиотек и ресурсов Minecraft");

        var gameProgress = CreateCmlLibByteProgress(downloadTask, "Проверка и загрузка файлов Minecraft", 0.50, 0.45);
        await launcherInstance.InstallAsync(
            installedId,
            new EmptyProgress<InstallerProgressChangedEventArgs>(),
            gameProgress,
            CancellationToken.None);

        WriteLog("Загрузчик установлен: " + installedId);
        return installedId;
    }

    async Task InstallAsync()
    {
        if (isBusy) return;
        if (version.SelectedItem is not string v) return;

        var task = BeginDownloadTask(
            "Minecraft " + v + " · " + settings.ModLoader.ToUpperInvariant(),
            "Minecraft " + v,
            "CmlLib",
            ActiveBuild.Name);
        try
        {
            SetBusy(true, "Установка " + settings.ModLoader + " для " + v + "…");
            UpdateDownloadTask(task, "Устанавливается", 0.01, "Активная сборка: " + ActiveBuild.Name);
            var installedId = await InstallSelectedLoaderAsync(v, task);
            settings.LastVersion = v;
            SettingsService.Save(settings);
            CompleteDownloadTask(task, "Все файлы Minecraft проверены и подготовлены: " + installedId);
            WriteLog("Готово: " + installedId);
            status.Text = "Установка завершена: " + installedId;
        }
        catch (Exception ex)
        {
            FailDownloadTask(task, ex);
            Fail("Ошибка установки загрузчика", ex);
        }
        finally { SetBusy(false); }
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
            ScreenWidth = settings.UseDesktopResolution ? GetDesktopResolution().Width : settings.ScreenWidth,
            ScreenHeight = settings.UseDesktopResolution ? GetDesktopResolution().Height : settings.ScreenHeight,
            FullScreen = settings.FullScreen
        };
        var extra = new List<MArgument>();
        if (!string.IsNullOrWhiteSpace(settings.ExtraJvmArguments))
            extra.Add(MArgument.FromCommandLine(settings.ExtraJvmArguments));
        if (extra.Count > 0) option.ExtraJvmArguments = extra;
        return option;
    }

    async Task LaunchAsync()
    {
        if (isBusy) return;
        if (version.SelectedItem is not string v) { MessageBox.Show("Сначала выбери версию Minecraft."); return; }
        var activeProfile = settings.OfflineProfiles?.FirstOrDefault(x => x.Id == settings.ActiveProfileId);
        var nicknameValidation = NickValidator.Validate(activeProfile?.Username);
        if (!nicknameValidation.ok)
        {
            MessageBox.Show(nicknameValidation.error, "Некорректный ник", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string launchVersion = v;
        var installationCompleted = false;
        LauncherDownloadTask? installTask = null;
        try
        {
            SaveSettings();
            SetBusy(true, "Подготовка игры…");
            installTask = BeginDownloadTask(
                "Запуск Minecraft " + v,
                "Minecraft " + v,
                "CmlLib",
                ActiveBuild.Name);
            UpdateDownloadTask(installTask, "Устанавливается", 0.01, "Проверка файлов активной сборки");
            launchVersion = await InstallSelectedLoaderAsync(v, installTask);
            installationCompleted = true;
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
            ActiveBuild.LastPlayedUtc = DateTime.UtcNow;
            ActiveBuild.MinecraftVersion = v;
            SettingsService.Save(settings);
            CompleteDownloadTask(installTask, "Minecraft подготовлен и запущен: " + launchVersion);
            WriteLog("Minecraft запущен: " + launchVersion);
            status.Text = "Игра запущена";
            if (settings.CloseAfterLaunch) BeginInvoke(new Action(Close));
        }
        catch (Exception ex)
        {
            WriteLog("Ошибка запуска: " + ex);
            if (settings.AutoRepair && installationCompleted && IsKnownLaunchError(ex))
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
                    UpdateDownloadTask(installTask!, "Устанавливается", Math.Max(installTask!.Progress, 0.50), "Повторная проверка файлов после ошибки запуска");
                    var repairProgress = CreateCmlLibByteProgress(installTask, "Автоисправление · файлы Minecraft", 0.52, 0.43);
                    await GetLauncher().InstallAsync(
                        launchVersion,
                        new EmptyProgress<InstallerProgressChangedEventArgs>(),
                        repairProgress,
                        CancellationToken.None);
                    var repaired = await GetLauncher().BuildProcessAsync(launchVersion, CreateLaunchOptions());
                    repaired.Start();
                    CompleteDownloadTask(installTask!, "Проверка завершена, игра запущена после восстановления");
                    status.Text = "Файлы восстановлены, игра запущена";
                    WriteLog("Автоисправление завершено: " + launchVersion);
                    if (settings.CloseAfterLaunch) BeginInvoke(new Action(Close));
                    return;
                }
                catch (Exception repairError)
                {
                    WriteLog("Автоисправление не помогло: " + repairError);
                    if (installTask != null)
                        FailDownloadTask(installTask, repairError);
                    Fail("Автоисправление не смогло устранить проблему", repairError);
                }
            }
            else
            {
                if (installTask != null)
                    FailDownloadTask(installTask, ex);
                Fail("Minecraft не удалось запустить", ex);
            }
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
        RoundCorners(card, 16);
        AppTheme.StyleCatalogCard(card);

        icon = new PictureBox
        {
            Location = new Point(14, 14),
            Size = new Size(70, 70),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.FromArgb(35, 39, 58),
            Image = CreateProjectFallbackIcon(title, projectType),
            AccessibleName = "Иконка проекта: " + title
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


    internal static Bitmap CreateProjectFallbackIcon(string title, string projectType, int size = 96)
    {
        size = Math.Clamp(size, 48, 256);
        var safeTitle = string.IsNullOrWhiteSpace(title) ? "Minecraft" : title.Trim();
        if (safeTitle.Length > 96)
            safeTitle = safeTitle[..96];

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(safeTitle + "|" + projectType));
        var first = Color.FromArgb(255, 66 + digest[0] % 58, 48 + digest[1] % 58, 116 + digest[2] % 72);
        var second = Color.FromArgb(255, 12 + digest[3] % 20, 17 + digest[4] % 24, 34 + digest[5] % 28);

        static Color Shade(Color color, float factor) => Color.FromArgb(
            255,
            Math.Clamp((int)(color.R * factor), 0, 255),
            Math.Clamp((int)(color.G * factor), 0, 255),
            Math.Clamp((int)(color.B * factor), 0, 255));

        var bitmap = new Bitmap(size, size);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        graphics.Clear(second);

        using (var background = new System.Drawing.Drawing2D.LinearGradientBrush(
            new Rectangle(0, 0, size, size), first, second, 35f))
        {
            graphics.FillRectangle(background, 0, 0, size, size);
        }

        using (var glow = new SolidBrush(Color.FromArgb(38, 226, 222, 255)))
            graphics.FillEllipse(glow, (int)(size * 0.40f), -(int)(size * 0.22f), (int)(size * 0.86f), (int)(size * 0.86f));

        using (var grid = new Pen(Color.FromArgb(16, 230, 235, 255), Math.Max(0.5f, size / 160f)))
        {
            for (var index = 1; index < 6; index++)
            {
                var position = size * index / 6f;
                graphics.DrawLine(grid, position, 0, position, size);
                graphics.DrawLine(grid, 0, position, size, position);
            }
        }

        // Isometric voxel emblem: three independently shaded faces give the fallback
        // a real project mark instead of a bare pair of letters.
        var centerX = size * 0.5f;
        var unit = size * 0.205f;
        var topY = size * 0.105f;
        var midY = topY + unit * 0.62f;
        var centerY = topY + unit * 1.24f;
        var bottomY = topY + unit * 2.36f;

        PointF[] topFace =
        {
            new(centerX, topY),
            new(centerX + unit, midY),
            new(centerX, centerY),
            new(centerX - unit, midY)
        };
        PointF[] leftFace =
        {
            new(centerX - unit, midY),
            new(centerX, centerY),
            new(centerX, bottomY),
            new(centerX - unit, bottomY - unit * 0.62f)
        };
        PointF[] rightFace =
        {
            new(centerX, centerY),
            new(centerX + unit, midY),
            new(centerX + unit, bottomY - unit * 0.62f),
            new(centerX, bottomY)
        };

        using (var topBrush = new SolidBrush(Shade(first, 1.42f)))
        using (var leftBrush = new SolidBrush(Shade(first, 0.92f)))
        using (var rightBrush = new SolidBrush(Shade(first, 0.62f)))
        using (var edge = new Pen(Color.FromArgb(188, 239, 242, 255), Math.Max(1f, size / 80f)))
        {
            graphics.FillPolygon(leftBrush, leftFace);
            graphics.FillPolygon(rightBrush, rightFace);
            graphics.FillPolygon(topBrush, topFace);
            graphics.DrawPolygon(edge, leftFace);
            graphics.DrawPolygon(edge, rightFace);
            graphics.DrawPolygon(edge, topFace);
        }

        // Small deterministic block lights make two projects with similar initials
        // remain visually distinct while keeping the icon fully local.
        for (var index = 0; index < 4; index++)
        {
            var side = index % 2 == 0 ? -1f : 1f;
            var x = centerX + side * (size * (0.31f + (digest[8 + index] % 8) / 100f));
            var y = size * (0.27f + (digest[12 + index] % 22) / 100f);
            var block = Math.Max(2f, size * (0.035f + (digest[16 + index] % 4) / 150f));
            var alpha = 90 + digest[20 + index] % 100;
            using var blockBrush = new SolidBrush(Color.FromArgb(alpha, Shade(first, 1.2f)));
            graphics.FillRectangle(blockBrush, x, y, block, block);
        }

        var words = safeTitle.Split(new[] { ' ', '-', '_', '.', ':' }, StringSplitOptions.RemoveEmptyEntries);
        var initials = words.Length >= 2
            ? string.Concat(words.Take(2).Select(word => char.ToUpperInvariant(word[0])))
            : safeTitle.Length >= 2 ? safeTitle[..2].ToUpperInvariant() : safeTitle.ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(initials))
            initials = "MC";

        var label = projectType switch
        {
            "shader" => "SHADER",
            "resourcepack" => "RESOURCE",
            "datapack" => "DATA",
            "modpack" => "PACK",
            _ => "MOD"
        };
        using (var tagFont = new Font("Segoe UI Semibold", Math.Max(6.5f, size * 0.075f), FontStyle.Bold, GraphicsUnit.Pixel))
        using (var tagBrush = new SolidBrush(Color.FromArgb(188, 224, 229, 247)))
        {
            graphics.DrawString(label, tagFont, tagBrush, new PointF(size * 0.09f, size * 0.055f));
        }

        var badgeBounds = new RectangleF(size * 0.18f, size * 0.715f, size * 0.64f, size * 0.235f);
        using (var badge = new SolidBrush(Color.FromArgb(158, 8, 11, 24)))
        using (var font = new Font("Segoe UI Semibold", Math.Max(11f, size * 0.19f), FontStyle.Bold, GraphicsUnit.Pixel))
        using (var brush = new SolidBrush(Color.FromArgb(248, 255, 255, 255)))
        using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
        {
            graphics.FillRectangle(badge, badgeBounds);
            graphics.DrawString(initials, font, brush, badgeBounds, format);
        }

        using (var border = new Pen(Color.FromArgb(82, 255, 255, 255), Math.Max(1f, size / 64f)))
            graphics.DrawRectangle(border, 1, 1, size - 3, size - 3);

        return bitmap;
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

    async Task LoadCurseForgeProjectIconAsync(PictureBox picture, string? iconUrl, SemaphoreSlim gate)
    {
        if (string.IsNullOrWhiteSpace(iconUrl) ||
            !Uri.TryCreate(iconUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(uri.Host, "media.forgecdn.net", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.UserInfo))
            return;

        await gate.WaitAsync();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
                return;

            var finalUri = response.RequestMessage?.RequestUri;
            if (finalUri == null ||
                finalUri.Scheme != Uri.UriSchemeHttps ||
                !string.Equals(finalUri.Host, "media.forgecdn.net", StringComparison.OrdinalIgnoreCase))
                return;

            if (response.Content.Headers.ContentLength is long length && (length <= 0 || length > 5 * 1024 * 1024))
                return;
            if (response.Content.Headers.ContentType?.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) != true)
                return;

            var bytes = await ReadBoundedContentAsync(response.Content, 5 * 1024 * 1024);
            if (bytes.Length == 0 || bytes.Length > 5 * 1024 * 1024)
                return;

            using var stream = new MemoryStream(bytes);
            using var source = Image.FromStream(stream);
            var bitmap = new Bitmap(source);
            if (picture.IsDisposed)
            {
                bitmap.Dispose();
                return;
            }

            picture.Image?.Dispose();
            picture.Image = bitmap;
        }
        catch
        {
            // Missing or unavailable artwork must not break the catalog.
        }
        finally
        {
            gate.Release();
        }
    }

    internal static string? SelectProjectArtworkUrl(JsonNode? hit)
    {
        if (hit is null)
            return null;

        var candidates = new List<string?>
        {
            hit["icon_url"]?.ToString(),
            hit["raw_icon_url"]?.ToString(),
            hit["featured_gallery"]?.ToString()
        };

        if (hit["gallery"] is JsonArray gallery)
        {
            foreach (var item in gallery.Take(8))
            {
                var candidate = item is JsonObject imageObject
                    ? imageObject["url"]?.ToString()
                    : item?.ToString();
                candidates.Add(candidate);
            }
        }

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate) || candidate.Length > 2048 ||
                !Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
                continue;

            if (uri.Scheme == Uri.UriSchemeHttps &&
                uri.Port == 443 &&
                string.IsNullOrEmpty(uri.UserInfo) &&
                (string.Equals(uri.Host, "cdn.modrinth.com", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(uri.Host, "images.modrinth.com", StringComparison.OrdinalIgnoreCase)))
                return uri.AbsoluteUri;
        }

        return null;
    }

    internal static bool IsSafeProjectArtworkSize(int width, int height) =>
        width > 0 &&
        height > 0 &&
        width <= 2048 &&
        height <= 2048 &&
        (long)width * height <= 4_194_304;

    static string ProjectIconCacheDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NovaCraft", "cache", "project-icons");

    static Bitmap? LoadCachedProjectIcon(string cachePath)
    {
        try
        {
            var info = new FileInfo(cachePath);
            if (!info.Exists || info.Length <= 0 || info.Length > 256 * 1024)
                return null;

            using var stream = new FileStream(cachePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var source = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
            if (!IsSafeProjectArtworkSize(source.Width, source.Height))
                throw new InvalidDataException("Cached project artwork has unsafe dimensions.");

            var bitmap = new Bitmap(source);
            try { File.SetLastWriteTimeUtc(cachePath, DateTime.UtcNow); } catch { }
            return bitmap;
        }
        catch
        {
            try { File.Delete(cachePath); } catch { }
            return null;
        }
    }

    static void StoreProjectIconCache(string cachePath, byte[] bytes)
    {
        if (bytes.Length is <= 0 or > 256 * 1024)
            return;

        var directory = Path.GetDirectoryName(cachePath);
        if (string.IsNullOrWhiteSpace(directory))
            return;

        string? temporaryPath = null;
        try
        {
            Directory.CreateDirectory(directory);
            temporaryPath = cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(temporaryPath, bytes);
            File.Move(temporaryPath, cachePath, overwrite: true);

            var cachedFiles = Directory.GetFiles(directory, "*.img");
            if (cachedFiles.Length > 256)
            {
                var removeCount = cachedFiles.Length - 256;
                foreach (var oldFile in cachedFiles
                    .OrderBy(File.GetLastWriteTimeUtc)
                    .Take(removeCount))
                {
                    try { File.Delete(oldFile); } catch { }
                }
            }
        }
        catch
        {
            // Icon caching is optional and must never prevent the image from displaying.
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try
                {
                    if (File.Exists(temporaryPath))
                        File.Delete(temporaryPath);
                }
                catch { }
            }
        }
    }

    static void SetProjectIcon(PictureBox picture, Bitmap bitmap)
    {
        if (picture.IsDisposed)
        {
            bitmap.Dispose();
            return;
        }

        var previous = picture.Image;
        picture.Image = bitmap;
        if (previous is not null && !ReferenceEquals(previous, bitmap))
            previous.Dispose();
        picture.Controls.Clear();
    }

    async Task LoadProjectIconAsync(PictureBox picture, string? iconUrl, SemaphoreSlim gate)
    {
        if (string.IsNullOrWhiteSpace(iconUrl) || iconUrl.Length > 2048 ||
            !Uri.TryCreate(iconUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || uri.Port != 443 ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            (uri.Host != "cdn.modrinth.com" && uri.Host != "images.modrinth.com"))
            return;

        var cacheKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri))).ToLowerInvariant();
        var cachePath = Path.Combine(ProjectIconCacheDirectory, cacheKey + ".img");
        var cached = LoadCachedProjectIcon(cachePath);
        if (cached is not null)
        {
            SetProjectIcon(picture, cached);
            return;
        }

        await gate.WaitAsync();
        try
        {
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var finalUri = response.RequestMessage?.RequestUri;
            if (finalUri == null ||
                finalUri.Scheme != Uri.UriSchemeHttps ||
                finalUri.Port != 443 ||
                !string.IsNullOrEmpty(finalUri.UserInfo) ||
                (finalUri.Host != "cdn.modrinth.com" && finalUri.Host != "images.modrinth.com"))
                return;

            if (response.Content.Headers.ContentType?.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) != true)
                return;
            if (response.Content.Headers.ContentLength is long length && (length <= 0 || length > 5 * 1024 * 1024))
                return;

            var bytes = await ReadBoundedContentAsync(response.Content, 5 * 1024 * 1024);
            if (bytes.Length == 0 || bytes.Length > 5 * 1024 * 1024)
                return;

            using var ms = new MemoryStream(bytes);
            using var source = Image.FromStream(ms, useEmbeddedColorManagement: false, validateImageData: true);
            if (!IsSafeProjectArtworkSize(source.Width, source.Height))
                return;

            var bitmap = new Bitmap(source);
            if (picture.IsDisposed)
            {
                bitmap.Dispose();
                return;
            }

            SetProjectIcon(picture, bitmap);
            StoreProjectIconCache(cachePath, bytes);
        }
        catch
        {
            // An absent icon or unavailable CDN keeps the generated voxel emblem.
        }
        finally
        {
            gate.Release();
        }
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
                iconTasks.Add(LoadProjectIconAsync(picture, SelectProjectArtworkUrl(hit), gate));
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

        var requestedBuildId = settings.ActiveBuildId;
        var requestedVersion = version.SelectedItem?.ToString() ?? ActiveBuild.MinecraftVersion;
        var requestedLoader = ActiveBuild.Loader;
        var requestedOffset = reset ? 0 : modFeedOffset;
        var requestedQuery = modFeedQuery;
        var requestedSort = modFeedSort;

        modFeedLoading = true;
        try
        {
            status.Text = requestedOffset == 0 ? "Загружаю моды Modrinth…" : "Загружаю ещё моды…";
            var loaderFacet = requestedLoader switch
            {
                "forge" => "categories:forge",
                "quilt" => "categories:quilt",
                _ => "categories:fabric"
            };
            var facetGroups = new List<string[]> { new[] { "project_type:mod" }, new[] { loaderFacet } };
            if (!string.IsNullOrWhiteSpace(requestedVersion))
                facetGroups.Add(new[] { "versions:" + requestedVersion });
            var facets = Uri.EscapeDataString(JsonSerializer.Serialize(facetGroups));
            var query = string.IsNullOrWhiteSpace(requestedQuery) ? "" : "&query=" + Uri.EscapeDataString(requestedQuery);
            var url = "https://api.modrinth.com/v2/search?limit=24&offset=" + requestedOffset +
                      "&index=" + Uri.EscapeDataString(requestedSort) + query + "&facets=" + facets;
            var root = JsonNode.Parse(await http.GetStringAsync(url));
            var hits = (root?["hits"]?.AsArray() ?? new JsonArray()).ToList();

            if (!string.Equals(settings.ActiveBuildId, requestedBuildId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(ActiveBuild.Loader, requestedLoader, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(version.SelectedItem?.ToString() ?? ActiveBuild.MinecraftVersion, requestedVersion, StringComparison.OrdinalIgnoreCase))
                return;

            if (reset) modCards.Controls.Clear();

            using var gate = new SemaphoreSlim(4);
            var iconTasks = new List<Task>();
            foreach (var hit in hits)
            {
                var card = CreateProjectCard(hit!, out var picture, "mod");
                modCards.Controls.Add(card);
                iconTasks.Add(LoadProjectIconAsync(picture, SelectProjectArtworkUrl(hit), gate));
            }

            modFeedOffset = requestedOffset + hits.Count;
            await Task.WhenAll(iconTasks);

            if (string.Equals(settings.ActiveBuildId, requestedBuildId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(ActiveBuild.Loader, requestedLoader, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(version.SelectedItem?.ToString() ?? ActiveBuild.MinecraftVersion, requestedVersion, StringComparison.OrdinalIgnoreCase))
            {
                status.Text = hits.Count == 0
                    ? "Больше проектов не найдено"
                    : $"Показано модов: {modFeedOffset}";
            }
        }
        catch (Exception ex)
        {
            if (string.Equals(settings.ActiveBuildId, requestedBuildId, StringComparison.OrdinalIgnoreCase))
                Fail("Не удалось загрузить каталог модов Modrinth", ex);
        }
        finally
        {
            modFeedLoading = false;
            if (modFeedReloadPending && pageHost.SelectedIndex == 3)
            {
                modFeedReloadPending = false;
                modFeedOffset = 0;
                _ = LoadModFeedAsync(true);
            }
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
                iconTasks.Add(LoadProjectIconAsync(picture, SelectProjectArtworkUrl(hit), gate));
            }
            target.ResumeLayout();
            await Task.WhenAll(iconTasks);
            status.Text = "Найдено проектов: " + hits.Count;
        }
        catch (Exception ex) { Fail("Каталог Modrinth недоступен", ex); }
    }

    async Task DownloadCatalogArchiveAsync(string projectId, string title, string projectType, string? worldDirectory = null)
    {
        if (isBusy) return;
        LauncherDownloadTask? downloadTask = null;
        var targetBuild = ActiveBuild;
        var targetGameDirectory = Path.GetFullPath(targetBuild.GameDirectory);
        try
        {
            var game = version.SelectedItem?.ToString() ?? targetBuild.MinecraftVersion;
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
                targetDir = Path.Combine(targetGameDirectory, folderName);
            }
            else if (projectType == "shader")
            {
                folderName = "shaderpacks";
                targetDir = Path.Combine(targetGameDirectory, folderName);
            }
            else if (projectType == "datapack")
            {
                var savesRoot = Path.Combine(targetGameDirectory, "saves");
                if (string.IsNullOrWhiteSpace(worldDirectory) ||
                    !IsDirectoryWithin(savesRoot, worldDirectory) ||
                    !File.Exists(Path.Combine(worldDirectory, "level.dat")))
                    throw new InvalidOperationException("Выбери мир с level.dat внутри папки saves текущей активной сборки.");
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
            downloadTask = BeginDownloadTask(title, filename, "Modrinth", targetBuild.Name);
            var fileProgress = new Progress<double>(value =>
                UpdateDownloadTask(downloadTask, "Загружается", value, "SHA-512 · " + folderName));
            await DownloadService.DownloadVerifiedAsync(
                http, uri, expected, targetDir, filename, new[] { ".zip" },
                250L * 1024 * 1024, fileProgress, CancellationToken.None, _ => true,
                (archivePath, _) =>
                {
                    UpdateDownloadTask(downloadTask, "Проверка", 0.98, "Проверяю структуру ZIP-архива");
                    CatalogArchiveValidator.Validate(archivePath, projectType);
                    return Task.CompletedTask;
                });
            CompleteDownloadTask(downloadTask, "Файл установлен в " + folderName);
            WriteLog("Установлен архив Modrinth: " + destination);
            status.Text = "Архив установлен: " + filename;
        }
        catch (Exception ex)
        {
            if (downloadTask != null) FailDownloadTask(downloadTask, ex);
            Fail("Не удалось установить проект Modrinth", ex);
        }
    }

    async Task DownloadSelectedModAsync()
    {
        if (isBusy || string.IsNullOrWhiteSpace(selectedModProject)) return;
        var targetProjectId = selectedModProject;
        var targetProjectTitle = selectedModTitle ?? targetProjectId;
        var targetBuild = ActiveBuild;
        var targetGameDirectory = Path.GetFullPath(targetBuild.GameDirectory);
        var loaderName = targetBuild.Loader;
        LauncherDownloadTask? activeDownloadTask = null;
        try
        {
            if (loaderName == "vanilla")
                throw new InvalidOperationException("Для установки модов выбери Fabric, Quilt или Forge.");
            var game = version.SelectedItem?.ToString() ?? targetBuild.MinecraftVersion;
            var gameFilter = Uri.EscapeDataString(JsonSerializer.Serialize(new[] { game }));
            var loaderFilter = Uri.EscapeDataString(JsonSerializer.Serialize(new[] { loaderName }));
            var url = $"https://api.modrinth.com/v2/project/{Uri.EscapeDataString(targetProjectId)}/version?game_versions={gameFilter}&loaders={loaderFilter}&include_changelog=false";
            var arr = JsonNode.Parse(await http.GetStringAsync(url))?.AsArray();
            var selectedVersion = arr?.FirstOrDefault() ?? throw new InvalidOperationException("Совместимая версия мода не найдена.");
            var projectTitle = targetProjectTitle;
            var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { targetProjectId };
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

            var modsDir = Path.Combine(targetGameDirectory, "mods");
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
                activeDownloadTask = BeginDownloadTask(entry.Title, name, "Modrinth", targetBuild.Name);
                if (File.Exists(destination))
                {
                    var hashMatches = await DownloadService.HasMatchingSha512Async(
                        destination,
                        expectedSha512,
                        CancellationToken.None);

                    if (!hashMatches)
                        throw new CryptographicException(
                            "В активной сборке уже есть файл с таким именем, но SHA-512 не совпадает. NovaCraft не перезаписывал существующий файл: " + name);

                    activeDownloadTask.Status = "Уже установлено";
                    activeDownloadTask.Progress = 1;
                    activeDownloadTask.Detail = "Локальный SHA-512 совпадает с Modrinth";
                    RefreshDownloadTaskPage();
                    continue;
                }

                var modProgress = new Progress<double>(value =>
                    UpdateDownloadTask(activeDownloadTask, "Загружается", value, "SHA-512 · " + game + " · " + loaderName));
                await DownloadService.DownloadVerifiedAsync(
                    http, downloadUri, expectedSha512, modsDir, name, new[] { ".jar" },
                    250L * 1024 * 1024, modProgress, CancellationToken.None,
                    _ => true);
                CompleteDownloadTask(activeDownloadTask, "Проверен SHA-512 и установлен в mods/");
                WriteLog("Установлен мод/зависимость: " + name);
            }

            status.Text = files.Count == 1 ? "Мод установлен" : $"Мод установлен вместе с {files.Count - 1} обязательной зависимостью.";
        }
        catch (Exception ex)
        {
            if (activeDownloadTask != null && activeDownloadTask.Status != "Уже установлено")
                FailDownloadTask(activeDownloadTask, ex);
            Fail("Не удалось установить мод и зависимости", ex);
        }
    }

    void SaveSettings()
    {
        if (isBusy)
            throw new InvalidOperationException("Нельзя менять папку и параметры сборки во время установки или запуска Minecraft.");
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
        settings.FpsBoost = false;
        settings.GameDirectory = gameDir.Text.Trim();
        settings.LastVersion = version.SelectedItem?.ToString() ?? settings.LastVersion;
        settings.ModLoader = gameLoader.SelectedItem?.ToString() switch { "Forge" => "forge", "Quilt" => "quilt", "Vanilla" => "vanilla", _ => "fabric" };
        var activeBuild = settings.BuildProfiles.First(p => p.Id == settings.ActiveBuildId);
        var selectedGameDirectory = Path.GetFullPath(settings.GameDirectory);
        if (settings.BuildProfiles.Any(p => p.Id != activeBuild.Id &&
            string.Equals(Path.GetFullPath(p.GameDirectory), selectedGameDirectory, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Папка уже назначена другой сборке. У каждой сборки должна быть своя отдельная папка.");

        activeBuild.GameDirectory = selectedGameDirectory;
        activeBuild.MinecraftVersion = settings.LastVersion;
        if (!string.Equals(activeBuild.Loader, settings.ModLoader, StringComparison.OrdinalIgnoreCase))
            activeBuild.LoaderVersion = "";
        activeBuild.Loader = settings.ModLoader;
        settings.UseDesktopResolution = useDesktopResolutionInput?.Checked ?? settings.UseDesktopResolution;
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

    void SetBusy(bool busy, string? message = null)
    {
        isBusy = busy;
        play.Enabled = !busy;
        install.Enabled = !busy;
        buildSelector.Enabled = !busy;
        foreach (var button in navigationButtons)
            button.Enabled = !busy;
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        if (message != null) status.Text = message;
    }
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
        if (isBusy) return;
        LauncherDownloadTask? repairTask = null;
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

            var memory = SystemMemoryStatus.Read();
            var safeLimit = PerformanceProfileRules.RecommendedHeapMb(memory);
            if (!safeLimit.HasValue)
                notes.Add(memory.TotalMb == 0 || memory.AvailableMb == 0
                    ? "Windows не предоставила сведения о физической памяти; лимит Java оставлен без изменений."
                    : "Свободной памяти недостаточно для безопасного автоподбора; закрой тяжёлые программы. Лимит Java оставлен без изменений.");
            else if (settings.MaximumRamMb > safeLimit.Value)
            {
                settings.MaximumRamMb = safeLimit.Value;
                ram.Value = Math.Clamp(safeLimit.Value, (int)ram.Minimum, (int)ram.Maximum);
                notes.Add("Лимит памяти уменьшен по данным о свободной физической памяти.");
            }
            else notes.Add("Лимит памяти не превышает рассчитанную рекомендацию.");

            var selected = version.SelectedItem?.ToString() ?? settings.LastVersion;
            if (!string.IsNullOrWhiteSpace(selected))
            {
                repairTask = BeginDownloadTask(
                    "Восстановление Minecraft " + selected,
                    "Minecraft " + selected,
                    "CmlLib",
                    ActiveBuild.Name);
                UpdateDownloadTask(repairTask, "Устанавливается", 0.01, "Проверка активной сборки");
                await InstallSelectedLoaderAsync(selected, repairTask);
                CompleteDownloadTask(repairTask, "Файлы версии и загрузчика проверены и восстановлены");
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
            if (repairTask != null)
                FailDownloadTask(repairTask, ex);
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

internal sealed class EmptyProgress<T> : IProgress<T>
{
    public void Report(T value) { }
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
            loaded.SchemaVersion = Math.Max(loaded.SchemaVersion, 3);
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
            loaded.MusicPlaylist = MusicPlaylistRules.Normalize(loaded.MusicPlaylist);
            loaded.BuildProfiles ??= new List<BuildProfile>();
            loaded.OfflineProfiles ??= new List<OfflineProfile>();
            if (loaded.OfflineProfiles.Count == 0)
            {
                var profileName = string.IsNullOrWhiteSpace(loaded.Username) ? "NovaPlayer" : loaded.Username;
                loaded.OfflineProfiles.Add(new OfflineProfile { Username = profileName, Uuid = OfflineProfileRules.OfflineUuid(profileName) });
            }
            if (string.IsNullOrWhiteSpace(loaded.ActiveProfileId) || !loaded.OfflineProfiles.Any(x => x.Id == loaded.ActiveProfileId))
                loaded.ActiveProfileId = loaded.OfflineProfiles[0].Id;
            loaded.Username = loaded.OfflineProfiles.First(x => x.Id == loaded.ActiveProfileId).Username;
            foreach (var profile in loaded.OfflineProfiles)
            {
                if (!Guid.TryParse(profile.Uuid, out _))
                    profile.Uuid = OfflineProfileRules.OfflineUuid(profile.Username);
            }
            BuildProfileRules.EnsureProfiles(loaded);
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
            var defaults = new LauncherSettings();
            BuildProfileRules.EnsureProfiles(defaults);
            return defaults;
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
