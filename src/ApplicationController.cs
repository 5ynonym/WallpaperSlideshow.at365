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

        private Config? _config;
        private bool _paused = false;

        private FolderWatcher? _folderWatcher;
        private System.Threading.Timer? _timer;
        private Rectangle[]? _lastMonitorBounds;

        private bool _disposed;

        private ApplicationController() { }

        // ============================================================
        // 初期化
        // ============================================================
        public void Initialize(Config config)
        {
            EnsureSingleInstance();
            SetWallpaperSpanMode();

            ApplyConfig(config);

            SystemEvents.DisplaySettingsChanged += (_, _) => InitializeApplication();
            SystemEvents.SessionSwitch += OnSessionSwitch;

            TrayIconManager.Instance.Initialize(
                config,
                getPausedState: () => _paused,
                togglePause: () => TogglePause(),
                createHistoryMenu: HistoryManager.Instance.CreateHistoryMenu
            );

            _folderWatcher = new FolderWatcher(
                config.Monitors.Select(m => m.Folder),
                () => InitializeApplication(true)
            );

            SetupConfigWatcher();
        }

        private void ApplyConfig(Config config)
        {
            _config = config;

            WallpaperRenderer.Instance.SetConfig(config);
            HistoryManager.Instance.SetConfig(config);
            QueueManager.Instance.SetConfig(config);
            WallpaperController.Instance.Initialize(config);

            if (_timer == null)
            {
                _timer = new System.Threading.Timer(
                    _ => WallpaperController.Instance.UpdateWallpaper(),
                    null,
                    config.IntervalSeconds * 1000,
                    config.IntervalSeconds * 1000
                );
            }
            else
            {
                _timer.Change(
                    config.IntervalSeconds * 1000,
                    config.IntervalSeconds * 1000
                );
            }

            QueueManager.Instance.Initialize(StableScreensProvider.Screens);
            HistoryManager.Instance.EnsureInitialized(StableScreensProvider.Screens);
            WallpaperController.Instance.UpdateWallpaper();
        }

        // ============================================================
        // アプリ初期化
        // ============================================================
        private void InitializeApplication(bool forceInitialize = false)
        {
            bool monitorChanged = HasMonitorConfigChanged();
            if (!monitorChanged && !forceInitialize)
                return;

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

        // ============================================================
        // Pause / Resume
        // ============================================================
        public void TogglePause(bool? forceState = null)
        {
            bool target = forceState ?? !_paused;

            if (!target)
            {
                _timer!.Change(0, _config!.IntervalSeconds * 1000);
                _paused = false;
                TrayIconManager.Instance.UpdateIcon();
            }
            else
            {
                _timer!.Change(Timeout.Infinite, Timeout.Infinite);
                _paused = true;
                TrayIconManager.Instance.UpdateIcon();
                WallpaperController.Instance.UpdateWallpaper();
            }
        }

        // ============================================================
        // SessionSwitch（ロック/アンロック）
        // ============================================================
        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
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

        // ============================================================
        // Monitor 変更検知
        // ============================================================
        private bool HasMonitorConfigChanged()
        {
            var screens = Screen.AllScreens
                .OrderBy(s => s.Bounds.Left)
                .ThenBy(s => s.Bounds.Top)
                .ToArray();

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

        // ============================================================
        // 壁紙スパンモード
        // ============================================================
        private void SetWallpaperSpanMode()
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\\Desktop", true);
            if (key != null)
            {
                key.SetValue("WallpaperStyle", "22");
                key.SetValue("TileWallpaper", "0");
            }
        }

        // ============================================================
        // シングルインスタンス
        // ============================================================
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

        // ============================================================
        // Dispose
        // ============================================================
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { _timer?.Dispose(); } catch { }
            try { _folderWatcher?.Dispose(); } catch { }
            try { TrayIconManager.Instance.Dispose(); } catch { }
        }
    }
}
