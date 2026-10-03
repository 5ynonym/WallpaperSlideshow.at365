namespace at365.WallpaperSlideshow;

// UI-thread polling also handles atomic replacements and directory recreation.
internal sealed class ConfigReloader(string path, Action<Config> apply)
{
    private string? _pending;
    private string? _accepted;
    public void Poll()
    {
        try
        {
            var text = File.ReadAllText(path);
            if (text != _pending) { _pending = text; return; }
            if (text == _accepted) return;
            apply(Config.Parse(text));
            _accepted = text;
        }
        catch (Exception ex)
        {
            _pending = null;
            AppLog.Error("設定再読み込み（現在の設定を維持）", ex);
        }
    }
}
