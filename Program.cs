using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace WallpaperSlideshow365
{
    public class MonitorConfig
    {
        public string? Folder { get; set; }
    }

    public class Config
    {
        public int IntervalSeconds { get; set; } = 60;
        public List<MonitorConfig> Monitors { get; set; } = new();
    }

    public static class Program
    {
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SystemParametersInfo(
            uint uiAction, uint uiParam, string pvParam, uint fWinIni);

        private const uint SPI_SETDESKWALLPAPER = 0x0014;
        private const uint SPIF_UPDATEINIFILE = 0x01;
        private const uint SPIF_SENDCHANGE = 0x02;

        private static bool _paused = false;
        private static NotifyIcon? _notifyIcon;
        private static Icon? _iconRunning;
        private static Icon? _iconPaused;

        private static readonly Random Rand = new();
        private static System.Threading.Timer? _timer;
        private static string TempPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "merged_wallpaper.jpg");

        private static Config _config = new();
        private static List<Queue<string>> _queues = new();
        private static List<string?> _lastImages = new();

        private static void SetWallpaperSpanMode()
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\\Desktop", true);
            if (key != null)
            {
                key.SetValue("WallpaperStyle", "22"); // 画像
                key.SetValue("TileWallpaper", "0"); // スパン
            }
        }

        [STAThread]
        public static void Main(string[] args)
        {
            SetWallpaperSpanMode();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            _iconRunning = new Icon("running.ico");
            _iconPaused = new Icon("paused.ico");
            _notifyIcon = new NotifyIcon
            {
                Icon = _iconRunning,
                Text = "WallpaperSlideshow@365",
                Visible = true
            };
            _notifyIcon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                    TogglePause();
            };

            var contextMenu = new ContextMenuStrip();
            var exitItem = new ToolStripMenuItem("終了(&X)");
            exitItem.Click += (s, e) => Application.Exit();
            contextMenu.Items.Add(exitItem);
            _notifyIcon.ContextMenuStrip = contextMenu;
            _notifyIcon.Visible = true;

            string configPath = args.Length > 0 ? args[0] : "config.json";
            if (!File.Exists(configPath))
            {
                MessageBox.Show($"設定ファイルが見つかりません: {configPath}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _notifyIcon.Dispose();
                return;
            }

            try
            {
                var options = new JsonSerializerOptions { AllowTrailingCommas = true };
                _config = JsonSerializer.Deserialize<Config>(File.ReadAllText(configPath), options) ?? new Config();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"設定ファイルの読み込みに失敗しました: {ex.Message}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _notifyIcon.Dispose();
                return;
            }

            string[] exts = { ".jpg", ".jpeg", ".png", ".bmp" };
            _queues.Clear();
            _lastImages.Clear();
            for (int i = 0; i < Screen.AllScreens.Length; i++)
            {
                if (i < _config.Monitors.Count && !string.IsNullOrWhiteSpace(_config.Monitors[i]?.Folder)
                    && Directory.Exists(_config.Monitors[i].Folder))
                {
                    var files = Directory.EnumerateFiles(_config.Monitors[i].Folder, "*.*", SearchOption.AllDirectories)
                        .Where(f => exts.Contains(Path.GetExtension(f).ToLowerInvariant()))
                        .ToList();
                    _queues.Add(new Queue<string>(Shuffle(files)));
                }
                else
                {
                    _queues.Add(item: null);
                }
                _lastImages.Add(null);
            }

            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            Application.ApplicationExit += OnProcessExit;
            void OnProcessExit(object? sender, EventArgs e)
            {
                SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, TempPath, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
                _notifyIcon?.Dispose();
            }

            _timer = new System.Threading.Timer(_ => UpdateWallpaper(), null, 0, _config.IntervalSeconds * 1000);

            Application.Run();

            SystemEvents.DisplaySettingsChanged += (_, __) =>
            {
                RestartApplication();
            };
        }

        private static void UpdateWallpaper()
        {
            if (_paused) return;

            string?[] monitorImages = new string?[Screen.AllScreens.Length];
            string[] exts = { ".jpg", ".jpeg", ".png", ".bmp" };

            for (int i = 0; i < Screen.AllScreens.Length; i++)
            {
                if (_queues[i] == null)
                {
                    monitorImages[i] = null;
                }
                else if (_queues[i].Count == 0)
                {
                    if (i < _config.Monitors.Count && !string.IsNullOrWhiteSpace(_config.Monitors[i].Folder)
                        && Directory.Exists(_config.Monitors[i].Folder))
                    {
                        var files = Directory.EnumerateFiles(_config.Monitors[i].Folder, "*.*", SearchOption.AllDirectories)
                            .Where(f => exts.Contains(Path.GetExtension(f).ToLowerInvariant()))
                            .ToList();
                        var shuffled = Shuffle(files);
                        if (_lastImages[i] != null && shuffled.Count > 1 && shuffled[0] == _lastImages[i])
                        {
                            int swapIndex = Rand.Next(1, shuffled.Count);
                            (shuffled[0], shuffled[swapIndex]) = (shuffled[swapIndex], shuffled[0]);
                        }
                        _queues[i] = new Queue<string>(shuffled);
                    }
                    monitorImages[i] = _queues[i].Count > 0 ? _queues[i].Dequeue() : null;
                }
                else
                {
                    monitorImages[i] = _queues[i].Dequeue();
                }
                _lastImages[i] = monitorImages[i];
            }

            ComposeWallpaper(monitorImages, TempPath);
            SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, TempPath, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
            OverwriteWithBlack(TempPath); // フェールセーフ
        }

        private static void ComposeWallpaper(string?[] monitorImages, string path)
        {
            // 仮想デスクトップ全体の論理座標範囲（DPI=96前提）
            Rectangle virtualBounds = Rectangle.Empty;
            foreach (var screen in Screen.AllScreens)
                virtualBounds = Rectangle.Union(virtualBounds, screen.Bounds);

            using var bmp = new Bitmap(virtualBounds.Width, virtualBounds.Height);
            using var gMain = Graphics.FromImage(bmp);
            gMain.FillRectangle(Brushes.Black, new Rectangle(0, 0, bmp.Width, bmp.Height));

            for (int i = 0; i < Screen.AllScreens.Length; i++)
            {
                var screen = Screen.AllScreens[i];
                var bounds = screen.Bounds;
                var drawRect = new Rectangle(bounds.Left - virtualBounds.Left, bounds.Top - virtualBounds.Top, bounds.Width, bounds.Height);

                if (monitorImages[i] == null)
                {
                    gMain.FillRectangle(Brushes.Black, drawRect);
                }
                else
                {
                    using var img = Image.FromFile(monitorImages[i]!);
                    float scale = Math.Max((float)drawRect.Width / img.Width, (float)drawRect.Height / img.Height);
                    int drawW = (int)(img.Width * scale);
                    int drawH = (int)(img.Height * scale);
                    int offsetX = drawRect.Left + (drawRect.Width - drawW) / 2;
                    int offsetY = drawRect.Top + (drawRect.Height - drawH) / 2;
                    gMain.DrawImage(img, new Rectangle(offsetX, offsetY, drawW, drawH));
                }
            }
            bmp.Save(path, ImageFormat.Jpeg);
        }

        private static void OverwriteWithBlack(string targetPath)
        {
            string defaultBlack = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "default.bmp");
            if (!File.Exists(defaultBlack))
            {
                using var bmp = new Bitmap(1, 1);
                using var g = Graphics.FromImage(bmp);
                g.FillRectangle(Brushes.Black, new Rectangle(0, 0, 1, 1));
                bmp.Save(defaultBlack, ImageFormat.Bmp);
            }
            File.Copy(defaultBlack, targetPath, overwrite: true);
        }

        private static List<string> Shuffle(List<string> list)
        {
            return list.OrderBy(_ => Rand.Next()).ToList();
        }

        private static void TogglePause()
        {
            if (_paused)
            {
                _paused = false;
                _notifyIcon!.Icon = _iconRunning;
                _timer!.Change(0, _config.IntervalSeconds * 1000);
            }
            else
            {
                _paused = true;
                _notifyIcon!.Icon = _iconPaused;
                _timer!.Change(Timeout.Infinite, Timeout.Infinite);
                OverwriteWithBlack(TempPath);
                SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, TempPath,
                    SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
            }
        }

        private static void RestartApplication()
        {
            try
            {
                var exe = Process.GetCurrentProcess().MainModule!.FileName!;
                var args = Environment.GetCommandLineArgs();

                Process.Start(exe, string.Join(" ", args.Skip(1)));
                Application.Exit();
            }
            catch { }
        }
    }
}
