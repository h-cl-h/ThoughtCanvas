using System.Reflection;
using System.Text.Json;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using BmapTextStyleEditor;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        var root = Path.Combine(Path.GetTempPath(), "bmap-multi-style-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); var target = Path.Combine(root, "main.js"); File.WriteAllText(target, "// test");
        Environment.SetEnvironmentVariable("BMAP_TEXT_STYLE_EDITOR_DATA_DIR", Path.Combine(root, "editor-data"));
        try
        {
            _ = new Application(); var window = new MainWindow(); var flags = BindingFlags.Instance | BindingFlags.NonPublic; var type = typeof(MainWindow);
            type.GetField("connectedRoot", flags)!.SetValue(window, root); type.GetField("connectedTarget", flags)!.SetValue(window, target); var sync = type.GetMethod("Sync", flags)!; var newStyle = type.GetMethod("NewStyle", flags)!; var styleField = type.GetField("style", flags)!; var nameBox = (TextBox)window.FindName("NameBox"); var idBox = (TextBox)window.FindName("IdBox");
            void Prepare(string name) { var s = (TextBoxStyle)styleField.GetValue(window)!; s.TextRegion = new TextRegion { X = 10, Y = 10, W = 80, H = 80 }; s.Layers.Add(new StyleLayer { X = 5, Y = 5, W = 90, H = 90 }); nameBox.Text = name; idBox.Text = "my-text-style"; }
            Prepare("第一个样式"); sync.Invoke(window, [false]); newStyle.Invoke(window, null); Prepare("第二个样式"); sync.Invoke(window, [false]);
            var file = Path.Combine(root, "text-styles", "custom-text-styles.json"); var lib = JsonSerializer.Deserialize<StyleLibrary>(File.ReadAllText(file), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            if (lib.Styles.Count != 2 || lib.Styles.Select(x => x.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 2 || !lib.Styles.Any(x => x.Id == "my-text-style-2")) throw new Exception("第二个样式被覆盖");
            nameBox.Text = "第二个样式修改"; sync.Invoke(window, [false]); lib = JsonSerializer.Deserialize<StyleLibrary>(File.ReadAllText(file), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!; if (lib.Styles.Count != 2) throw new Exception("继续编辑同一样式时错误新增");
            Console.WriteLine("MULTI_STYLE_SYNC_SMOKE_OK count=2 ids=" + string.Join(',', lib.Styles.Select(x => x.Id))); window.Close();
        }
        finally { Environment.SetEnvironmentVariable("BMAP_TEXT_STYLE_EDITOR_DATA_DIR", null); Directory.Delete(root, true); }
    }
}
