using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security;
using System.Security.Cryptography;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.ProcessBuilder;

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
    public int MaximumRamMb { get; set; } = 3072;
    public bool FpsBoost { get; set; }
    public string LastVersion { get; set; } = "1.21.1";
    public string ModLoader { get; set; } = "fabric";
}

internal sealed class MainForm : Form
{
    readonly LauncherSettings settings;
    MinecraftLauncher launcher;
    readonly TextBox username = new();
    readonly ComboBox version = new();
    readonly NumericUpDown ram = new();
    readonly TextBox gameDir = new();
    readonly CheckBox boost = new();
    readonly RichTextBox log = new();
    readonly Label status = new();
    readonly Button play = new();
    readonly Button install = new();
    readonly ListView mods = new();
    readonly ComboBox modLoader = new();
    readonly HttpClient http = new(new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All });

    public MainForm()
    {
        settings = SettingsService.Load();
        Directory.CreateDirectory(settings.GameDirectory);
        launcher = new MinecraftLauncher(new MinecraftPath(settings.GameDirectory));

        Text = "NovaCraft 1.14";
        Width = 1180; Height = 760; MinimumSize = new Size(980, 650);
        BackColor = Color.FromArgb(18, 19, 23); ForeColor = Color.White;
        Font = new Font("Segoe UI", 10); StartPosition = FormStartPosition.CenterScreen;

        BuildUi();
        Shown += async (_, _) => await RefreshVersionsAsync();
    }

    void BuildUi()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 76, BackColor = Color.FromArgb(24, 26, 31) };
        header.Controls.Add(new Label { Text = "NOVACRAFT", Font = new Font("Segoe UI Semibold", 22, FontStyle.Bold), AutoSize = true, Location = new Point(26, 17) });
        header.Controls.Add(new Label { Text = "1.14 • Windows 10/11 • Minecraft Java", ForeColor = Color.Silver, AutoSize = true, Location = new Point(29, 49) });

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(HomePage()); tabs.TabPages.Add(VersionsPage()); tabs.TabPages.Add(ModsPage()); tabs.TabPages.Add(SettingsPage());

        status.Text = "Готов"; status.Dock = DockStyle.Bottom; status.Height = 32; status.Padding = new Padding(16, 7, 0, 0);
        status.ForeColor = Color.LightGray;
        Controls.Add(tabs); Controls.Add(header); Controls.Add(status);
    }

    TabPage HomePage()
    {
        var p = new TabPage("Главная") { BackColor = Color.FromArgb(18,19,23) };
        var left = new Panel { Dock = DockStyle.Left, Width = 420, Padding = new Padding(22) };
        var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(22) };
        left.Controls.Add(new Label { Text = "Запуск Minecraft", Font = new Font("Segoe UI Semibold", 20, FontStyle.Bold), AutoSize = true, Location = new Point(22, 20) });

        username.Text = settings.Username;
        left.Controls.Add(Labeled("Ник", username, 22, 70, 360));

        version.DropDownStyle = ComboBoxStyle.DropDownList;
        left.Controls.Add(Labeled("Версия", version, 22, 140, 360));

        ram.Minimum = 1024; ram.Maximum = 16384; ram.Increment = 512;
        ram.Value = Math.Clamp(settings.MaximumRamMb, 1024, 16384);
        left.Controls.Add(Labeled("Память игры (МБ)", ram, 22, 210, 360));

        boost.Text = "FPS Boost — JVM профиль"; boost.Checked = settings.FpsBoost;
        boost.AutoSize = true; boost.Location = new Point(26, 282); left.Controls.Add(boost);

        play.Text = "ИГРАТЬ"; play.Font = new Font("Segoe UI Semibold", 13, FontStyle.Bold);
        play.FlatStyle = FlatStyle.Flat; play.BackColor = Color.FromArgb(46, 160, 67); play.ForeColor = Color.White;
        play.SetBounds(22, 325, 360, 54); play.Click += async (_, _) => await LaunchAsync(); left.Controls.Add(play);

        install.Text = "Установить выбранную версию"; install.FlatStyle = FlatStyle.Flat;
        install.SetBounds(22, 392, 360, 44); install.Click += async (_, _) => await InstallAsync(); left.Controls.Add(install);

        left.Controls.Add(new Label {
            Text = "Нет скрытых служб, автозапуска и рекламных баннеров.\nФайлы Minecraft находятся только в выбранной папке.",
            ForeColor = Color.LightGray, AutoSize = true, Location = new Point(22, 465)
        });

        log.Dock = DockStyle.Fill; log.ReadOnly = true; log.BackColor = Color.FromArgb(11,12,15);
        log.ForeColor = Color.Gainsboro; log.Font = new Font("Consolas", 9); log.BorderStyle = BorderStyle.None;
        right.Controls.Add(log); p.Controls.Add(right); p.Controls.Add(left); return p;
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
        var p = new TabPage("Моды") { BackColor = Color.FromArgb(18,19,23) };
        var search = new TextBox { PlaceholderText = "Поиск модов Modrinth…", Width = 430, Location = new Point(22,18) };
        var btn = new Button { Text = "Найти", Location = new Point(465,17), Width = 90 };
        modLoader.DropDownStyle = ComboBoxStyle.DropDownList;
        modLoader.Items.AddRange(new object[] { "Fabric", "Forge", "NeoForge" });
        modLoader.SelectedItem = settings.ModLoader switch { "forge" => "Forge", "neoforge" => "NeoForge", _ => "Fabric" };
        modLoader.Location = new Point(565,17); modLoader.Width = 125;
        var get = new Button { Text = "Скачать выбранный", Location = new Point(700,17), Width = 160 };
        mods.SetBounds(22,62,1050,520); mods.View=View.Details; mods.FullRowSelect=true;
        mods.BackColor=Color.FromArgb(25,27,32); mods.ForeColor=Color.White;
        mods.Columns.Add("Название",300); mods.Columns.Add("Описание",600); mods.Columns.Add("Проект",120);
        btn.Click += async (_,_) => await SearchModsAsync(search.Text);
        get.Click += async (_,_) => await DownloadSelectedModAsync();
        modLoader.SelectedIndexChanged += (_,_) => {
            settings.ModLoader = modLoader.SelectedItem?.ToString() switch { "Forge" => "forge", "NeoForge" => "neoforge", _ => "fabric" };
            SettingsService.Save(settings);
        };
        p.Controls.AddRange(new Control[]{search,btn,modLoader,get,mods}); return p;
    }

    TabPage SettingsPage()
    {
        var p = new TabPage("Настройки") { BackColor = Color.FromArgb(18,19,23) };
        p.Controls.Add(new Label { Text="Папка Minecraft", AutoSize=true, Location=new Point(22,25) });
        gameDir.Text=settings.GameDirectory; gameDir.SetBounds(22,55,760,34);
        var browse=new Button{Text="Выбрать…",Location=new Point(795,55),Width=100};
        browse.Click += (_,_) => {
            using var d=new FolderBrowserDialog{Description="Выберите папку Minecraft",SelectedPath=gameDir.Text};
            if(d.ShowDialog()==DialogResult.OK) gameDir.Text=d.SelectedPath;
        };
        var save=new Button{Text="Сохранить",Location=new Point(22,110),Width=150,Height=40};
        save.Click += (_,_)=>SaveSettings();
        p.Controls.AddRange(new Control[]{gameDir,browse,save}); return p;
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
            var releases=all.Where(v=>string.Equals(v.Type.ToString(),"release",StringComparison.OrdinalIgnoreCase))
                .Select(v=>v.Name).Distinct().Take(100).ToList();
            version.Items.Clear(); foreach(var v in releases) version.Items.Add(v);
            version.SelectedItem = releases.Contains(settings.LastVersion) ? settings.LastVersion : releases.FirstOrDefault();
            if(version.Tag is ListBox list){list.Items.Clear();list.Items.AddRange(releases.ToArray());}
            WriteLog("Версий получено: "+releases.Count); status.Text="Готов";
        } catch(Exception ex){Fail("Не удалось получить список версий",ex);}
    }

    async Task InstallAsync()
    {
        if(version.SelectedItem is not string v)return;
        try{SetBusy(true,"Установка "+v+"…"); await launcher.InstallAsync(v); WriteLog("Установлено: "+v); status.Text="Установка завершена";}
        catch(Exception ex){Fail("Ошибка установки",ex);} finally{SetBusy(false);}
    }

    async Task LaunchAsync()
    {
        if(version.SelectedItem is not string v){MessageBox.Show("Выберите версию.");return;}
        if(string.IsNullOrWhiteSpace(username.Text)){MessageBox.Show("Введите ник.");return;}
        SaveSettings();
        try{
            SetBusy(true,"Подготовка "+v+"…"); await launcher.InstallAsync(v);
            var opt=new MLaunchOption{Session=MSession.CreateOfflineSession(username.Text.Trim()),MaximumRamMb=(int)ram.Value};
            if(boost.Checked) opt.ExtraJvmArguments=new[]{
                MArgument.FromCommandLine("-XX:+UseG1GC"),
                MArgument.FromCommandLine("-XX:+ParallelRefProcEnabled"),
                MArgument.FromCommandLine("-XX:MaxGCPauseMillis=100"),
                MArgument.FromCommandLine("-XX:+UnlockExperimentalVMOptions"),
                MArgument.FromCommandLine("-XX:+DisableExplicitGC")};
            var process=await launcher.BuildProcessAsync(v,opt); process.Start();
            WriteLog("Minecraft запущен: "+v); status.Text="Minecraft запущен";
        }catch(Exception ex){Fail("Minecraft не удалось запустить",ex);}finally{SetBusy(false);}
    }

    async Task SearchModsAsync(string query)
    {
        if(string.IsNullOrWhiteSpace(query))return;
        try{
            status.Text="Поиск Modrinth…";
            var url="https://api.modrinth.com/v2/search?query="+Uri.EscapeDataString(query)+"&facets="+Uri.EscapeDataString("[[\"project_type:mod\"]]")+"&limit=20";
            var root=JsonNode.Parse(await http.GetStringAsync(url));
            mods.Items.Clear();
            foreach(var hit in root?["hits"]?.AsArray() ?? new JsonArray()){
                var item=new ListViewItem(hit?["title"]?.ToString()??"Без названия");
                item.SubItems.Add(hit?["description"]?.ToString()??"");
                item.SubItems.Add(hit?["project_id"]?.ToString()??""); mods.Items.Add(item);
            }
            status.Text="Найдено: "+mods.Items.Count;
        }catch(Exception ex){Fail("Modrinth недоступен",ex);}
    }

    async Task DownloadSelectedModAsync()
    {
        if(mods.SelectedItems.Count==0)return;
        var project=mods.SelectedItems[0].SubItems[2].Text;
        try{
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
            var projectTitle=mods.SelectedItems[0].Text;
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
            var bytes=await response.Content.ReadAsByteArrayAsync();
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
        settings.Username=username.Text.Trim(); settings.MaximumRamMb=(int)ram.Value;
        settings.FpsBoost=boost.Checked; settings.GameDirectory=gameDir.Text.Trim();
        settings.LastVersion=version.SelectedItem?.ToString()??settings.LastVersion;
        Directory.CreateDirectory(settings.GameDirectory); launcher=new MinecraftLauncher(new MinecraftPath(settings.GameDirectory));
        SettingsService.Save(settings); status.Text="Настройки сохранены";
    }

    void SetBusy(bool busy,string? message=null){play.Enabled=!busy;install.Enabled=!busy;Cursor=busy?Cursors.WaitCursor:Cursors.Default;if(message!=null)status.Text=message;}
    void WriteLog(string text)=>log.AppendText("["+DateTime.Now.ToString("HH:mm:ss")+"] "+text+Environment.NewLine);
    void Fail(string message,Exception ex){WriteLog(message+": "+ex.Message);status.Text=message;MessageBox.Show(message+"\n\n"+ex.Message,"NovaCraft",MessageBoxButtons.OK,MessageBoxIcon.Error);}
}

static class SettingsService
{
    static readonly string PathName=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"NovaCraft","settings.json");
    public static LauncherSettings Load(){try{if(File.Exists(PathName))return JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(PathName))??new();}catch{}return new();}
    public static void Save(LauncherSettings s){Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);File.WriteAllText(PathName,JsonSerializer.Serialize(s,new JsonSerializerOptions{WriteIndented=true}));}
}
