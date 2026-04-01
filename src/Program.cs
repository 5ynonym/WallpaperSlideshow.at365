using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace at365.WallpaperSlideshow
{
    public static class Program
    {
        private static TrayIconManager? _tray;
        private static Config _config = new();

        private static System.Threading.Timer? _timer;
        private static bool _paused = false;
        
        private static List<Queue<string>> _queues = new();
        private static List<string?> _lastImages = new();

        private static FolderWatcher? _folderWatcher;
        private static readonly string[] ImageExts = [".jpg", ".jpeg", ".png", ".bmp"];

        private static Rectangle[]? _lastMonitorBounds;

        [STAThread]
        public static void Main(string[] args)
        {
            EnsureSingleInstance();

            _config = Config.LoadConfig()!;
            if (_config == null) return;
            WallpaperRenderer.Instance.SetConfig(_config);
            HistoryManager.Instance.SetConfig(_config);

            SetWallpaperSpanMode();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            InitializeApplication();

            SystemEvents.DisplaySettingsChanged += (_, _) => InitializeApplication();
            SystemEvents.SessionSwitch += OnSessionSwitch;

            _tray = new TrayIconManager(
                getPausedState: () => _paused,
                togglePause: () => TogglePause(),
                openDataFolder: OpenDataFolder,
                createHistoryMenu: () => HistoryManager.Instance.CreateHistoryMenu()
            );
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

        private static void InitializeMonitorState()
        {
            _queues.Clear();
            _lastImages.Clear();
            StableScreensProvider.Refresh();

            var screens = StableScreensProvider.Screens;

            HistoryManager.Instance.EnsureInitialized(screens);

            while (_queues.Count < screens.Length)
                _queues.Add(BuildQueueForMonitor(_queues.Count));

            while (_lastImages.Count < screens.Length)
                _lastImages.Add(null);
        }

        internal static void InitializeApplication(bool forceInitialize = false)
        {
            bool monitorChanged = HasMonitorConfigChanged();
            if (!monitorChanged && !forceInitialize)
                return;

            InitializeMonitorState();
            UpdateWallpaper();
        }

        private static void UpdateWallpaper()
        {
            if (_paused) return;

            var screens = StableScreensProvider.Screens;

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
                WallpaperRenderer.Instance.ComposeMonitor(
                    i,
                    monitorImages[i],
                    gMain,
                    virtualBounds,
                    screens,
                    _queues[i],
                    (mon, path) => HistoryManager.Instance.Push(mon, path, _config.History.Limit)
                );
            }

            try { bmp.Save(Const.WallpaperPicturePath, ImageFormat.Bmp); } catch { }

            ApplyWallpaper();
            WallpaperRenderer.Instance.OverwriteWithBlack(Const.WallpaperPicturePath);
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

            return new Queue<string>(WallpaperRenderer.Instance.Shuffle(files));
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

        internal static void TogglePause(bool? forceState = null)
        {
            bool target = forceState ?? !_paused;

            if (!target)
            {
                _timer!.Change(0, _config.IntervalSeconds * 1000);
                _paused = false;
                _tray?.UpdateIcon();
            }
            else
            {
                _timer!.Change(Timeout.Infinite, Timeout.Infinite);
                _paused = true;
                _tray?.UpdateIcon();
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
            try { _timer?.Dispose(); } catch { }
            try { _folderWatcher?.Dispose(); _folderWatcher = null; } catch { }
            try { _tray?.Dispose(); _tray = null; } catch { }
            try { ApplyWallpaper(); } catch { }
            try { Application.Exit(); } catch { }
        }

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, string pvParam, uint fWinIni);
        private const uint SPI_SETDESKWALLPAPER = 0x0014;
        private const uint SPIF_UPDATEINIFILE = 0x01;
        private const uint SPIF_SENDCHANGE = 0x02;

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
    }
}
