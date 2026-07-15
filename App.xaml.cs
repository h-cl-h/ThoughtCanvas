using System.IO;
using System.Windows;
namespace BmapTextStyleEditor;
public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += (_, e) =>
        {
            LogException(e.Exception);
            MessageBox.Show("本次操作遇到异常，已阻止程序退出。可继续操作；诊断记录已保存。\n\n" + e.Exception.Message, "操作未完成", MessageBoxButton.OK, MessageBoxImage.Warning);
            e.Handled = true;
        };
    }
    public static void LogException(Exception ex)
    {
        try { var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BMAPTextStyleEditor"); Directory.CreateDirectory(dir); File.AppendAllText(Path.Combine(dir, "error.log"), $"[{DateTime.Now:O}] {ex}\n\n"); } catch { }
    }
}
