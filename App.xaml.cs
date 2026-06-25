using System;
using System.IO;
using System.Threading;
using System.Windows;

namespace ThoughtCanvas
{
    public partial class App : Application
    {
        static Mutex _instanceMutex;   // 单实例锁（基于固定名 brace-mindmap）

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 开发期自测：--selftest <out.png> 离屏渲染一张图后退出（不弹窗、不占单实例锁）
            if (e.Args.Length >= 2 && e.Args[0] == "--selftest")
            {
                try { var w = new MainWindow(); w.SelfTest(e.Args[1]); } catch (Exception ex) { LogError(ex); File.WriteAllText(e.Args[1] + ".err", ex.ToString()); }
                Shutdown();
                return;
            }

            // 单实例：已在运行就提示并退出（保持唯一一份，避免 .bmap 文件锁冲突）
            _instanceMutex = new Mutex(true, "ThoughtCanvas-brace-mindmap-singleton", out bool isNew);
            if (!isNew)
            {
                MessageBox.Show("ThoughtCanvas 已经在运行了。", "ThoughtCanvas", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            // 全局兜底：UI 线程上的异常不再让软件闪退，而是记录 + 提示后继续运行
            DispatcherUnhandledException += (s, ev) =>
            {
                LogError(ev.Exception);
                MessageBox.Show("刚才那一步出错了，已记录，软件会继续运行。\n\n" + ev.Exception.Message,
                    "ThoughtCanvas", MessageBoxButton.OK, MessageBoxImage.Warning);
                ev.Handled = true;
            };
            AppDomain.CurrentDomain.UnhandledException += (s, ev) => LogError(ev.ExceptionObject as Exception);
        }

        public static void LogError(Exception ex)
        {
            try
            {
                if (ex == null) return;
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ThoughtCanvas");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "error.log"), DateTime.Now + "\n" + ex + "\n\n");
            }
            catch { }
        }
    }
}
