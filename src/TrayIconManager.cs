using System.Diagnostics;

namespace at365.WallpaperSlideshow
{
    public sealed class TrayIconManager : IDisposable
    {
        private static readonly Lazy<TrayIconManager> _lazy =
            new(() => new TrayIconManager());

        public static TrayIconManager Instance => _lazy.Value;

        private Config? _config = null;
        private NotifyIcon? _notifyIcon = null;
        private Icon? _iconRunning = null;
        private Icon? _iconPaused = null;

        private Action? _leftClickAction = null;
        private Action? _middleClickAction = null;
        private Action? _togglePause = null;
        private Func<ToolStripMenuItem>? _createHistoryMenu = null;
        private Action? _shutdown = null;

        private bool _disposed;

        private TrayIconManager() { }

        public void Initialize(
            Config config,
            Action? leftClickAction,
            Action? middleClickAction,
            Action? togglePause,
            Func<ToolStripMenuItem> createHistoryMenu,
            Action shutdown)
        {
            _config = config;
            _leftClickAction = leftClickAction ?? togglePause;
            _middleClickAction = middleClickAction ?? togglePause;
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
            switch (e.Button)
            {
                case MouseButtons.Left:
                    _leftClickAction?.Invoke();
                    break;
                case MouseButtons.Middle:
                    _middleClickAction?.Invoke();
                    break;
            }
        }

        private void BuildContextMenu()
        {
            if (_notifyIcon == null) return;

            var menu = new ContextMenuStrip();

            if (_togglePause != null)
            {
                var pause = new ToolStripMenuItem("一時停止/再開(&P)");
                pause.Click += (_, _) => _togglePause();
                menu.Items.Add(pause);
            }

            if (_createHistoryMenu != null)
            {
                menu.Items.Add(_createHistoryMenu());
            }

            menu.Items.Add(new ToolStripSeparator());

            var openData = new ToolStripMenuItem("データフォルダを開く(&D)");
            openData.Click += (_, _) => OpenDataFolder();
            menu.Items.Add(openData);

            var exit = new ToolStripMenuItem("終了(&X)");
            exit.Click += (_, _) => _shutdown?.Invoke();
            menu.Items.Add(exit);

            _notifyIcon.ContextMenuStrip = menu;
        }

        public void UpdateIcon(bool paused)
        {
            if (_notifyIcon == null || _iconRunning == null || _iconPaused == null)
                return;

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
