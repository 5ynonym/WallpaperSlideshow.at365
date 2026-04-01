using System.Text.Json;
using System.Text.Json.Serialization;

namespace at365.WallpaperSlideshow
{
    public enum StretchMode
    {
        Fill,    // 画面いっぱい（現在の動作）
        Fit,     // 黒帯ありで収まるように
        Stretch, // アスペクト比無視で引き伸ばし
        Center,   // 中央に等倍表示
        Tile,   // タイル表示 (TileCount枚数で画面を埋める)
    }

    public class MonitorConfig
    {
        public string? Folder { get; set; }
        public StretchMode? Mode { get; set; } = StretchMode.Fit;
    }

    public class Config
    {
        public int IntervalSeconds { get; set; } = 60;
        public int HistoryLimit { get; set; } = 30;
        public int TileCount { get; set; } = 8;
        public List<MonitorConfig> Monitors { get; set; } = new();

        public static Config? LoadConfig()
        {
            if (!Directory.Exists(Const.AppDataFolder))
            {
                Directory.CreateDirectory(Const.AppDataFolder);
            }

            var configPath = Const.ConfigPath;
            if (!File.Exists(configPath))
            {
                configPath = Const.ConfigFileName; // カレントディレクトリ
                if (!File.Exists(configPath))
                {
                    MessageBox.Show($"設定ファイルが見つかりません: {configPath}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return null;
                }
            }

            try
            {
                var options = new JsonSerializerOptions
                {
                    AllowTrailingCommas = true,
                    ReadCommentHandling = JsonCommentHandling.Skip
                };
                options.Converters.Add(new JsonStringEnumConverter());
                return JsonSerializer.Deserialize<Config>(File.ReadAllText(configPath), options) ?? new Config();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"設定ファイルの読み込みに失敗しました: {ex.Message}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
        }
    }
}
