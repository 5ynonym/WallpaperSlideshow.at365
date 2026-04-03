using System.Drawing.Imaging;
using Image = System.Drawing.Image;

namespace at365.WallpaperSlideshow
{
    public sealed class WallpaperRenderer
    {
        public static WallpaperRenderer Instance => _lazy.Value;
        private static readonly Lazy<WallpaperRenderer> _lazy = new(() => new WallpaperRenderer());

        private Config _config;
        private static readonly Bitmap _empty = new(1, 1);

        private WallpaperRenderer() { }

        public void SetConfig(Config config)
        {
            _config = config;
        }

        public static Image? LoadImageWithoutLock(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;

                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var ms = new MemoryStream();
                fs.CopyTo(ms);
                ms.Position = 0;
                return Image.FromStream(ms);
            }
            catch
            {
                return null;
            }
        }

        public void ComposeMonitor(
            int monitorIndex,
            string? monitorImage,
            Graphics gMain,
            Rectangle virtualBounds,
            Screen[] screens,
            Queue<string> queue,
            Action<int, string> pushHistory)
        {
            if (monitorIndex < 0 || monitorIndex >= screens.Length) return;

            var monitorConfig = (monitorIndex < _config.Monitors.Count)
                ? _config.Monitors[monitorIndex]
                : new MonitorConfig();

            var screen = screens[monitorIndex];
            var bounds = screen.Bounds;

            var drawRect = new Rectangle(
                bounds.Left - virtualBounds.Left + monitorConfig.PaddingLeft,
                bounds.Top - virtualBounds.Top + monitorConfig.PaddingTop,
                bounds.Width - monitorConfig.PaddingLeft - monitorConfig.PaddingRight,
                bounds.Height - monitorConfig.PaddingTop - monitorConfig.PaddingBottom
            );

            if (monitorImage != null && !File.Exists(monitorImage)) return;

            var mode = monitorConfig.Mode ?? StretchMode.Fit;
            if (mode == StretchMode.Tile)
            {
                var paths = new List<string>();
                if (monitorImage != null && File.Exists(monitorImage))
                {
                    paths.Add(monitorImage);
                }

                while (paths.Count < monitorConfig.TileCount && queue.Count > 0)
                {
                    var next = queue.Dequeue();
                    if (next != null && File.Exists(next))
                    {
                        paths.Add(next);
                    }
                }

                if (paths.Count == 0) return;

                List<Image> images = new(paths.Count);
                try
                {
                    foreach (var path in paths)
                    {
                        var image = LoadImageWithoutLock(path);
                        if (image == null) continue;

                        images.Add(image);
                        pushHistory(monitorIndex, path);
                    }

                    if (images.Count > 0)
                    {
                        DrawTile(gMain, images.ToArray(), drawRect);
                    }
                }
                finally
                {
                    foreach (var im in images)
                    {
                        im.Dispose();
                    }
                }
            }
            else
            {
                if (monitorImage == null) return;

                try
                {
                    using var img = LoadImageWithoutLock(monitorImage);
                    if (img == null) return;

                    DrawImageWithMode(gMain, img, drawRect, mode);
                    pushHistory(monitorIndex, monitorImage);
                }
                catch { }
            }
        }

        public void DrawImageWithMode(Graphics g, Image img, Rectangle drawRect, StretchMode mode)
        {
            switch (mode)
            {
                case StretchMode.Fill:
                    {
                        float scale = Math.Max(
                            (float)drawRect.Width / img.Width,
                            (float)drawRect.Height / img.Height
                        );
                        int w = (int)(img.Width * scale);
                        int h = (int)(img.Height * scale);
                        int x = drawRect.Left + (drawRect.Width - w) / 2;
                        int y = drawRect.Top + (drawRect.Height - h) / 2;
                        g.DrawImage(img, new Rectangle(x, y, w, h));
                        break;
                    }

                case StretchMode.Fit:
                    {
                        float scale = Math.Min(
                            (float)drawRect.Width / img.Width,
                            (float)drawRect.Height / img.Height
                        );
                        int w = (int)(img.Width * scale);
                        int h = (int)(img.Height * scale);
                        int x = drawRect.Left + (drawRect.Width - w) / 2;
                        int y = drawRect.Top + (drawRect.Height - h) / 2;
                        g.DrawImage(img, new Rectangle(x, y, w, h));
                        break;
                    }

                case StretchMode.Stretch:
                    g.DrawImage(img, drawRect);
                    break;

                case StretchMode.Center:
                    {
                        int x = drawRect.Left + (drawRect.Width - img.Width) / 2;
                        int y = drawRect.Top + (drawRect.Height - img.Height) / 2;
                        g.DrawImage(img, new Rectangle(x, y, img.Width, img.Height));
                        break;
                    }
            }
        }

        public void DrawTile(Graphics g, Image[] images, Rectangle rect)
        {
            int bestRows = -1;
            float bestScore = float.MaxValue;
            Image[][]? bestSplit = null;
            float[]? bestHeights = null;

            for (int rowCount = 1; rowCount <= images.Length; rowCount++)
            {
                var rows = SplitRowsAspectBalanced(images, rowCount);
                float[] heights = new float[rowCount];
                bool ok = true;

                for (int i = 0; i < rowCount; i++)
                {
                    heights[i] = CalcRowHeightNoOverlap(rows[i], rect.Width);
                    if (heights[i] <= 0)
                    {
                        ok = false;
                        break;
                    }
                }

                if (!ok)
                    continue;

                float totalHeight = heights.Sum();
                if (totalHeight > rect.Height)
                {
                    float scale = rect.Height / totalHeight;
                    for (int i = 0; i < rowCount; i++)
                        heights[i] *= scale;

                    totalHeight = heights.Sum();
                }

                float unusedHeight = rect.Height - totalHeight;
                float unusedWidth = 0;

                for (int i = 0; i < rowCount; i++)
                {
                    float rowWidth = rows[i].Sum(im => (im.Width / (float)im.Height) * heights[i]);
                    unusedWidth += Math.Max(0, rect.Width - rowWidth);
                }

                float score = unusedHeight * 3f + unusedWidth;
                if (score < bestScore)
                {
                    bestScore = score;
                    bestRows = rowCount;
                    bestSplit = rows;
                    bestHeights = heights;
                }
            }

            if (bestRows < 0 || bestSplit == null || bestHeights == null)
                return;

            for (int r = 0; r < bestRows; r++)
                bestSplit[r] = bestSplit[r].OrderBy(_ => Random.Shared.Next()).ToArray();

            float totalHeightFinal = bestHeights.Sum();
            float gapY = (rect.Height - totalHeightFinal) / (bestRows + 1);
            if (gapY < 0) gapY = 0;

            float y = rect.Top + gapY;

            for (int i = 0; i < bestRows; i++)
            {
                DrawRowNoOverlap_WithVisualMargin(g, bestSplit[i], rect, bestHeights[i], y);
                y += bestHeights[i] + gapY;
            }
        }

        private static Image[][] SplitRowsAspectBalanced(Image[] images, int rows)
        {
            var result = new List<Image>[rows];
            for (int i = 0; i < rows; i++)
                result[i] = new List<Image>();

            float[] rowAspect = new float[rows];

            foreach (var img in images.OrderByDescending(i => (float)i.Width / i.Height))
            {
                int target = Array.IndexOf(rowAspect, rowAspect.Min());
                result[target].Add(img);
                rowAspect[target] += (float)img.Width / img.Height;
            }

            return result.Select(r => r.ToArray()).ToArray();
        }

        private static float CalcRowHeightNoOverlap(Image[] row, int totalWidth)
        {
            float sumAspect = row.Sum(im => (float)im.Width / im.Height);
            float h = totalWidth / sumAspect;
            return AdjustHeight(row, totalWidth, h);
        }

        private static float AdjustHeight(Image[] row, int totalWidth, float h)
        {
            const int MAX_ITER = 200;

            for (int i = 0; i < MAX_ITER; i++)
            {
                float width = 0;
                foreach (var img in row)
                    width += (img.Width / (float)img.Height) * h;

                if (width <= totalWidth)
                    return h;

                h *= 0.95f;

                if (h < 1f)
                    return -1f;
            }

            return -1f;
        }

        private void DrawRowNoOverlap_WithVisualMargin(Graphics g, Image[] row, Rectangle rect, float rowHeight, float y)
        {
            float[] widths = row.Select(im => (float)im.Width / im.Height * rowHeight).ToArray();
            float total = widths.Sum();
            float gapX = (rect.Width - total) / (row.Length + 1);
            if (gapX < 0) gapX = 0;

            float x = rect.Left + gapX;
            for (int i = 0; i < row.Length; i++)
            {
                float w = widths[i];
                var layoutRect = new RectangleF(x, y, w, rowHeight);
                var margin = _config.TileMargin / 2;
                var inner = RectangleF.Inflate(layoutRect, -margin, -margin);
                if (inner.Width <= 0 || inner.Height <= 0) continue;

                DrawImageFit(g, row[i], Rectangle.Round(inner));
                x += w + gapX;
            }
        }

        private static void DrawImageFit(Graphics g, Image img, Rectangle rect)
        {
            if (rect.Width <= 0 || rect.Height <= 0) return;

            float s = Math.Min((float)rect.Width / img.Width, (float)rect.Height / img.Height);
            int w = (int)(img.Width * s);
            int h = (int)(img.Height * s);
            int x = rect.Left + (rect.Width - w) / 2;
            int y = rect.Top + (rect.Height - h) / 2;
            g.DrawImage(img, new Rectangle(x, y, w, h));
        }

        public void OverwriteWithBlack(string targetPath)
        {
            try { _empty.Save(targetPath, ImageFormat.Bmp); } catch { }
        }
    }
}
