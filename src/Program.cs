using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace at365.WallpaperSlideshow
{
    public static class Program
    {
        private static Config _config = new();

        private static System.Threading.Timer? _timer;
        private static bool _paused = false;
        
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
            WallpaperController.Instance.Initialize(_config);

            SetWallpaperSpanMode();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            InitializeApplication();

            SystemEvents.DisplaySettingsChanged += (_, _) => InitializeApplication();
            SystemEvents.SessionSwitch += OnSessionSwitch;

            TrayIconManager.Instance.Initialize(
                _config,
                getPausedState: () => _paused,
                togglePause: () => TogglePause(),
                createHistoryMenu: HistoryManager.Instance.CreateHistoryMenu
            );

            _folderWatcher = new FolderWatcher(_config.Monitors.Select(m => m.Folder), () => InitializeApplication(true));
            _timer = new System.Threading.Timer(
                _ => WallpaperController.Instance.UpdateWallpaper(),
                null, _config.IntervalSeconds * 1000, _config.IntervalSeconds * 1000);

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
            StableScreensProvider.Refresh();
            QueueManager.Instance.SetConfig(_config);
            QueueManager.Instance.Initialize(StableScreensProvider.Screens);
            HistoryManager.Instance.EnsureInitialized(StableScreensProvider.Screens);
        }

        internal static void InitializeApplication(bool forceInitialize = false)
        {
            bool monitorChanged = HasMonitorConfigChanged();
            if (!monitorChanged && !forceInitialize)
                return;

            InitializeMonitorState();
            WallpaperController.Instance.UpdateWallpaper();
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
                TrayIconManager.Instance.UpdateIcon();
            }
            else
            {
                _timer!.Change(Timeout.Infinite, Timeout.Infinite);
                _paused = true;
                TrayIconManager.Instance.UpdateIcon();
                ApplyWallpaper();
            }
        }

        internal static void ApplicationShutdown()
        {
            try { _timer?.Dispose(); } catch { }
            try { _folderWatcher?.Dispose(); _folderWatcher = null; } catch { }
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
