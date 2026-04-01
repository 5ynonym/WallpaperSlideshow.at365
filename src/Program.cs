using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace at365.WallpaperSlideshow
{
    public static class Program
    {
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, string pvParam, uint fWinIni);

        private const uint SPI_SETDESKWALLPAPER = 0x0014;
        private const uint SPIF_UPDATEINIFILE = 0x01;
        private const uint SPIF_SENDCHANGE = 0x02;

        private static bool _paused = false;
        private static NotifyIcon? _notifyIcon;
        private static Icon? _iconRunning;
        private static Icon? _iconPaused;

        private static System.Threading.Timer? _timer;

        private static Config _config = new();
        private static List<Queue<string>> _queues = new();
        private static List<string?> _lastImages = new();

        private static List<string>[] _historyPerMonitor = Array.Empty<List<string>>();

        private static Screen[]? _cachedScreens;
        private static readonly object _screenLock = new();

        private static FolderWatcher? _folderWatcher;
        private static readonly string[] ImageExts = [".jpg", ".jpeg", ".png", ".bmp"];

        private static Rectangle[]? _lastMonitorBounds;
        public static Screen[] StableScreens => _cachedScreens ??= Screen.AllScreens.OrderBy(s => s.Bounds.Left).ThenBy(s => s.Bounds.Top).ToArray();

        [STAThread]
        public static void Main(string[] args)
        {
            _config = Config.LoadConfig();
            if (_config == null) return;

            EnsureSingleInstance();
            SetWallpaperSpanMode();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            InitializeApplication();
            SetupNotifyIcon();

            SystemEvents.DisplaySettingsChanged += (_, _) => InitializeApplication();
            SystemEvents.SessionSwitch += OnSessionSwitch;

            _folderWatcher = new FolderWatcher(_config.Monitors.Select(m => m.Folder), () => InitializeApplication(true));
            _timer = new System.Threading.Timer(_ => UpdateWallpaper(), null, _config.IntervalSeconds * 1000, _config.IntervalSeconds * 1000);

            Application.Run();

            static void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
            {
                switch (e.Reason)
                {
                    case SessionSwitchReason.SessionLock:
                    case SessionSwitchReason.RemoteDisconnect:
                        TogglePause(true);
                        break;

                    case SessionSwitchReason.SessionUnlock:
                    case SessionSwitchReason.RemoteConnect:
                        TogglePause(false);
                        break;
                }
            }
        }

        private static void EnsureSingleInstance()
        {
            var current = Process.GetCurrentProcess();
            var processes = Process.GetProcessesByName(current.ProcessName);
            foreach (var p in processes)
            {
                if (p.Id != current.Id)
                {
                    try { p.Kill(); } catch { }
                }
            }
        }

        private static void SetWallpaperSpanMode()
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\\Desktop", true);
            if (key != null)
            {
                key.SetValue("WallpaperStyle", "22");
                key.SetValue("TileWallpaper", "0");
            }
        }

        private static void SetupNotifyIcon()
        {
            _iconRunning = new Icon("running.ico");
            _iconPaused = new Icon("paused.ico");
            _notifyIcon = new NotifyIcon
            {
                Icon = _iconRunning,
                Text = "WallpaperSlideshow.at365",
                Visible = true
            };
            _notifyIcon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                    TogglePause();
            };

            var contextMenu = new ContextMenuStrip();

            var openDataFolderItem = new ToolStripMenuItem("データフォルダを開く(&D)");
            openDataFolderItem.Click += (_, _) => OpenDataFolder();
            contextMenu.Items.Add(openDataFolderItem);

            var historyRoot = new ToolStripMenuItem("最近使った壁紙(&R)");
            contextMenu.Items.Add(historyRoot);

            historyRoot.DropDownOpening += (_, _) =>
            {
                historyRoot.DropDownItems.Clear();

                if (_historyPerMonitor == null ||
                    _historyPerMonitor.Length != StableScreens.Length)
                {
                    _historyPerMonitor = new List<string>[StableScreens.Length];
                    for (int j = 0; j < _historyPerMonitor.Length; j++)
                        _historyPerMonitor[j] = new List<string>();
                }

                var thumbnailCache = new Dictionary<string, (Image? img, string? size, string? res)>();
                for (int i = 0; i < _historyPerMonitor.Length; i++)
                {
                    int monitorIndex = i;

                    var monItem = new ToolStripMenuItem($"{monitorIndex + 1}: ");
                    historyRoot.DropDownItems.Add(monItem);

                    monItem.DropDownOpening += (_, _) =>
                    {
                        monItem.DropDownItems.Clear();

                        if (monitorIndex < 0 || monitorIndex >= _historyPerMonitor.Length)
                        {
                            monItem.DropDownItems.Add("(なし)");
                            return;
                        }

                        var list = _historyPerMonitor[monitorIndex];
                        if (list == null || list.Count == 0)
                        {
                            monItem.DropDownItems.Add("(なし)");
                            return;
                        }

                        foreach (var path in list)
                        {
                            var menuItem = CreateThumbnailMenuItem(path, thumbnailCache);
                            monItem.DropDownItems.Add(menuItem);
                        }
                    };
                }
            };

            var exitItem = new ToolStripMenuItem("終了(&X)");
            exitItem.Click += (_, _) => ApplicationShutdown();
            contextMenu.Items.Add(exitItem);
            _notifyIcon.ContextMenuStrip = contextMenu;
            _notifyIcon.Visible = true;
        }

        private static void InitializeMonitorState()
        {
            _queues.Clear();
            _lastImages.Clear();
            _cachedScreens = null;

            var screens = StableScreens;
            if (_historyPerMonitor == null)
            {
                _historyPerMonitor = new List<string>[screens.Length];
                for (int i = 0; i < screens.Length; i++)
                    _historyPerMonitor[i] = new List<string>();
            }
            else if (_historyPerMonitor.Length < screens.Length)
            {
                var newHist = new List<string>[screens.Length];
                for (int i = 0; i < screens.Length; i++)
                {
                    if (i < _historyPerMonitor.Length)
                        newHist[i] = _historyPerMonitor[i];
                    else
                        newHist[i] = new List<string>();
                }
                _historyPerMonitor = newHist;
            }

            while (_queues.Count < screens.Length)
            {
                _queues.Add(BuildQueueForMonitor(_queues.Count));
            }

            while (_lastImages.Count < screens.Length)
            {
                _lastImages.Add(null);
            }
        }

        internal static void InitializeApplication(bool forceInitialize = false)
        {
            bool monitorChanged = HasMonitorConfigChanged();
            if (!monitorChanged && !forceInitialize)
                return;

            InitializeMonitorState();
            UpdateWallpaper();
        }

        private static void PushHistory(int monitor, string imagePath)
        {
            var list = _historyPerMonitor[monitor];
            list.Insert(0, imagePath);

            if (list.Count > _config.History.Limit)
                list.RemoveAt(list.Count - 1);
        }

        private static void UpdateWallpaper()
        {
            if (_paused) return;

            var screens = StableScreens;
            if (_queues.Count != screens.Length)
                InitializeMonitorState();

            string?[] monitorImages = new string?[screens.Length];

            Rectangle virtualBounds = Rectangle.Empty;
            for (int i = 0; i < screens.Length; i++)
            {
                if (_queues[i].Count == 0)
                {
                    _queues[i] = BuildQueueForMonitor(i);
                    if (_lastImages[i] != null &&
                        _queues[i].Count > 1 &&
                        _queues[i].Peek() == _lastImages[i])
                    {
                        var arr = _queues[i].ToArray();
                        int swapIndex = Random.Shared.Next(1, arr.Length);
                        (arr[0], arr[swapIndex]) = (arr[swapIndex], arr[0]);
                        _queues[i] = new Queue<string>(arr);
                    }
                }

                var image = _queues[i].Count > 0 ? _queues[i].Dequeue() : null;
                _lastImages[i] = monitorImages[i] = image;
                virtualBounds = Rectangle.Union(virtualBounds, screens[i].Bounds);
            }

            using var bmp = new Bitmap(virtualBounds.Width, virtualBounds.Height);
            using var gMain = Graphics.FromImage(bmp);
            gMain.FillRectangle(Brushes.Black, new Rectangle(0, 0, bmp.Width, bmp.Height));

            for (int i = 0; i < screens.Length; i++)
            {
                ComposeWallpaperForMonitor(i, monitorImages[i], gMain, virtualBounds);
            }

            try { bmp.Save(Const.WallpaperPicturePath, ImageFormat.Bmp); } catch { }

            ApplyWallpaper();
            OverwriteWithBlack(Const.WallpaperPicturePath);
        }

        private static void ComposeWallpaperForMonitor(int monitorIndex, string? monitorImage, Graphics gMain, Rectangle virtualBounds)
        {
            var screens = StableScreens;
            if (monitorIndex < 0 || monitorIndex >= screens.Length)
            {
                return;
            }

            var monitorConfig = (monitorIndex < _config.Monitors.Count)
                ? _config.Monitors[monitorIndex]
                : new MonitorConfig();

            var screen = screens[monitorIndex];
            var bounds = screen.Bounds;
            var drawRect = new Rectangle(
                bounds.Left - virtualBounds.Left + monitorConfig.PaddingLeft,
                bounds.Top - virtualBounds.Top + monitorConfig.PaddingTop,
                bounds.Width - monitorConfig.PaddingLeft - monitorConfig.PaddingRight,
                bounds.Height - monitorConfig.PaddingTop - monitorConfig.PaddingBottom
            );

            var mode = monitorConfig.Mode ?? StretchMode.Fit;
            if (mode == StretchMode.Tile)
            {
                if (monitorImage == null)
                {
                    gMain.FillRectangle(Brushes.Black, drawRect);
                    return;
                }

                var paths = new List<string> { monitorImage };
                PushHistory(monitorIndex, monitorImage);

                while (paths.Count < monitorConfig.TileCount && _queues[monitorIndex].Count > 0)
                {
                    var next = _queues[monitorIndex].Dequeue();
                    if (next != null)
                    {
                        paths.Add(next);
                        PushHistory(monitorIndex, next);
                    }
                }

                var imgs = paths.Select(p => LoadImageWithoutLock(p)).ToArray();
                DrawTile(gMain, imgs, drawRect);

                foreach (var im in imgs)
                    im.Dispose();

                return;
            }
            else
            {

                if (monitorImage == null)
                {
                    gMain.FillRectangle(Brushes.Black, drawRect);
                    return;
                }

                try
                {
                    using var img = LoadImageWithoutLock(monitorImage);
                    DrawImageWithMode(gMain, img, drawRect, mode);
                    PushHistory(monitorIndex, monitorImage);
                }
                catch
                {
                    gMain.FillRectangle(Brushes.Black, drawRect);
                }
            }
        }

        private static Queue<string> BuildQueueForMonitor(int index)
        {
            string? folder = (index < _config.Monitors.Count) ? _config.Monitors[index].Folder : null;
            List<string> files = new();
            if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
            {
                try
                {
                    files = Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
                        .Where(f => ImageExts.Contains(Path.GetExtension(f).ToLowerInvariant()))
                        .ToList();
                }
                catch { }
            }

            return new Queue<string>(Shuffle(files));
        }

        private static void DrawImageWithMode(Graphics g, Image img, Rectangle drawRect, StretchMode mode)
        {
            switch (mode)
            {
                case StretchMode.Fill:
                    {
                        float scale = Math.Max(
                            (float)drawRect.Width / img.Width,
                            (float)drawRect.Height / img.Height
                        );
                        int w = (int)(img.Width * scale);
                        int h = (int)(img.Height * scale);
                        int x = drawRect.Left + (drawRect.Width - w) / 2;
                        int y = drawRect.Top + (drawRect.Height - h) / 2;
                        g.DrawImage(img, new Rectangle(x, y, w, h));
                        break;
                    }

                case StretchMode.Fit:
                    {
                        float scale = Math.Min(
                            (float)drawRect.Width / img.Width,
                            (float)drawRect.Height / img.Height
                        );
                        int w = (int)(img.Width * scale);
                        int h = (int)(img.Height * scale);
                        int x = drawRect.Left + (drawRect.Width - w) / 2;
                        int y = drawRect.Top + (drawRect.Height - h) / 2;
                        g.DrawImage(img, new Rectangle(x, y, w, h));
                        break;
                    }

                case StretchMode.Stretch:
                    {
                        g.DrawImage(img, drawRect);
                        break;
                    }

                case StretchMode.Center:
                    {
                        int x = drawRect.Left + (drawRect.Width - img.Width) / 2;
                        int y = drawRect.Top + (drawRect.Height - img.Height) / 2;
                        g.DrawImage(img, new Rectangle(x, y, img.Width, img.Height));
                        break;
                    }
            }
        }
        private const float VisualMargin = 5f; // 左右上下に 5px ずつ → 画像間は実質 10px

        private static void DrawTile(Graphics g, Image[] images, Rectangle rect)
        {
            int bestRows = -1;
            float bestScore = float.MaxValue;
            Image[][] bestSplit = null;
            float[] bestHeights = null;

            for (int rowCount = 1; rowCount <= images.Length; rowCount++)
            {
                var rows = SplitRowsAspectBalanced(images, rowCount);

                float[] heights = new float[rowCount];
                bool ok = true;

                for (int i = 0; i < rowCount; i++)
                {
                    heights[i] = CalcRowHeightNoOverlap(rows[i], rect.Width);
                    if (heights[i] <= 0)
                    {
                        ok = false;
                        break;
                    }
                }

                if (!ok)
                    continue;

                float totalHeight = heights.Sum();
                if (totalHeight > rect.Height)
                {
                    float scale = rect.Height / totalHeight;
                    for (int i = 0; i < rowCount; i++)
                        heights[i] *= scale;

                    totalHeight = heights.Sum();
                }

                float unusedHeight = rect.Height - totalHeight;
                float unusedWidth = 0;
                for (int i = 0; i < rowCount; i++)
                {
                    float rowWidth = rows[i].Sum(im => (im.Width / (float)im.Height) * heights[i]);
                    unusedWidth += Math.Max(0, rect.Width - rowWidth);
                }

                float score = unusedHeight + unusedWidth;

                // 1 行はペナルティ
                if (rowCount == 1)
                    score *= 5f;

                if (score < bestScore)
                {
                    bestScore = score;
                    bestRows = rowCount;
                    bestSplit = rows;
                    bestHeights = heights;
                }
            }

            if (bestRows < 0)
                return;

            for (int r = 0; r < bestRows; r++)
            {
                var arr = bestSplit[r].OrderBy(_ => Random.Shared.Next()).ToArray();
                bestSplit[r] = arr;
            }

            float totalHeightFinal = bestHeights.Sum();
            float gapY = (rect.Height - totalHeightFinal) / (bestRows + 1);
            if (gapY < 0) gapY = 0;

            float y = rect.Top + gapY;

            for (int i = 0; i < bestRows; i++)
            {
                DrawRowNoOverlap_WithVisualMargin(g, bestSplit[i], rect, bestHeights[i], y);
                y += bestHeights[i] + gapY;
            }
        }

        private static Image[][] SplitRowsAspectBalanced(Image[] images, int rows)
        {
            var result = new List<Image>[rows];
            for (int i = 0; i < rows; i++)
                result[i] = new List<Image>();

            float[] rowAspect = new float[rows];

            foreach (var img in images.OrderByDescending(i => (float)i.Width / i.Height))
            {
                int target = Array.IndexOf(rowAspect, rowAspect.Min());
                result[target].Add(img);
                rowAspect[target] += (float)img.Width / img.Height;
            }

            return result.Select(r => r.ToArray()).ToArray();
        }

        private static float CalcRowHeightNoOverlap(Image[] row, int totalWidth)
        {
            float sumAspect = row.Sum(im => (float)im.Width / im.Height);
            float h = totalWidth / sumAspect;
            return AdjustHeight(row, totalWidth, h);
        }

        private static float AdjustHeight(Image[] row, int totalWidth, float h)
        {
            float width = 0;
            foreach (var img in row)
                width += (img.Width / (float)img.Height) * h;

            if (width <= totalWidth)
                return h;

            if (h < 1)
                return -1;

            return AdjustHeight(row, totalWidth, h * 0.95f);
        }

        private static void DrawRowNoOverlap_WithVisualMargin(Graphics g, Image[] row, Rectangle rect, float rowHeight, float y)
        {
            float[] widths = row.Select(im => (float)im.Width / im.Height * rowHeight).ToArray();
            float total = widths.Sum();
            float gapX = (rect.Width - total) / (row.Length + 1);
            if (gapX < 0) gapX = 0;

            float x = rect.Left + gapX;

            for (int i = 0; i < row.Length; i++)
            {
                float w = widths[i];
                var layoutRect = new RectangleF(x, y, w, rowHeight);
                var inner = RectangleF.Inflate(layoutRect, -VisualMargin, -VisualMargin);

                DrawImageFit(g, row[i], Rectangle.Round(inner));
                x += w + gapX;
            }
        }

        private static void DrawImageFit(Graphics g, Image img, Rectangle rect)
        {
            float s = Math.Min((float)rect.Width / img.Width, (float)rect.Height / img.Height);
            int w = (int)(img.Width * s);
            int h = (int)(img.Height * s);
            int x = rect.Left + (rect.Width - w) / 2;
            int y = rect.Top + (rect.Height - h) / 2;
            g.DrawImage(img, new Rectangle(x, y, w, h));
        }

        private static void OverwriteWithBlack(string targetPath)
        {
            try
            {
                using var bmp = new Bitmap(1, 1);
                using var g = Graphics.FromImage(bmp);
                g.FillRectangle(Brushes.Black, new Rectangle(0, 0, 1, 1));

                bmp.Save(targetPath, ImageFormat.Bmp);
            }
            catch { }
        }

        private static List<string> Shuffle(List<string> list)
        {
            return list.OrderBy(_ => Random.Shared.Next()).ToList();
        }

        internal static void TogglePause(bool? forceState = null)
        {
            bool target = forceState ?? !_paused;

            if (!target)
            {
                _timer!.Change(0, _config.IntervalSeconds * 1000);
                _paused = false;
                _notifyIcon?.Icon = _iconRunning;
            }
            else
            {
                _timer!.Change(Timeout.Infinite, Timeout.Infinite);
                _paused = true;
                _notifyIcon?.Icon = _iconPaused;
                ApplyWallpaper();
            }
        }

        private static void OpenDataFolder()
        {
            try
            {
                string dataPath = Const.AppDataFolder;
                if (!Directory.Exists(Const.AppDataFolder))
                    Directory.CreateDirectory(Const.AppDataFolder);

                Process.Start("explorer.exe", Const.AppDataFolder);
            }
            catch { }
        }

        internal static void ApplicationShutdown()
        {
            try { ApplyWallpaper(); } catch { }
            try { _folderWatcher?.Dispose(); } catch { }
            try { _notifyIcon?.Dispose(); } catch { }
            try { Application.Exit(); } catch { }
        }

        private static void ApplyWallpaper()
        {
            try
            {
                SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, Const.WallpaperPicturePath, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
            }
            catch { }
        }

        private static bool HasMonitorConfigChanged()
        {
            var screens = Screen.AllScreens.OrderBy(s => s.Bounds.Left).ThenBy(s => s.Bounds.Top).ToArray();
            var bounds = screens.Select(s => s.Bounds).ToArray();

            if (_lastMonitorBounds == null ||
                _lastMonitorBounds.Length != bounds.Length)
            {
                _lastMonitorBounds = bounds;
                return true;
            }

            for (int i = 0; i < bounds.Length; i++)
            {
                if (!_lastMonitorBounds[i].Equals(bounds[i]))
                {
                    _lastMonitorBounds = bounds;
                    return true;
                }
            }

            return false;
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

        private static ToolStripMenuItem CreateThumbnailMenuItem(
            string path,
            Dictionary<string, (Image? img, string? size, string? res)> cache)
        {
            var thumbWidth = _config.History.ThumbnailWidth;
            var thumbheight = _config.History.ThumbnailHeight;

            var panel = new Panel
            {
                Width = thumbWidth + 80,
                Height = thumbheight + 60,
                Margin = new Padding(4)
            };

            var pb = new PictureBox
            {
                Width = thumbWidth,
                Height = thumbheight,
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = null
            };

            var labelSize = new Label
            {
                Text = "",
                AutoSize = false,
                Width = panel.Width,
                Height = 18,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.Gray
            };

            var labelRes = new Label
            {
                Text = "",
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
            if (!File.Exists(path)) return;

            try
            {
                string normalized = path.Replace('/', '\\').Replace("\\\\", "\\").Trim();
                if (normalized.StartsWith("\\") && !normalized.StartsWith("\\\\"))
                    normalized = "\\" + normalized;

                if (!File.Exists(normalized))
                {
                    MessageBox.Show("ファイルが存在しません:\n" + normalized,
                        "エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                Process.Start(new ProcessStartInfo(normalized)
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("ファイルを開けませんでした:\n" + ex.Message,
                    "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
    }
}
