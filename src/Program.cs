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

        private static readonly Random Rand = new();
        private static System.Threading.Timer? _timer;
        private static string TempPath => Const.WallpaperPicturePath;

        private static Config _config = new();
        private static List<Queue<string>> _queues = new();
        private static List<string?> _lastImages = new();

        private static Screen[]? _cachedScreens;
        private static readonly object _screenLock = new();

        private static FolderWatcher? _folderWatcher;
        private static readonly string[] ImageExts = [".jpg", ".jpeg", ".png", ".bmp"];

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
            
            SetupNotifyIcon();
            InitializeMonitorState();

            static void handleRequiredInitialize()
            {
                InitializeMonitorState();
                UpdateWallpaper(); ;
            }
            SystemEvents.DisplaySettingsChanged += (_, _) => Application.OpenForms[0]?.BeginInvoke(handleRequiredInitialize);
            _folderWatcher = new FolderWatcher(_config.Monitors.Select(m => m.Folder), handleRequiredInitialize);
            _timer = new System.Threading.Timer(_ => UpdateWallpaper(), null, 0, _config.IntervalSeconds * 1000);

            Application.Run();
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
                key.SetValue("WallpaperStyle", "22"); // 画像
                key.SetValue("TileWallpaper", "0"); // スパン
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
            var exitItem = new ToolStripMenuItem("終了(&X)");
            exitItem.Click += (s, e) => ApplicationShutdown();
            contextMenu.Items.Add(exitItem);
            _notifyIcon.ContextMenuStrip = contextMenu;
            _notifyIcon.Visible = true;
        }

        private static void InitializeMonitorState()
        {
            _queues.Clear();
            _lastImages.Clear();
            _cachedScreens = null;

            for (int i = 0; i < StableScreens.Length; i++)
            {
                string? folder = (i < _config.Monitors.Count) ? _config.Monitors[i].Folder : null;

                if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
                {
                    var files = Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
                        .Where(f => ImageExts.Contains(Path.GetExtension(f).ToLowerInvariant()))
                        .ToList();

                    _queues.Add(new Queue<string>(Shuffle(files)));
                }
                else
                {
                    _queues.Add(new Queue<string>());
                }

                _lastImages.Add(null);
            }
        }

        private static void UpdateWallpaper()
        {
            if (_paused) return;

            var screens = StableScreens;
            if (_queues.Count != screens.Length)
                InitializeMonitorState();

            string?[] monitorImages = new string?[screens.Length];

            for (int i = 0; i < screens.Length; i++)
            {
                if (_queues[i].Count == 0)
                {
                    string? folder = (i < _config.Monitors.Count) ? _config.Monitors[i].Folder : null;

                    if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
                    {
                        var files = Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
                            .Where(f => ImageExts.Contains(Path.GetExtension(f).ToLowerInvariant()))
                            .ToList();

                        var shuffled = Shuffle(files);
                        if (_lastImages[i] != null && shuffled.Count > 1 && shuffled[0] == _lastImages[i])
                        {
                            int swapIndex = Rand.Next(1, shuffled.Count);
                            (shuffled[0], shuffled[swapIndex]) = (shuffled[swapIndex], shuffled[0]);
                        }

                        _queues[i] = new Queue<string>(shuffled);
                    }
                }

                monitorImages[i] = _queues[i].Count > 0 ? _queues[i].Dequeue() : null;
                _lastImages[i] = monitorImages[i];
            }

            ComposeWallpaper(monitorImages, TempPath);
            SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, TempPath, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
            OverwriteWithBlack(TempPath);
        }

        private static void ComposeWallpaper(string?[] monitorImages, string path)
        {
            Rectangle virtualBounds = Rectangle.Empty;
            var screens = StableScreens;
            foreach (var screen in screens)
                virtualBounds = Rectangle.Union(virtualBounds, screen.Bounds);

            using var bmp = new Bitmap(virtualBounds.Width, virtualBounds.Height);
            using var gMain = Graphics.FromImage(bmp);
            gMain.FillRectangle(Brushes.Black, new Rectangle(0, 0, bmp.Width, bmp.Height));

            for (int i = 0; i < screens.Length; i++)
            {
                var screen = screens[i];
                var bounds = screen.Bounds;
                var drawRect = new Rectangle(bounds.Left - virtualBounds.Left, bounds.Top - virtualBounds.Top, bounds.Width, bounds.Height);

                if (monitorImages[i] == null)
                {
                    gMain.FillRectangle(Brushes.Black, drawRect);
                }
                else
                {
                    using var img = Image.FromFile(monitorImages[i]!);
                    var mode = (i < _config.Monitors.Count) ? _config.Monitors[i].Mode : null;
                    DrawImageWithMode(gMain, img, drawRect, mode ?? StretchMode.Fit);
                }
            }
            bmp.Save(path, ImageFormat.Jpeg);
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

        private static void OverwriteWithBlack(string targetPath)
        {
            string emptyPicPath = Const.EmptyPicturePath;
            if (!File.Exists(emptyPicPath))
            {
                using var bmp = new Bitmap(1, 1);
                using var g = Graphics.FromImage(bmp);
                g.FillRectangle(Brushes.Black, new Rectangle(0, 0, 1, 1));
                bmp.Save(emptyPicPath, ImageFormat.Jpeg);
            }
            File.Copy(emptyPicPath, targetPath, overwrite: true);
        }

        private static List<string> Shuffle(List<string> list)
        {
            return [.. list.OrderBy(_ => Rand.Next())];
        }

        private static void TogglePause()
        {
            if (_paused)
            {
                _timer!.Change(0, _config.IntervalSeconds * 1000);
                _paused = false;
                _notifyIcon?.Icon = _iconRunning;
                
            }
            else
            {
                SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, TempPath, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
                _timer!.Change(Timeout.Infinite, Timeout.Infinite);
                _paused = true;
                _notifyIcon?.Icon = _iconPaused;
            }
        }

        private static void ApplicationShutdown()
        {
            try { SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, TempPath, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE); } catch { }
            try { _folderWatcher?.Dispose(); } catch { }
            try { _notifyIcon?.Dispose();} catch { }
            try { Application.Exit(); } catch { }
        }

    }
}
