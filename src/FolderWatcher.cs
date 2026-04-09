namespace at365.WallpaperSlideshow
{
    public class FolderWatcher : IDisposable
    {
        private readonly List<FileSystemWatcher> _watchers = new();
        private readonly Action _onChanged;

        private readonly object _lock = new();
        private bool _disposed = false;

        public FolderWatcher(IEnumerable<string?> folders, Action onChanged)
        {
            _onChanged = onChanged;

            foreach (var folder in folders)
            {
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                    continue;

                var watcher = new FileSystemWatcher(folder)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter =
                        NotifyFilters.FileName |
                        NotifyFilters.DirectoryName |
                        NotifyFilters.LastWrite |
                        NotifyFilters.Size
                };

                watcher.Changed += OnFsEvent;
                watcher.Created += OnFsEvent;
                watcher.Deleted += OnFsEvent;
                watcher.Renamed += OnFsEvent;
                watcher.Error += (_, __) => { };

                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
        }

        private void OnFsEvent(object sender, FileSystemEventArgs e)
        {
            if (_disposed) return;

            try
            {
                lock (_lock)
                {
                    if (_disposed) return;

                    _onChanged();
                }
            }
            catch { }
        }

        public void Dispose()
        {
            _disposed = true;

            foreach (var w in _watchers)
            {
                try { w.Dispose(); }
                catch { }
            }
        }
    }
}
