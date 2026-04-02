using at365.WallpaperSlideshow;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var config = Config.LoadConfig();
        if (config == null) return;

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var dispatcherForm = DispatcherForm.Instance;
        ApplicationController.Instance.Initialize(config, dispatcherForm);
        Application.Run(dispatcherForm);
    }
}
