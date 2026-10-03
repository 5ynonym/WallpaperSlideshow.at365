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
        try
        {
            ApplicationController.Instance.Initialize(config, dispatcherForm);
        }
        catch (Exception ex)
        {
            AppLog.Error("起動", ex);
            MessageBox.Show($"起動できません: {ex.Message}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            ApplicationController.Instance.Dispose();
            return;
        }
        try
        {
            Application.Run(dispatcherForm);
        }
        finally
        {
            ApplicationController.Instance.Dispose();
        }
    }
}
