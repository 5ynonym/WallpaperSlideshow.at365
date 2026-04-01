namespace at365.WallpaperSlideshow
{
    public sealed class TrayIconManager : IDisposable
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly Icon _iconRunning;
        private readonly Icon _iconPaused;

        private readonly Func<bool> _getPausedState;
        private readonly Action _togglePause;
        private readonly Action _openDataFolder;
        private readonly Func<ToolStripMenuItem> _createHistoryMenu;

        private bool _disposed;

        public TrayIconManager(
            Func<bool> getPausedState,
            Action togglePause,
            Action openDataFolder,
            Func<ToolStripMenuItem> createHistoryMenu)
        {
            _getPausedState = getPausedState;
            _togglePause = togglePause;
            _openDataFolder = openDataFolder;
            _createHistoryMenu = createHistoryMenu;

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

        private void OnMouseClick(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
                _togglePause();
        }

        private void BuildContextMenu()
        {
            var menu = new ContextMenuStrip();

            var openData = new ToolStripMenuItem("データフォルダを開く(&D)");
            openData.Click += (_, _) => _openDataFolder();
            menu.Items.Add(openData);

            // 履歴メニューは Program 側で生成する（依存を逆転）
            menu.Items.Add(_createHistoryMenu());

            var exit = new ToolStripMenuItem("終了(&X)");
            exit.Click += (_, _) => Application.Exit();
            menu.Items.Add(exit);

            _notifyIcon.ContextMenuStrip = menu;
        }

        public void UpdateIcon()
        {
            _notifyIcon.Icon = _getPausedState() ? _iconPaused : _iconRunning;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { _notifyIcon.Visible = false; } catch { }
            try { _notifyIcon.Dispose(); } catch { }
            try { _iconRunning.Dispose(); } catch { }
            try { _iconPaused.Dispose(); } catch { }
        }
    }
}
