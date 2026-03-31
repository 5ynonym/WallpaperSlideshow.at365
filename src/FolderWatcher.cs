namespace at365.WallpaperSlideshow
{
    public class FolderWatcher : IDisposable
    {
        private readonly List<FileSystemWatcher> _watchers = new();
        private readonly Action _onChanged;

        private readonly object _lock = new();
        private System.Threading.Timer? _debounceTimer;
        private const int DebounceMs = 500;

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

                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
        }

        private void OnFsEvent(object sender, FileSystemEventArgs e)
        {
            lock (_lock)
            {
                _debounceTimer?.Dispose();
                _debounceTimer = new System.Threading.Timer(_ =>
                {
                    _onChanged();
                }, null, DebounceMs, Timeout.Infinite);
            }
        }

        public void Dispose()
        {
            foreach (var w in _watchers)
                w.Dispose();

            _debounceTimer?.Dispose();
        }
    }
}
