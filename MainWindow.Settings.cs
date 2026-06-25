using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using IOPath2 = System.IO.Path;

namespace ThoughtCanvas
{
    // 设置面板（通用 / 快捷键 / 大括号 / 关于）+ 可自定义快捷键，持久到 settings.json
    public partial class MainWindow
    {
        // 可改键的四个动作（默认左手区友好键）
        readonly Dictionary<string, Key> shortcuts = new Dictionary<string, Key>
        {
            ["addChild"] = Key.Tab,
            ["addSibling"] = Key.Enter,
            ["edit"] = Key.F2,
            ["delete"] = Key.Delete,
        };
        static readonly (string key, string label)[] ShortcutRows =
        {
            ("addChild", "添加子主题"),
            ("addSibling", "添加同级主题"),
            ("edit", "编辑文字"),
            ("delete", "删除主题"),
        };

        string lang = "zh";   // i18n：zh | en

        static string SettingsPath =>
            IOPath2.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ThoughtCanvas", "settings.json");

        void LoadSettings()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return;
                using var doc = JsonDocument.Parse(File.ReadAllText(SettingsPath));
                var r = doc.RootElement;
                if (r.TryGetProperty("lang", out var lg) && lg.ValueKind == JsonValueKind.String) lang = lg.GetString();
                if (r.TryGetProperty("shortcuts", out var sc) && sc.ValueKind == JsonValueKind.Object)
                    foreach (var p in sc.EnumerateObject())
                        if (shortcuts.ContainsKey(p.Name) && p.Value.ValueKind == JsonValueKind.String
                            && Enum.TryParse<Key>(p.Value.GetString(), out var k)) shortcuts[p.Name] = k;
            }
            catch { }
        }

        void SaveSettings()
        {
            try
            {
                Directory.CreateDirectory(IOPath2.GetDirectoryName(SettingsPath));
                var sc = new JsonObject();
                foreach (var kv in shortcuts) sc[kv.Key] = kv.Value.ToString();
                var obj = new JsonObject { ["lang"] = lang, ["shortcuts"] = sc };
                File.WriteAllText(SettingsPath, obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        static readonly (string label, string val)[] NumberingOptions =
        {
            ("无", ""), ("1.2.3", "num"), ("A.B.C", "alpha"), ("a.b.c", "lalpha"), ("I.II.III", "roman"),
        };

        void OpenSettings()
        {
            var w = new Window { Title = T("设置", "Settings"), Width = 420, SizeToContent = SizeToContent.Height, MaxHeight = 640, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this, ResizeMode = ResizeMode.NoResize, Background = Brushes.White };
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var root = new StackPanel { Margin = new Thickness(22, 18, 22, 18) };

            TextBlock Section(string s) => new TextBlock { Text = s, FontWeight = FontWeights.Bold, FontSize = 14, Foreground = Accent, Margin = new Thickness(0, 14, 0, 8) };

            // —— 通用 ——
            root.Children.Add(Section(T("通用", "General")));
            var numPanel = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            numPanel.Children.Add(new TextBlock { Text = T("主题编号", "Numbering"), Foreground = Ink, VerticalAlignment = VerticalAlignment.Center, Width = 130 });
            var numCombo = new ComboBox { Width = 160, HorizontalAlignment = HorizontalAlignment.Left };
            foreach (var o in NumberingOptions) numCombo.Items.Add(o.label);
            numCombo.SelectedIndex = Math.Max(0, Array.FindIndex(NumberingOptions, o => o.val == numbering));
            numCombo.SelectionChanged += (s, e) => { numbering = NumberingOptions[numCombo.SelectedIndex].val; Rebuild(); };
            numPanel.Children.Add(numCombo);
            root.Children.Add(numPanel);

            var langPanel = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            langPanel.Children.Add(new TextBlock { Text = T("界面语言", "Language"), Foreground = Ink, VerticalAlignment = VerticalAlignment.Center, Width = 130 });
            var langCombo = new ComboBox { Width = 160, HorizontalAlignment = HorizontalAlignment.Left };
            langCombo.Items.Add("中文"); langCombo.Items.Add("English");
            langCombo.SelectedIndex = lang == "en" ? 1 : 0;
            langCombo.SelectionChanged += (s, e) => { lang = langCombo.SelectedIndex == 1 ? "en" : "zh"; SaveSettings(); ApplyLang(); w.Close(); OpenSettings(); };
            langPanel.Children.Add(langCombo);
            root.Children.Add(langPanel);

            // —— 快捷键 ——
            root.Children.Add(Section(T("快捷键（点按钮后按下新键）", "Shortcuts (click then press a key)")));
            foreach (var (key, label) in ShortcutRows)
            {
                string actionKey = key;
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
                row.Children.Add(new TextBlock { Text = T(label, label), Foreground = Ink, VerticalAlignment = VerticalAlignment.Center, Width = 130 });
                var btn = new Button { Content = shortcuts[actionKey].ToString(), Style = (Style)Resources["Btn"], MinWidth = 110, HorizontalAlignment = HorizontalAlignment.Left, Focusable = true };
                btn.Click += (s, e) => { btn.Content = T("按下新键…", "press a key…"); btn.Focus(); };
                btn.PreviewKeyDown += (s, e) =>
                {
                    if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl || e.Key == Key.LeftShift || e.Key == Key.RightShift || e.Key == Key.LeftAlt || e.Key == Key.RightAlt) return;
                    e.Handled = true;
                    var k = e.Key == Key.System ? e.SystemKey : e.Key;
                    shortcuts[actionKey] = k;
                    btn.Content = k.ToString();
                    SaveSettings();
                };
                row.Children.Add(btn);
                root.Children.Add(row);
            }
            root.Children.Add(new TextBlock { Text = T("方向键导航固定不变。", "Arrow keys navigate (fixed)."), Foreground = Muted, FontSize = 12, Margin = new Thickness(0, 2, 0, 0) });

            // —— 大括号 / 排版 ——
            root.Children.Add(Section(T("排版", "Layout")));
            var compPanel = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            compPanel.Children.Add(new TextBlock { Text = T("整理密度", "Density"), Foreground = Ink, VerticalAlignment = VerticalAlignment.Center, Width = 130 });
            var compCombo = new ComboBox { Width = 160, HorizontalAlignment = HorizontalAlignment.Left };
            compCombo.Items.Add(T("普通", "Normal")); compCombo.Items.Add(T("纵向压缩", "Compact")); compCombo.Items.Add(T("横向错位", "Staggered"));
            compCombo.SelectedIndex = Math.Max(0, Math.Min(2, doc.CompactMode));
            compCombo.SelectionChanged += (s, e) => { doc.CompactMode = compCombo.SelectedIndex; Rebuild(); Fit(); UpdateTidyBtn(); };
            compPanel.Children.Add(compCombo);
            root.Children.Add(compPanel);

            // —— 关于 ——
            root.Children.Add(Section(T("关于", "About")));
            root.Children.Add(new TextBlock { Text = "ThoughtCanvas C# 版 · V0.0.2\n大括号 + 蜘蛛网思维导图 · .bmap 与网页版互通", Foreground = Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap });

            scroll.Content = root;
            w.Content = scroll;
            w.ShowDialog();
        }
    }
}
