using System.Diagnostics;

namespace at365.WallpaperSlideshow
{
    public sealed class TrayIconManager : IDisposable
    {
        private static readonly Lazy<TrayIconManager> _lazy =
            new(() => new TrayIconManager());

        public static TrayIconManager Instance => _lazy.Value;

        private Config? _config;

        private NotifyIcon? _notifyIcon;
        private Icon? _iconRunning;
        private Icon? _iconPaused;

        private Func<bool>? _getPausedState;
        private Action? _togglePause;
        private Func<ToolStripMenuItem>? _createHistoryMenu;
        private Action? _shutdown;

        private bool _disposed;

        private TrayIconManager() { }

        public void Initialize(
            Config config,
            Func<bool> getPausedState,
            Action togglePause,
            Func<ToolStripMenuItem> createHistoryMenu,
            Action shutdown)
        {
            _config = config;
            _getPausedState = getPausedState;
            _togglePause = togglePause;
            _createHistoryMenu = createHistoryMenu;
            _shutdown = shutdown;

            _iconRunning = new Icon("running.ico");
            _iconPaused = new Icon("paused.ico");

            _notifyIcon = new NotifyIcon
            {
                Icon = _iconRunning,
                Text = "WallpaperSlideshow.at365",
                Visible = true
            };

            _notifyIcon.MouseClick += OnMouseClick;

            BuildContextMenu();
        }

        public void SetConfig(Config config)
        {
            _config = config;
        }

        private void OnMouseClick(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
                _togglePause?.Invoke();
        }

        private void BuildContextMenu()
        {
            if (_notifyIcon == null) return;

            var menu = new ContextMenuStrip();

            var openData = new ToolStripMenuItem("データフォルダを開く(&D)");
            openData.Click += (_, _) => OpenDataFolder();
            menu.Items.Add(openData);

            if (_createHistoryMenu != null)
                menu.Items.Add(_createHistoryMenu());

            var exit = new ToolStripMenuItem("終了(&X)");
            exit.Click += (_, _) => _shutdown?.Invoke();
            menu.Items.Add(exit);

            _notifyIcon.ContextMenuStrip = menu;
        }

        public void UpdateIcon()
        {
            if (_notifyIcon == null || _iconRunning == null || _iconPaused == null)
                return;

            bool paused = _getPausedState?.Invoke() ?? false;
            _notifyIcon.Icon = paused ? _iconPaused : _iconRunning;
        }

        private void OpenDataFolder()
        {
            if (_config == null) return;

            try
            {
                string path = Const.AppDataFolder;

                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);

                Process.Start("explorer.exe", path);
            }
            catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { if (_notifyIcon != null) _notifyIcon.Visible = false; } catch { }
            try { _notifyIcon?.Dispose(); } catch { }
            try { _iconRunning?.Dispose(); } catch { }
            try { _iconPaused?.Dispose(); } catch { }
        }
    }
}
