using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace at365.WallpaperSlideshow
{
    public sealed class QueueManager
    {
        private static readonly Lazy<QueueManager> _lazy =
            new(() => new QueueManager());

        public static QueueManager Instance => _lazy.Value;

        private readonly List<Queue<string>> _queues = new();
        private readonly List<string?> _lastImages = new();
        private Config? _config;

        private QueueManager() { }

        public void SetConfig(Config config)
        {
            _config = config;
        }

        public void Initialize(Screen[] screens)
        {
            if (_config == null)
                throw new InvalidOperationException("QueueManager.SetConfig() が呼ばれていません");

            _queues.Clear();
            _lastImages.Clear();

            for (int i = 0; i < screens.Length; i++)
            {
                _queues.Add(BuildQueueForMonitor(i));
                _lastImages.Add(null);
            }
        }

        public Queue<string> GetQueue(int monitorIndex)
        {
            return _queues[monitorIndex];
        }

        public string? GetNextImage(int monitorIndex)
        {
            if (_config == null)
                throw new InvalidOperationException("QueueManager.SetConfig() が呼ばれていません");

            if (monitorIndex < 0 || monitorIndex >= _queues.Count)
                return null;

            var queue = _queues[monitorIndex];

            // キューが空なら再構築
            if (queue.Count == 0)
            {
                queue = _queues[monitorIndex] = BuildQueueForMonitor(monitorIndex);
            }

            // 前回と同じ画像が先頭ならスワップ
            if (_lastImages[monitorIndex] != null &&
                queue.Count > 1 &&
                queue.Peek() == _lastImages[monitorIndex])
            {
                var arr = queue.ToArray();
                int swapIndex = Random.Shared.Next(1, arr.Length);
                (arr[0], arr[swapIndex]) = (arr[swapIndex], arr[0]);
                queue = _queues[monitorIndex] = new Queue<string>(arr);
            }

            // 次の画像を取得
            string? next = queue.Count > 0 ? queue.Dequeue() : null;
            _lastImages[monitorIndex] = next;
            return next;
        }

        private Queue<string> BuildQueueForMonitor(int index)
        {
            if (_config == null)
                throw new InvalidOperationException("QueueManager.SetConfig() が呼ばれていません");

            string? folder = (index < _config.Monitors.Count)
                ? _config.Monitors[index].Folder
                : null;

            List<string> files = new();

            if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
            {
                try
                {
                    files = Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
                        .Where(f => IsImageExt(f))
                        .ToList();
                }
                catch { }
            }

            return new Queue<string>(Shuffle(files));
        }

        private bool IsImageExt(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".bmp";
        }

        private List<string> Shuffle(List<string> list)
        {
            return list.OrderBy(_ => Random.Shared.Next()).ToList();
        }
    }
}
