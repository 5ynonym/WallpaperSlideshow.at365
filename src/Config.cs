using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace at365.WallpaperSlideshow
{
    public class MonitorConfig
    {
        public string? Folder { get; set; }
    }

    public class Config
    {
        public int IntervalSeconds { get; set; } = 60;
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
                var options = new JsonSerializerOptions { AllowTrailingCommas = true };
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
