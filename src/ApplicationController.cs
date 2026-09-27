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

        private bool _requiredInitialize = false;
        private bool _paused = false;
        private readonly PauseState _pauseState = new();
        private bool _disposed;

        private ApplicationController() { }

        public void Initialize(Config config, DispatcherForm dispatcherForm)
        {
            EnsureSingleInstance();
            _pauseState.Remote = SystemInformation.TerminalServerSession;
            _paused = _pauseState.IsPaused;
            ApplyConfig(config);
            SetWallpaperSpanMode();
            InitializeApplication();

            _ = dispatcherForm.Handle;
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            SystemEvents.SessionSwitch += OnSessionSwitch;
            dispatcherForm.OnRdpConnect = () => SetRemotePause(true);
            dispatcherForm.OnRdpDisconnect = () => SetRemotePause(true);

            TrayIconManager.Instance.Initialize(
                config,
                leftClickAction: null,
                middleClickAction: null,
                togglePause: () => TogglePause(),
                createHistoryMenu: HistoryManager.Instance.CreateHistoryMenu,
                shutdown: ApplicationShutdown
            );

            if (_uiTimer == null)
            {
                _uiTimer = new System.Windows.Forms.Timer();
                _uiTimer.Tick += (_, _) =>
                {
                    if (_pauseState.IsPaused) return;
                    if (_requiredInitialize)
                    {
                        _requiredInitialize = false;
                        InitializeApplication(true);
                    }
                    else
                    {
                        WallpaperController.Instance.UpdateWallpaper();
                    }
                };
            }

            _uiTimer.Interval = config.IntervalSeconds * 1000;
            if (!_paused) _uiTimer.Start();
            else WallpaperController.ClearWallpaper();
            TrayIconManager.Instance.UpdateIcon(_paused);

            SetupConfigWatcher();
        }

        private void ApplyConfig(Config config)
        {
            var screens = StableScreensProvider.Screens;
            config.Validate(screens.Select(s => s.Bounds));
            // Prepare fallible work before replacing any active configuration.
            var queues = QueueManager.Prepare(config, screens.Length);
            var watcher = new FolderWatcher(config.Monitors.Select(m => m.Folder),
                () => _requiredInitialize = true);

            WallpaperRenderer.Instance.SetConfig(config);
            WallpaperController.Instance.Initialize(config);
            QueueManager.Instance.SetConfig(config);
            QueueManager.Instance.ReplaceQueues(queues);
            HistoryManager.Instance.SetConfig(config);
            HistoryManager.Instance.EnsureInitialized(screens);
            var previousWatcher = _folderWatcher;
            _folderWatcher = watcher;
            previousWatcher?.Dispose();
            if (_uiTimer != null) _uiTimer.Interval = checked(config.IntervalSeconds * 1000);
        }

        private void InitializeApplication(bool forceInitialize = false)
        {
            bool monitorChanged = HasMonitorConfigChanged();
            if (!monitorChanged && !forceInitialize)
                return;

            StableScreensProvider.Refresh();
            QueueManager.Instance.Initialize(StableScreensProvider.Screens);
            HistoryManager.Instance.EnsureInitialized(StableScreensProvider.Screens);
            if (!_pauseState.IsPaused)
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
                    OnUiThread(ReloadConfig);
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
                _requiredInitialize = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"設定を適用できません。以前の設定を維持します: {ex.Message}",
                    "設定エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        public void TogglePause(bool? forceState = null)
        {
            _pauseState.Manual = forceState ?? !_pauseState.Manual;
            ApplyPauseState();
        }

        private void SetRemotePause(bool paused)
        {
            _pauseState.Remote = paused;
            ApplyPauseState();
        }

        private void ApplyPauseState()
        {
            bool target = _pauseState.IsPaused;
            if (target == _paused) return;
            _paused = target;
            if (!target)
            {
                InitializeApplication(true);
                _uiTimer?.Start();
            }
            else
            {
                _uiTimer?.Stop();
                WallpaperController.ClearWallpaper();
            }

            TrayIconManager.Instance.UpdateIcon(_paused);
        }

        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            OnUiThread(() =>
            {
                switch (e.Reason)
                {
                    case SessionSwitchReason.SessionLock:
                        _pauseState.SessionLocked = true;
                        break;
                    case SessionSwitchReason.SessionUnlock:
                        _pauseState.SessionLocked = false;
                        break;
                    case SessionSwitchReason.RemoteConnect:
                    case SessionSwitchReason.RemoteDisconnect:
                        _pauseState.Remote = true;
                        break;
                    case SessionSwitchReason.ConsoleConnect:
                        _pauseState.Remote = false;
                        break;
                }
                ApplyPauseState();
            });
        }

        private void OnDisplaySettingsChanged(object? sender, EventArgs e)
            => OnUiThread(() => InitializeApplication());

        private void OnUiThread(Action action)
        {
            var form = DispatcherForm.Instance;
            if (_disposed || form.IsDisposed || !form.IsHandleCreated) return;
            if (form.InvokeRequired)
            {
                try
                {
                    form.BeginInvoke(() =>
                    {
                        if (!_disposed && !form.IsDisposed) action();
                    });
                }
                catch (InvalidOperationException) when (_disposed || form.IsDisposed || !form.IsHandleCreated) { }
            }
            else action();
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

        public static void ApplicationShutdown()
        {
      WallpaperController.ClearWallpaper();
            Application.Exit();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            try { _configWatcher?.Dispose(); } catch { }
            try { _configDebounce?.Dispose(); } catch { }
            try { _uiTimer?.Dispose(); } catch { }
            try { _folderWatcher?.Dispose(); } catch { }
            try { TrayIconManager.Instance.Dispose(); } catch { }
        }
    }
}
