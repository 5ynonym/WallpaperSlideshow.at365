using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace at365.WallpaperSlideshow
{
    public sealed class ApplicationController : IDisposable
    {
        public static ApplicationController Instance => _lazy.Value;
        private static readonly Lazy<ApplicationController> _lazy = new(() => new ApplicationController());

        private FileSystemWatcher? _configWatcher;
        private System.Threading.Timer? _configDebounce;
        private FolderWatcher? _folderWatcher;
        private System.Windows.Forms.Timer? _uiTimer;
        private Rectangle[]? _lastMonitorBounds;

        private bool _paused = false;
        private bool _disposed;

        private ApplicationController() { }

        public void Initialize(Config config, DispatcherForm dispatcherForm)
        {
            EnsureSingleInstance();
            SetWallpaperSpanMode();
            ApplyConfig(config);

            SystemEvents.DisplaySettingsChanged += (_, _) => InitializeApplication();
            SystemEvents.SessionSwitch += OnSessionSwitch;
            dispatcherForm.OnRdpConnect = () => TogglePause(true);
            dispatcherForm.OnRdpDisconnect = () => TogglePause(true);

            TrayIconManager.Instance.Initialize(
                config,
                leftClickAction: null,
                middleClickAction: null,
                togglePause: () => TogglePause(),
                createHistoryMenu: HistoryManager.Instance.CreateHistoryMenu,
                shutdown: ApplicationShutdown
            );

            _folderWatcher = new FolderWatcher(
                config.Monitors.Select(m => m.Folder),
                () => dispatcherForm.BeginInvoke(() => InitializeApplication(true))
            );

            SetupConfigWatcher();
        }

        private void ApplyConfig(Config config)
        {
            WallpaperRenderer.Instance.SetConfig(config);
            HistoryManager.Instance.SetConfig(config);
            QueueManager.Instance.SetConfig(config);
            WallpaperController.Instance.Initialize(config);

            if (_uiTimer == null)
            {
                _uiTimer = new System.Windows.Forms.Timer();
                _uiTimer.Tick += (_, _) => WallpaperController.Instance.UpdateWallpaper();
            }

            _uiTimer.Interval = config.IntervalSeconds * 1000;
            _uiTimer.Start();

            QueueManager.Instance.Initialize(StableScreensProvider.Screens);
            HistoryManager.Instance.EnsureInitialized(StableScreensProvider.Screens);
            WallpaperController.Instance.UpdateWallpaper();
        }

        private void InitializeApplication(bool forceInitialize = false)
        {
            bool monitorChanged = HasMonitorConfigChanged();
            if (!monitorChanged && !forceInitialize)
                return;

            StableScreensProvider.Refresh();

            QueueManager.Instance.Initialize(StableScreensProvider.Screens);
            HistoryManager.Instance.EnsureInitialized(StableScreensProvider.Screens);
            WallpaperController.Instance.UpdateWallpaper();
        }

        private void SetupConfigWatcher()
        {
            string configPath = Const.ConfigPath;
            string dir = Path.GetDirectoryName(configPath)!;
            string file = Path.GetFileName(configPath);

            _configWatcher = new FileSystemWatcher(dir)
            {
                Filter = file,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
            };

            _configWatcher.Changed += (_, __) =>
            {
                _configDebounce?.Dispose();
                _configDebounce = new System.Threading.Timer(_ =>
                {
                    ReloadConfig();
                }, null, 500, Timeout.Infinite);
            };

            _configWatcher.EnableRaisingEvents = true;
        }

        private void ReloadConfig()
        {
            try
            {
                var newConfig = Config.LoadConfig();
                if (newConfig == null) return;

                ApplyConfig(newConfig);
            }
            catch { }
        }

        public void TogglePause(bool? forceState = null)
        {
            bool target = forceState ?? !_paused;
            if (!target)
            {
                _uiTimer!.Start();
                _paused = false;
                InitializeApplication(true);
            }
            else
            {
                _uiTimer!.Stop();
                _paused = true;
                WallpaperController.Instance.ClearWallpaper();
            }

            TrayIconManager.Instance.UpdateIcon(_paused);
        }

        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            switch (e.Reason)
            {
                case SessionSwitchReason.SessionLock:
                    TogglePause(true);
                    break;

                case SessionSwitchReason.SessionUnlock:
                    TogglePause(false);
                    break;
            }
        }

        private bool HasMonitorConfigChanged()
        {
            var screens = Screen.AllScreens
                .OrderBy(s => s.Bounds.Left)
                .ThenBy(s => s.Bounds.Top)
                .ToArray();

            if (_lastMonitorBounds == null ||
                _lastMonitorBounds.Length != screens.Length)
            {
                _lastMonitorBounds = screens.Select(s => s.Bounds).ToArray();
                return true;
            }

            var bounds = screens.Select(s => s.Bounds).ToArray();

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

        private void SetWallpaperSpanMode()
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\\Desktop", true);
            if (key != null)
            {
                key.SetValue("WallpaperStyle", "22");
                key.SetValue("TileWallpaper", "0");
            }
        }

        private void EnsureSingleInstance()
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

        private static void ApplicationShutdown()
        {
            WallpaperController.Instance.ClearWallpaper();
            Application.Exit();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { _uiTimer?.Dispose(); } catch { }
            try { _folderWatcher?.Dispose(); } catch { }
            try { TrayIconManager.Instance.Dispose(); } catch { }
        }
    }
}
