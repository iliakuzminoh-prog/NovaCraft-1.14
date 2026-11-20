using System.Diagnostics;
using System.Drawing;
using NovaCraft.Builds;
using NovaCraft.Services;

namespace NovaCraft;

internal static class BuildsPageFactory
{
    public static TabPage Create(
        LauncherSettings settings,
        ComboBox version,
        ComboBox gameLoader,
        Action saveSettings,
        Action<string> setStatus,
        Func<Task> launchAsync)
    {
        var p = new TabPage("Сборки")
        {
            BackColor = Color.FromArgb(13, 15, 25),
            Padding = new Padding(20),
            AutoScroll = true
        };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Color.Transparent
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var heading = new Label
        {
            Text = "Мои сборки",
            AutoSize = true,
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 26, FontStyle.Bold),
            Margin = new Padding(4, 0, 4, 4)
        };
        var description = new Label
        {
            Text = "Отдельные профили Minecraft, загрузчика, памяти и папки игры. Каждая сборка запускается из своей папки.",
            AutoSize = true,
            ForeColor = Color.FromArgb(151, 161, 183),
            Margin = new Padding(6, 0, 6, 14)
        };

        var store = new BuildProfileStore(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NovaCraft", "builds.json"));
        var instanceStore = new InstanceStore(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NovaCraft"));

        var builds = store.Load().ToList();
        MigrateLegacyProfiles();

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 0, 0, 12),
            BackColor = Color.Transparent
        };

        var name = new TextBox
        {
            PlaceholderText = "Название новой сборки",
            Width = 250,
            Height = 34,
            Margin = new Padding(0, 0, 8, 0)
        };
        var add = MakeButton("＋  Создать", SystemIcons.Application, 130);
        var favoriteOnly = new CheckBox
        {
            Text = "Только избранные",
            AutoSize = true,
            ForeColor = Color.Gainsboro,
            Margin = new Padding(8, 8, 0, 0)
        };

        toolbar.Controls.Add(name);
        toolbar.Controls.Add(add);
        toolbar.Controls.Add(favoriteOnly);

        var list = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 0, 8, 20),
            BackColor = Color.Transparent
        };

        root.Controls.Add(heading, 0, 0);
        root.Controls.Add(description, 0, 1);
        root.Controls.Add(toolbar, 0, 2);
        root.Controls.Add(list, 0, 3);
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        p.Controls.Add(root);

        void Persist()
        {
            store.Save(builds);
            saveSettings();
        }

        void LaunchBuild(BuildProfile build)
        {
            BuildProfileLaunch.Apply(settings, build);
            settings.ActiveBuildId = build.Id;
            Persist();
        }

        void Refresh()
        {
            list.SuspendLayout();
            list.Controls.Clear();

            var visible = builds
                .OrderByDescending(x => x.Favorite)
                .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
                .Where(x => !favoriteOnly.Checked || x.Favorite)
                .ToList();

            if (visible.Count == 0)
            {
                list.Controls.Add(new Label
                {
                    Text = favoriteOnly.Checked
                        ? "Избранных сборок пока нет."
                        : "Сборок пока нет. Создай первую выше.",
                    AutoSize = true,
                    ForeColor = Color.Silver,
                    Font = new Font("Segoe UI", 11),
                    Padding = new Padding(8, 18, 8, 18)
                });
            }

            foreach (var build in visible)
                list.Controls.Add(CreateCard(build));

            list.ResumeLayout();
        }

        Control CreateCard(BuildProfile build)
        {
            var card = new Panel
            {
                Width = 360,
                Height = 190,
                Margin = new Padding(0, 0, 12, 12),
                BackColor = Color.FromArgb(25, 28, 45),
                Padding = new Padding(14)
            };

            var header = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 42,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.Transparent
            };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var title = new Label
            {
                Text = build.Name,
                Dock = DockStyle.Fill,
                ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 12, FontStyle.Bold),
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft
            };
            var star = MakeButton(build.Favorite ? "★" : "☆", SystemIcons.Asterisk, 40);
            star.Click += (_, _) =>
            {
                build.Favorite = !build.Favorite;
                Persist();
                Refresh();
            };

            header.Controls.Add(title, 0, 0);
            header.Controls.Add(star, 1, 0);

            var info = new Label
            {
                Text = $"Minecraft {build.MinecraftVersion}  •  {build.DisplayLoader}\nОЗУ: {build.MaximumRamMb} МБ  •  FPS Boost: {(build.FpsBoost ? "вкл." : "выкл.")}\n{build.GameDirectory}",
                Dock = DockStyle.Top,
                Height = 68,
                ForeColor = Color.FromArgb(166, 176, 198),
                AutoEllipsis = true
            };

            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 44,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = Color.Transparent
            };

            var launch = MakeButton("▶  Запустить", SystemIcons.Application, 118);
            launch.Click += async (_, _) =>
            {
                try
                {
                    LaunchBuild(build);
                    setStatus("Запуск сборки «" + build.Name + "»…");
                    await launchAsync();
                }
                catch (Exception ex)
                {
                    setStatus("Не удалось запустить сборку: " + ex.Message);
                }
            };

            var edit = MakeButton("Настроить", SystemIcons.Information, 96);
            edit.Click += (_, _) =>
            {
                if (EditBuild(build))
                {
                    Persist();
                    Refresh();
                }
            };

            var folder = MakeButton("Папка", SystemIcons.WinLogo, 76);
            folder.Click += (_, _) =>
            {
                try
                {
                    Directory.CreateDirectory(build.GameDirectory);
                    Process.Start(new ProcessStartInfo("explorer.exe", """ + build.GameDirectory + """)
                    {
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    setStatus("Не удалось открыть папку: " + ex.Message);
                }
            };

            var duplicate = MakeButton("Копия", SystemIcons.Asterisk, 76);
            duplicate.Click += (_, _) =>
            {
                try
                {
                    var copy = store.Duplicate(builds, build.Id);
                    var copyInstance = instanceStore.Create(copy.Name, copy.MinecraftVersion, copy.Loader);
                    copy.Id = copyInstance.Id;
                    copy.GameDirectory = instanceStore.GameDir(copyInstance);
                    Persist();
                    Refresh();
                }
                catch (Exception ex)
                {
                    setStatus("Не удалось создать копию: " + ex.Message);
                }
            };

            var remove = MakeButton("Удалить", SystemIcons.Warning, 76);
            remove.Click += (_, _) =>
            {
                if (MessageBox.Show(
                    "Удалить профиль «" + build.Name + "»? Файлы игры в его папке останутся.",
                    "Удаление сборки",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) != DialogResult.Yes)
                    return;

                store.Remove(builds, build.Id);
                Persist();
                Refresh();
            };

            actions.Controls.AddRange(new Control[] { launch, edit, folder, duplicate, remove });
            card.Controls.Add(actions);
            card.Controls.Add(info);
            card.Controls.Add(header);
            return card;
        }

        bool EditBuild(BuildProfile build)
        {
            using var dialog = new Form
            {
                Text = "Настройки сборки",
                Width = 500,
                Height = 330,
                StartPosition = FormStartPosition.CenterParent,
                BackColor = Color.FromArgb(18, 20, 31),
                ForeColor = Color.White,
                MinimizeBox = false,
                MaximizeBox = false
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(18),
                ColumnCount = 2,
                RowCount = 5
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            var buildName = new TextBox { Text = build.Name, Dock = DockStyle.Fill };
            var ram = new NumericUpDown { Minimum = 512, Maximum = 32768, Increment = 256, Value = Math.Clamp(build.MaximumRamMb, 512, 32768), Dock = DockStyle.Fill };
            var fps = new CheckBox { Text = "Включить FPS Boost", Checked = build.FpsBoost, AutoSize = true };
            var path = new TextBox { Text = build.GameDirectory, ReadOnly = true, Dock = DockStyle.Fill };
            var ok = new Button { Text = "Сохранить", DialogResult = DialogResult.OK, AutoSize = true };
            var cancel = new Button { Text = "Отмена", DialogResult = DialogResult.Cancel, AutoSize = true };

            layout.Controls.Add(new Label { Text = "Название", AutoSize = true }, 0, 0);
            layout.Controls.Add(buildName, 1, 0);
            layout.Controls.Add(new Label { Text = "ОЗУ, МБ", AutoSize = true }, 0, 1);
            layout.Controls.Add(ram, 1, 1);
            layout.Controls.Add(new Label { Text = "FPS", AutoSize = true }, 0, 2);
            layout.Controls.Add(fps, 1, 2);
            layout.Controls.Add(new Label { Text = "Папка", AutoSize = true }, 0, 3);
            layout.Controls.Add(path, 1, 3);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            layout.Controls.Add(buttons, 1, 4);
            dialog.Controls.Add(layout);
            dialog.AcceptButton = ok;
            dialog.CancelButton = cancel;

            if (dialog.ShowDialog(p.FindForm()) != DialogResult.OK)
                return false;

            if (string.IsNullOrWhiteSpace(buildName.Text))
            {
                MessageBox.Show("Название не может быть пустым.", "NovaCraft", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            build.Name = buildName.Text.Trim();
            build.MaximumRamMb = (int)ram.Value;
            build.FpsBoost = fps.Checked;
            return true;
        }

        void MigrateLegacyProfiles()
        {
            if (builds.Count > 0 || settings.SavedBuilds.Count == 0)
                return;

            foreach (var saved in settings.SavedBuilds)
            {
                var parts = saved.Split('|');
                var buildName = parts.Length >= 3 ? parts[0].Trim() : saved.Trim();
                var buildVersion = parts.Length >= 3 ? parts[1].Trim() : settings.LastVersion;
                var loader = parts.Length >= 3 ? parts[2].Trim() : settings.ModLoader;
                if (string.IsNullOrWhiteSpace(buildName) || string.IsNullOrWhiteSpace(buildVersion))
                    continue;

                var normalizedLoader = LoaderIds.Normalize(loader);
                var instance = instanceStore.Create(buildName, buildVersion, normalizedLoader);
                builds.Add(new BuildProfile
                {
                    Name = buildName,
                    MinecraftVersion = buildVersion,
                    Loader = normalizedLoader,
                    GameDirectory = instanceStore.GameDir(instance),
                    MaximumRamMb = settings.MaximumRamMb,
                    FpsBoost = settings.FpsBoost
                });
            }

            settings.SavedBuilds.Clear();
            store.Save(builds);
            saveSettings();
        }

        add.Click += (_, _) =>
        {
            try
            {
                var selectedVersion = version.SelectedItem?.ToString() ?? settings.LastVersion;
                var selectedLoader = LoaderIds.Normalize(gameLoader.SelectedItem?.ToString() ?? settings.ModLoader);
                if (string.IsNullOrWhiteSpace(selectedVersion))
                {
                    setStatus("Сначала выбери версию Minecraft.");
                    return;
                }

                var buildName = string.IsNullOrWhiteSpace(name.Text)
                    ? "Моя сборка " + (builds.Count + 1)
                    : name.Text.Trim();

                var instance = instanceStore.Create(buildName, selectedVersion, selectedLoader);
                var build = new BuildProfile
                {
                    Name = buildName,
                    MinecraftVersion = selectedVersion,
                    Loader = selectedLoader,
                    GameDirectory = instanceStore.GameDir(instance),
                    MaximumRamMb = settings.MaximumRamMb,
                    FpsBoost = settings.FpsBoost
                };

                build.Id = instance.Id;
                store.Add(builds, build);
                Persist();
                name.Clear();
                Refresh();
                setStatus("Сборка создана: " + buildName);
            }
            catch (Exception ex)
            {
                setStatus("Не удалось создать сборку: " + ex.Message);
            }
        };

        favoriteOnly.CheckedChanged += (_, _) => Refresh();
        Refresh();
        return p;
    }

    static Button MakeButton(string text, Icon? icon, int width)
    {
        var button = new Button
        {
            Text = text,
            Width = width,
            Height = 34,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(38, 42, 57),
            ForeColor = Color.White,
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, 6, 0),
            TextImageRelation = TextImageRelation.ImageBeforeText,
            ImageAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(7, 0, 7, 0)
        };
        button.FlatAppearance.BorderSize = 0;
        if (icon is not null)
            button.Image = icon.ToBitmap();
        return button;
    }
}
