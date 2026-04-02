using System.Diagnostics;

namespace at365.WallpaperSlideshow
{
    public sealed class HistoryManager
    {
        private static readonly Lazy<HistoryManager> _lazy = new(() => new HistoryManager());
        public static HistoryManager Instance => _lazy.Value;

        private LinkedList<string>[] _history = Array.Empty<LinkedList<string>>();
        private Config? _config;

        private HistoryManager() { }

        public void SetConfig(Config config)
        {
            _config = config;
        }

        public void EnsureInitialized(Screen[] screens)
        {
            if (_history.Length == 0)
            {
                _history = new LinkedList<string>[screens.Length];
                for (int i = 0; i < screens.Length; i++)
                    _history[i] = new LinkedList<string>();
                return;
            }

            if (_history.Length < screens.Length)
            {
                var newHist = new LinkedList<string>[screens.Length];
                for (int i = 0; i < screens.Length; i++)
                {
                    if (i < _history.Length)
                        newHist[i] = _history[i];
                    else
                        newHist[i] = new LinkedList<string>();
                }
                _history = newHist;
            }
        }

        public void Push(int monitor, string path, int limit)
        {
            if (monitor < 0 || monitor >= _history.Length)
                return;

            var list = _history[monitor];
            list.AddFirst(path);

            if (list.Count > limit)
                list.RemoveLast();
        }

        public ToolStripMenuItem CreateHistoryMenu()
        {
            var root = new ToolStripMenuItem("最近使った壁紙(&R)");

            root.DropDownOpening += (_, _) =>
            {
                root.DropDownItems.Clear();

                var screens = StableScreensProvider.Screens;
                EnsureInitialized(screens);

                var cache = new Dictionary<string, (Image? img, string? size, string? res)>();

                for (int i = 0; i < _history.Length; i++)
                {
                    int monitorIndex = i;
                    var monItem = new ToolStripMenuItem($"{monitorIndex + 1}: ");
                    root.DropDownItems.Add(monItem);

                    monItem.DropDownOpening += (_, _) =>
                    {
                        monItem.DropDownItems.Clear();

                        var list = _history[monitorIndex];
                        if (list.Count == 0)
                        {
                            monItem.DropDownItems.Add("(なし)");
                            return;
                        }

                        foreach (var path in list)
                        {
                            var menuItem = CreateThumbnailMenuItem(path, cache);
                            monItem.DropDownItems.Add(menuItem);
                        }
                    };
                }
            };

            return root;
        }

        private ToolStripMenuItem CreateThumbnailMenuItem(
            string path,
            Dictionary<string, (Image? img, string? size, string? res)> cache)
        {
            if (_config == null)
                throw new InvalidOperationException("HistoryManager.SetConfig() が呼ばれていません");

            var thumbWidth = _config.History.ThumbnailWidth;
            var thumbHeight = _config.History.ThumbnailHeight;

            var panel = new Panel
            {
                Width = thumbWidth + 80,
                Height = thumbHeight + 60,
                Margin = new Padding(4)
            };

            var pb = new PictureBox
            {
                Width = thumbWidth,
                Height = thumbHeight,
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = null
            };

            var labelSize = new Label
            {
                AutoSize = false,
                Width = panel.Width,
                Height = 18,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.Gray
            };

            var labelRes = new Label
            {
                AutoSize = false,
                Width = panel.Width,
                Height = 18,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.Gray
            };

            panel.Controls.Add(pb);
            panel.Controls.Add(labelSize);
            panel.Controls.Add(labelRes);

            pb.Top = 0;
            pb.Left = (panel.Width - pb.Width) / 2;
            labelSize.Top = pb.Bottom + 4;
            labelRes.Top = labelSize.Bottom;

            var host = new ToolStripControlHost(panel);
            var parent = new ToolStripMenuItem(Truncate(Path.GetFileName(path), _config.History.MaxFileNameLength));
            parent.DropDownItems.Add(host);
            parent.DropDownDirection = ToolStripDropDownDirection.Left;

            var tt = new ToolTip();
            tt.SetToolTip(pb, path);
            tt.SetToolTip(labelSize, path);
            tt.SetToolTip(labelRes, path);
            tt.SetToolTip(panel, path);

            parent.DropDownOpened += (_, _) =>
            {
                if (!File.Exists(path))
                {
                    pb.Image = null;
                    labelSize.Text = "(ファイルが存在しません)";
                    labelRes.Text = "";
                    return;
                }

                if (!cache.TryGetValue(path, out var info))
                {
                    Image? img = null;
                    string sizeText = "";
                    string resText = "";

                    try { img = LoadImageWithoutLock(path); } catch { }
                    try
                    {
                        var fi = new FileInfo(path);
                        sizeText = $"{fi.Length / 1024f / 1024f:0.00} MB";
                    }
                    catch { }
                    try
                    {
                        using var tmp = LoadImageWithoutLock(path);
                        resText = $"{tmp.Width}×{tmp.Height}";
                    }
                    catch { }

                    info = (img, sizeText, resText);
                    cache[path] = info;
                }

                pb.Image = info.img;
                labelSize.Text = info.size;
                labelRes.Text = info.res;
            };

            parent.Click += (_, _) =>
            {
                if (File.Exists(path))
                    OpenImage(path);
            };

            host.Click += (_, _) => OpenImage(path);
            panel.Click += (_, _) => OpenImage(path);
            pb.Click += (_, _) => OpenImage(path);
            labelSize.Click += (_, _) => OpenImage(path);
            labelRes.Click += (_, _) => OpenImage(path);

            return parent;
        }

        private static void OpenImage(string path)
        {
            try
            {
                string normalized = path.Replace('/', '\\').Trim();

                while (normalized.Contains("\\\\"))
                    normalized = normalized.Replace("\\\\", "\\");

                if (normalized.StartsWith("\\") && !normalized.StartsWith("\\\\"))
                    normalized = "\\" + normalized;

                if (!File.Exists(normalized))
                {
                    MessageBox.Show(
                        $"ファイルが存在しません:\n{normalized}",
                        "エラー",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning
                    );
                    return;
                }

                Process.Start(new ProcessStartInfo(normalized)
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "ファイルを開けませんでした:\n" + ex.Message,
                    "エラー",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }

        private static Image LoadImageWithoutLock(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var ms = new MemoryStream();
            fs.CopyTo(ms);
            ms.Position = 0;
            return Image.FromStream(ms);
        }

        private static string Truncate(string text, int max)
        {
            if (text.Length <= max) return text;

            string ext = Path.GetExtension(text);
            string name = Path.GetFileNameWithoutExtension(text);

            int allowed = max - ext.Length - 3;
            if (allowed <= 0)
                return "..." + ext;

            return name.Substring(0, allowed) + "..." + ext;
        }
    }
}
