namespace at365.WallpaperSlideshow
{
    internal class Const
    {
        public static string AppFolderName = @"at365\WallpaperSlideshow";
        public static string AppDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolderName);

        public static string ConfigFileName = "config.json";
        public static string ConfigPath = Path.Combine(AppDataFolder, ConfigFileName);

        public static string MergedJpgPath => Path.Combine(Const.AppDataFolder, "merged_wallpaper.jpg");
    }
}
