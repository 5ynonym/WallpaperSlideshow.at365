namespace at365.WallpaperSlideshow;

internal static class ImageCatalog
{
    public static List<string> Scan(string root,
        Func<string, IEnumerable<string>>? files = null,
        Func<string, IEnumerable<string>>? directories = null)
    {
        files ??= path => Directory.EnumerateFiles(path);
        directories ??= path => Directory.EnumerateDirectories(path, "*", new EnumerationOptions
        {
            RecurseSubdirectories = false, IgnoreInaccessible = false,
            AttributesToSkip = FileAttributes.ReparsePoint
        });
        var result = new List<string>();
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            try
            {
                foreach (var file in files(directory))
                    if (Path.GetExtension(file).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".bmp")
                        result.Add(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { AppLog.Error("画像一覧: " + directory, ex); }
            try
            {
                foreach (var child in directories(directory)) pending.Push(child);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { AppLog.Error("サブフォルダ一覧: " + directory, ex); }
        }
        return result;
    }
}
