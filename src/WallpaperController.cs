using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace at365.WallpaperSlideshow
{
    public sealed class WallpaperController
    {
        private static readonly Lazy<WallpaperController> _lazy =
            new(() => new WallpaperController());

        public static WallpaperController Instance => _lazy.Value;

        private Config? _config;

        private WallpaperController() { }

        public void Initialize(Config config)
        {
            _config = config;
        }

        // ============================================================
        // 壁紙更新
        // ============================================================
        public void UpdateWallpaper()
        {
            if (_config == null)
                throw new InvalidOperationException("WallpaperController.Initialize() が呼ばれていません");

            var screens = StableScreensProvider.Screens;

            // キュー初期化（必要なら）
            QueueManager.Instance.Initialize(screens);
            HistoryManager.Instance.EnsureInitialized(screens);

            string?[] monitorImages = new string?[screens.Length];

            Rectangle virtualBounds = Rectangle.Empty;

            // ------------------------------------------------------------
            // 各モニタの画像を QueueManager から取得
            // ------------------------------------------------------------
            for (int i = 0; i < screens.Length; i++)
            {
                var next = QueueManager.Instance.GetNextImage(i);
                monitorImages[i] = next;
                virtualBounds = Rectangle.Union(virtualBounds, screens[i].Bounds);
            }

            // ------------------------------------------------------------
            // 仮想キャンバス作成
            // ------------------------------------------------------------
            using var bmp = new Bitmap(virtualBounds.Width, virtualBounds.Height);
            using var gMain = Graphics.FromImage(bmp);
            gMain.FillRectangle(Brushes.Black, new Rectangle(0, 0, bmp.Width, bmp.Height));

            // ------------------------------------------------------------
            // 各モニタを描画
            // ------------------------------------------------------------
            for (int i = 0; i < screens.Length; i++)
            {
                WallpaperRenderer.Instance.ComposeMonitor(
                    i,
                    monitorImages[i],
                    gMain,
                    virtualBounds,
                    screens,
                    QueueManager.Instance.GetQueue(i),
                    (mon, path) => HistoryManager.Instance.Push(mon, path, _config.History.Limit)
                );
            }

            // ------------------------------------------------------------
            // 壁紙ファイル保存
            // ------------------------------------------------------------
            try { bmp.Save(Const.WallpaperPicturePath, ImageFormat.Bmp); } catch { }

            ApplyWallpaper();
            WallpaperRenderer.Instance.OverwriteWithBlack(Const.WallpaperPicturePath);
        }

        // ============================================================
        // 壁紙適用（Program.cs から移動）
        // ============================================================
        private void ApplyWallpaper()
        {
            try
            {
                SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, Const.WallpaperPicturePath,
                    SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
            }
            catch { }
        }

        // WinAPI
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, string pvParam, uint fWinIni);

        private const uint SPI_SETDESKWALLPAPER = 0x0014;
        private const uint SPIF_UPDATEINIFILE = 0x01;
        private const uint SPIF_SENDCHANGE = 0x02;
    }
}
