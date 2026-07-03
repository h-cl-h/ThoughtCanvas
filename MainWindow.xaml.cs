using System;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;

namespace BmapEditor
{
    public partial class MainWindow : Window
    {
        private enum DocType { Theme, Ui }
        private DocType _doc = DocType.Theme;
        private bool _ready;                 // 控件与事件已挂好
        private bool _webReady;              // WebView2 已初始化
        private string _currentPath;         // 当前文件路径（保存用）

        // 最近使用列表存放位置：%AppData%\BMAP配色编辑器\recent.json
        private static readonly string RecentPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BMAP配色编辑器", "recent.json");

        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private const string CssTemplate =
            "body{background:#1a1a1a!important}\n" +
            "#toolbar{background:#111!important;border-bottom-color:#333!important}\n" +
            ".title,.card-inner{color:#eee!important}\n" +
            ".btn{background:#222!important;color:#ddd!important;border-color:#444!important}\n" +
            ".btn.primary{background:#e91e63!important;color:#fff!important;border-color:#e91e63!important}\n" +
            ".card{background:#242424!important;border-color:#3a3a3a!important}\n" +
            ".node.sel .card{border-color:#e91e63!important;box-shadow:0 0 0 2px rgba(233,30,99,.3)!important}";

        public MainWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // 下拉数据
            CmbThemePreset.ItemsSource = Skins.ThemePresets.Select(p => p.Name).ToList();
            CmbThemePreset.SelectedIndex = 0;
            CmbBase.ItemsSource = Skins.BuiltinUI;
            CmbBase.SelectedIndex = 0;

            WireEvents();
            NewTheme();
            _ready = true;

            ShowHome();   // 启动先进开始页；WebView2 首次进编辑器时再懒加载
        }

        // 懒加载 WebView2（第一次进入编辑器时初始化，避免开始页被原生窗口盖住 + 加快启动）
        private async System.Threading.Tasks.Task EnsureWebAsync()
        {
            if (_webReady) return;
            try
            {
                await Web.EnsureCoreWebView2Async();
                string skinsDir = Path.Combine(AppContext.BaseDirectory, "assets", "skins");
                if (Directory.Exists(skinsDir))
                    Web.CoreWebView2.SetVirtualHostNameToFolderMapping(
                        "appassets", skinsDir, CoreWebView2HostResourceAccessKind.Allow);
                Web.CoreWebView2.Settings.AreDevToolsEnabled = false;
                Web.CoreWebView2.Settings.IsStatusBarEnabled = false;
                _webReady = true;
            }
            catch (Exception ex)
            {
                TxtStatus.Text = "预览初始化失败（缺少 WebView2 运行时？）：" + ex.Message;
            }
        }

        // ================= 开始页 / 编辑器 切换 =================
        private void ShowHome()
        {
            EditorRoot.Visibility = Visibility.Collapsed;   // 收起编辑器（连同 WebView2 原生窗口），否则会盖住开始页
            HomeRoot.Visibility = Visibility.Visible;
            RenderRecent();
        }

        private async void ShowEditor()
        {
            HomeRoot.Visibility = Visibility.Collapsed;
            EditorRoot.Visibility = Visibility.Visible;
            await EnsureWebAsync();
            RefreshAll();
        }

        // ================= 最近使用 =================
        private class RecentItem { public string path { get; set; } public string name { get; set; } public string type { get; set; } }

        private System.Collections.Generic.List<RecentItem> LoadRecent()
        {
            try
            {
                if (File.Exists(RecentPath))
                    return JsonSerializer.Deserialize<System.Collections.Generic.List<RecentItem>>(File.ReadAllText(RecentPath))
                           ?? new System.Collections.Generic.List<RecentItem>();
            }
            catch { }
            return new System.Collections.Generic.List<RecentItem>();
        }

        private void SaveRecent(System.Collections.Generic.List<RecentItem> list)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(RecentPath));
                File.WriteAllText(RecentPath, JsonSerializer.Serialize(list, JsonOpts), new System.Text.UTF8Encoding(false));
            }
            catch { }
        }

        private void AddRecent(string path, string type)
        {
            var list = LoadRecent();
            list.RemoveAll(x => string.Equals(x.path, path, StringComparison.OrdinalIgnoreCase));
            list.Insert(0, new RecentItem { path = path, name = Path.GetFileName(path), type = type });
            if (list.Count > 12) list = list.GetRange(0, 12);
            SaveRecent(list);
        }

        private void RenderRecent()
        {
            RecentList.Children.Clear();
            var list = LoadRecent().Where(x => !string.IsNullOrEmpty(x.path) && File.Exists(x.path)).ToList();
            if (list.Count == 0)
            {
                RecentList.Children.Add(new TextBlock { Text = "暂无最近文件 —— 新建或打开后会出现在这里", Foreground = Brush("#8a90a0"), FontSize = 13 });
                return;
            }
            foreach (var it in list) RecentList.Children.Add(CreateRecentRow(it));
        }

        private Border CreateRecentRow(RecentItem it)
        {
            var b = new Border
            {
                Background = Brush("#ffffff"),
                BorderBrush = Brush("#eceef2"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 0, 0, 8),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            string icon = it.type == "UI方案" ? "🖼" : it.type == "主题色" ? "🎨" : "📄";
            var ic = new TextBlock { Text = icon, FontSize = 20, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            Grid.SetColumn(ic, 0);

            var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            mid.Children.Add(new TextBlock { Text = it.name, FontSize = 14, FontWeight = FontWeights.Medium, Foreground = Brush("#23262e") });
            mid.Children.Add(new TextBlock { Text = it.path, FontSize = 11.5, Foreground = Brush("#8a90a0"), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0) });
            Grid.SetColumn(mid, 1);

            var chip = new Border { Background = Brush("#eef1f6"), CornerRadius = new CornerRadius(7), Padding = new Thickness(9, 3, 9, 3), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            chip.Child = new TextBlock { Text = it.type ?? "文件", FontSize = 11.5, Foreground = Brush("#5b6070") };
            Grid.SetColumn(chip, 2);

            g.Children.Add(ic); g.Children.Add(mid); g.Children.Add(chip);
            b.Child = g;
            string p = it.path;
            b.MouseLeftButtonUp += (s, e) => OpenPath(p);
            return b;
        }

        private static SolidColorBrush Brush(string hex)
        {
            var c = ColorUtil.ParseHex(hex);
            return c != null ? new SolidColorBrush(Color.FromRgb(c.Value.R, c.Value.G, c.Value.B)) : Brushes.Transparent;
        }

        private void WireEvents()
        {
            BtnHome.Click += (s, e) => ShowHome();
            HomeNewBtn.Click += (s, e) => { NewTheme(); ShowEditor(); };
            HomeOpenBtn.Click += (s, e) => OpenFile();
            BtnNewTheme.Click += (s, e) => { NewTheme(); };
            BtnNewUi.Click += (s, e) => { NewUi(); };
            BtnOpen.Click += (s, e) => OpenFile();
            BtnSave.Click += (s, e) => Save(false);
            BtnSaveAs.Click += (s, e) => Save(true);

            BtnLoadPreset.Click += (s, e) => LoadPreset();

            // 取色按钮
            BtnAccentPick.Click += (s, e) => PickColor(TxtAccentHex);
            BtnSoftPick.Click += (s, e) => PickColor(TxtSoftHex);
            BtnUiAccentPick.Click += (s, e) => PickColor(TxtUiAccentHex);
            BtnUiSoftPick.Click += (s, e) => PickColor(TxtUiSoftHex);

            // 任意输入 -> 刷新
            foreach (var tb in new[]{ TxtThemeName, TxtAccentHex, TxtSoftHex, TxtSide, TxtTBlank, TxtTSample, TxtTOpen,
                                      TxtUiName, TxtUiAccentHex, TxtUiSoftHex, TxtCss })
                tb.TextChanged += (s, e) => RefreshAll();

            foreach (var cb in new[]{ ChkSoftAuto, ChkSideAuto, ChkTBlankAuto, ChkTSampleAuto, ChkTOpenAuto, ChkUiSoftAuto })
                cb.Click += (s, e) => RefreshAll();

            CmbBase.SelectionChanged += (s, e) => RefreshAll();

            RbPvStart.Checked += (s, e) => { if (_ready) RefreshAll(); };
            RbPvMain.Checked += (s, e) => { if (_ready) RefreshAll(); };

            RbModeA.Checked += (s, e) => { if (_ready) { ModeAPanel.Visibility = Visibility.Visible; ModeBPanel.Visibility = Visibility.Collapsed; RefreshAll(); } };
            RbModeB.Checked += (s, e) => { if (_ready) { ModeAPanel.Visibility = Visibility.Collapsed; ModeBPanel.Visibility = Visibility.Visible; RefreshAll(); } };

            BtnInsertTpl.Click += (s, e) => { TxtCss.Text = CssTemplate; };
        }

        // ================= 新建 =================
        private void NewTheme()
        {
            _doc = DocType.Theme; _currentPath = null;
            ThemePanel.Visibility = Visibility.Visible;
            UiPanel.Visibility = Visibility.Collapsed;
            TxtCurrent.Text = "未命名主题色（.bmaptheme）";

            TxtThemeName.Text = "自定义配色";
            TxtAccentHex.Text = "#5b8def";
            ChkSoftAuto.IsChecked = true;
            ChkSideAuto.IsChecked = true;
            ChkTBlankAuto.IsChecked = true;
            ChkTSampleAuto.IsChecked = true;
            ChkTOpenAuto.IsChecked = true;
            RefreshAll();
        }

        private void NewUi()
        {
            _doc = DocType.Ui; _currentPath = null;
            ThemePanel.Visibility = Visibility.Collapsed;
            UiPanel.Visibility = Visibility.Visible;
            TxtCurrent.Text = "未命名 UI 方案（.bmapui）";

            TxtUiName.Text = "自定义 UI";
            RbModeA.IsChecked = true;
            ModeAPanel.Visibility = Visibility.Visible;
            ModeBPanel.Visibility = Visibility.Collapsed;
            CmbBase.SelectedIndex = 0;
            TxtUiAccentHex.Text = "#5b8def";
            ChkUiSoftAuto.IsChecked = true;
            if (string.IsNullOrWhiteSpace(TxtCss.Text)) TxtCss.Text = CssTemplate;
            RefreshAll();
        }

        private void LoadPreset()
        {
            int i = CmbThemePreset.SelectedIndex;
            if (i < 0 || i >= Skins.ThemePresets.Count) return;
            var p = Skins.ThemePresets[i];
            TxtThemeName.Text = p.Name;
            TxtAccentHex.Text = p.Accent;
            // 载入模板时把各字段设为「手动」并填入原值，便于微调
            ChkSoftAuto.IsChecked = false; TxtSoftHex.Text = p.AccentSoft;
            ChkSideAuto.IsChecked = false; TxtSide.Text = p.SideGrad;
            ChkTBlankAuto.IsChecked = false; TxtTBlank.Text = p.ThumbBlank;
            ChkTSampleAuto.IsChecked = false; TxtTSample.Text = p.ThumbSample;
            ChkTOpenAuto.IsChecked = false; TxtTOpen.Text = p.ThumbOpen;
            RefreshAll();
        }

        // ================= 取色 =================
        private void PickColor(TextBox target)
        {
            using (var dlg = new System.Windows.Forms.ColorDialog { FullOpen = true, AnyColor = true })
            {
                var cur = ColorUtil.ParseHex(target.Text);
                if (cur != null)
                    dlg.Color = System.Drawing.Color.FromArgb(cur.Value.R, cur.Value.G, cur.Value.B);
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    target.Text = ColorUtil.FromRgb(dlg.Color.R, dlg.Color.G, dlg.Color.B);
            }
        }

        private static void SetSwatch(Border b, string hex)
        {
            var p = ColorUtil.ParseHex(hex);
            b.Background = p != null
                ? new SolidColorBrush(Color.FromRgb(p.Value.R, p.Value.G, p.Value.B))
                : Brushes.Transparent;
        }

        // ================= 解析当前编辑内容 =================
        private class ThemeData
        {
            public string Name, Accent, AccentSoft, SideGrad, ThumbBlank, ThumbSample, ThumbOpen;
            public bool Valid; public string Error;
        }

        private ThemeData GatherTheme()
        {
            var t = new ThemeData();
            t.Name = string.IsNullOrWhiteSpace(TxtThemeName.Text) ? "自定义配色" : TxtThemeName.Text.Trim();
            string ac = ColorUtil.NormalizeHex(TxtAccentHex.Text);
            if (ac == null) { t.Valid = false; t.Error = "主色 accent 填个颜色，如 #0a8a6f（# 可省略，也支持 3 位简写）"; return t; }
            t.Accent = ac;

            string d = ColorUtil.HexDark(ac, 0.8);
            string l = ColorUtil.HexDark(ac, 1.18);

            t.AccentSoft = (ChkSoftAuto.IsChecked == true)
                ? ColorUtil.HexDark(ac, 1.7)
                : (ColorUtil.NormalizeHex(TxtSoftHex.Text) ?? ColorUtil.HexDark(ac, 1.7));
            t.SideGrad = (ChkSideAuto.IsChecked == true) ? $"160deg,{ac},{d}" : (TxtSide.Text ?? "").Trim();
            t.ThumbBlank = (ChkTBlankAuto.IsChecked == true) ? $"135deg,{ac},{l}" : (TxtTBlank.Text ?? "").Trim();
            t.ThumbSample = (ChkTSampleAuto.IsChecked == true) ? $"135deg,{d},{ac}" : (TxtTSample.Text ?? "").Trim();
            t.ThumbOpen = (ChkTOpenAuto.IsChecked == true) ? $"135deg,{ac},{d}" : (TxtTOpen.Text ?? "").Trim();
            t.Valid = true;
            return t;
        }

        // ================= 刷新（更新联动 + 预览 + 校验） =================
        private void RefreshAll()
        {
            if (!_ready) return;

            if (_doc == DocType.Theme)
            {
                var t = GatherTheme();
                // 自动项：把算出来的值回填到禁用的输入框，做到所见即所得
                TxtSoftHex.IsEnabled = ChkSoftAuto.IsChecked != true; BtnSoftPick.IsEnabled = TxtSoftHex.IsEnabled;
                TxtSide.IsEnabled = ChkSideAuto.IsChecked != true;
                TxtTBlank.IsEnabled = ChkTBlankAuto.IsChecked != true;
                TxtTSample.IsEnabled = ChkTSampleAuto.IsChecked != true;
                TxtTOpen.IsEnabled = ChkTOpenAuto.IsChecked != true;
                if (ChkSoftAuto.IsChecked == true) TxtSoftHex.Text = t.AccentSoft;
                if (ChkSideAuto.IsChecked == true) TxtSide.Text = t.SideGrad;
                if (ChkTBlankAuto.IsChecked == true) TxtTBlank.Text = t.ThumbBlank;
                if (ChkTSampleAuto.IsChecked == true) TxtTSample.Text = t.ThumbSample;
                if (ChkTOpenAuto.IsChecked == true) TxtTOpen.Text = t.ThumbOpen;

                SetSwatch(SwAccent, t.Accent);
                SetSwatch(SwSoft, t.AccentSoft);

                TxtStatus.Text = t.Valid ? "✔ 主题色可用 · " + t.Name : "✘ " + t.Error;
                if (_webReady) NavigatePreview(BuildThemePreview(t, RbPvStart.IsChecked == true));
            }
            else
            {
                bool modeA = RbModeA.IsChecked == true;
                if (modeA)
                {
                    string ac = ColorUtil.NormalizeHex(TxtUiAccentHex.Text);
                    bool okAc = ac != null;
                    string baseAc = okAc ? ac : "#5b8def";
                    string soft = (ChkUiSoftAuto.IsChecked == true)
                        ? ColorUtil.HexDark(baseAc, 1.7)
                        : (ColorUtil.NormalizeHex(TxtUiSoftHex.Text) ?? ColorUtil.HexDark(baseAc, 1.7));
                    TxtUiSoftHex.IsEnabled = ChkUiSoftAuto.IsChecked != true; BtnUiSoftPick.IsEnabled = TxtUiSoftHex.IsEnabled;
                    if (ChkUiSoftAuto.IsChecked == true) TxtUiSoftHex.Text = soft;
                    SetSwatch(SwUiAccent, ac);
                    SetSwatch(SwUiSoft, soft);
                    var b = (BaseSkin)CmbBase.SelectedItem ?? Skins.BuiltinUI[0];
                    TxtStatus.Text = "✔ UI 方案（写法A：" + b.Name + (okAc ? " + 主色" : "") + "）";
                    if (_webReady) NavigatePreview(BuildUiPreviewA(b, okAc ? ac : null, soft, RbPvStart.IsChecked == true));
                }
                else
                {
                    string css = TxtCss.Text ?? "";
                    TxtStatus.Text = string.IsNullOrWhiteSpace(css) ? "✘ CSS 为空" : "✔ UI 方案（写法B：整段 CSS）";
                    if (_webReady) NavigatePreview(BuildUiPreviewB(css, RbPvStart.IsChecked == true));
                }
            }
        }

        private void NavigatePreview(string html)
        {
            try { Web.NavigateToString(html); } catch { /* 忽略预览异常 */ }
        }

        // ================= 预览 HTML 构建 =================
        private static string Doc(string head, string body)
        {
            return "<!doctype html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\"><style>"
                 + Skins.PreviewBaseCss + "</style>" + head + "</head><body>" + body + "</body></html>";
        }

        // 主题色所有参数拼成一段 CSS 变量
        private static string ThemeVars(ThemeData t)
        {
            return $":root{{--accent:{t.Accent};--accent-soft:{t.AccentSoft};" +
                   $"--side-grad:linear-gradient({t.SideGrad});" +
                   $"--thumb-blank:linear-gradient({t.ThumbBlank});" +
                   $"--thumb-sample:linear-gradient({t.ThumbSample});" +
                   $"--thumb-open:linear-gradient({t.ThumbOpen});}}";
        }

        private static string BuildThemePreview(ThemeData t, bool start)
        {
            if (!t.Valid) return Doc("", "<div style='padding:24px;color:#c00'>主色不合法，无法预览</div>");
            string vars = ThemeVars(t);
            // 开始页：StartCss 先加载（带默认值），再用主题变量覆盖，故变量样式要放在其后
            if (start)
                return Doc("<style>" + Skins.StartCss + "</style><style>" + vars + "</style>", Skins.StartMarkup);
            return Doc("<style>" + vars + "</style>", Skins.PreviewMarkup);
        }

        private static string BuildUiPreviewA(BaseSkin b, string accent, string accentSoft, bool start)
        {
            string head = "";
            foreach (var f in b.Files)
                head += "<link rel=\"stylesheet\" href=\"https://appassets/" + f + "\">";
            if (accent != null)
            {
                string soft = ColorUtil.IsHex6(accentSoft) ? accentSoft : ColorUtil.HexDark(accent, 1.7);
                head += "<style>:root{--accent:" + accent + "!important;--accent-soft:" + soft + "!important;}"
                      + ".btn.primary,.btn.toggle-on{background:" + accent + "!important;border-color:" + accent + "!important;}</style>";
            }
            if (start) return Doc("<style>" + Skins.StartCss + "</style>" + head, Skins.StartMarkup);
            return Doc(head, Skins.PreviewMarkup);
        }

        private static string BuildUiPreviewB(string css, bool start)
        {
            if (start) return Doc("<style>" + Skins.StartCss + "</style><style>" + css + "</style>", Skins.StartMarkup);
            return Doc("<style>" + css + "</style>", Skins.PreviewMarkup);
        }

        // ================= 保存 =================
        private void Save(bool forceDialog)
        {
            string json, ext, filter, defName;
            if (_doc == DocType.Theme)
            {
                var t = GatherTheme();
                if (!t.Valid) { MessageBox.Show(this, t.Error, "无法保存", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
                var obj = new
                {
                    app = "brace-mindmap-theme",
                    version = 1,
                    name = t.Name,
                    accent = t.Accent,
                    accentSoft = t.AccentSoft,
                    sideGrad = t.SideGrad,
                    thumbBlank = t.ThumbBlank,
                    thumbSample = t.ThumbSample,
                    thumbOpen = t.ThumbOpen
                };
                json = JsonSerializer.Serialize(obj, JsonOpts);
                ext = ".bmaptheme"; filter = "主题色文件 (*.bmaptheme)|*.bmaptheme"; defName = t.Name;
            }
            else
            {
                string name = string.IsNullOrWhiteSpace(TxtUiName.Text) ? "自定义 UI" : TxtUiName.Text.Trim();
                if (RbModeA.IsChecked == true)
                {
                    var b = (BaseSkin)CmbBase.SelectedItem ?? Skins.BuiltinUI[0];
                    string ac = ColorUtil.NormalizeHex(TxtUiAccentHex.Text);
                    if (ac != null)
                    {
                        string soft = (ChkUiSoftAuto.IsChecked == true) ? ColorUtil.HexDark(ac, 1.7) : (ColorUtil.NormalizeHex(TxtUiSoftHex.Text) ?? ColorUtil.HexDark(ac, 1.7));
                        var obj = new { app = "brace-mindmap-ui", name, @base = b.Id, accent = ac, accentSoft = soft };
                        json = JsonSerializer.Serialize(obj, JsonOpts);
                    }
                    else
                    {
                        var obj = new { app = "brace-mindmap-ui", name, @base = b.Id };
                        json = JsonSerializer.Serialize(obj, JsonOpts);
                    }
                }
                else
                {
                    string css = TxtCss.Text ?? "";
                    if (string.IsNullOrWhiteSpace(css)) { MessageBox.Show(this, "CSS 为空", "无法保存", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
                    var obj = new { app = "brace-mindmap-ui", name, css };
                    json = JsonSerializer.Serialize(obj, JsonOpts);
                }
                ext = ".bmapui"; filter = "UI 方案文件 (*.bmapui)|*.bmapui"; defName = name;
            }

            string path = _currentPath;
            if (forceDialog || string.IsNullOrEmpty(path))
            {
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = filter + "|所有文件 (*.*)|*.*",
                    FileName = SanitizeFileName(defName) + ext,
                    DefaultExt = ext
                };
                if (dlg.ShowDialog(this) != true) return;
                path = dlg.FileName;
            }

            try
            {
                File.WriteAllText(path, json, new System.Text.UTF8Encoding(false));
                _currentPath = path;
                TxtCurrent.Text = Path.GetFileName(path);
                TxtStatus.Text = "已保存：" + path;
                AddRecent(path, _doc == DocType.Theme ? "主题色" : "UI方案");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "保存失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string SanitizeFileName(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return string.IsNullOrWhiteSpace(s) ? "未命名" : s;
        }

        // ================= 打开 =================
        private void OpenFile()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "BMAP 文件 (*.bmaptheme;*.bmapui)|*.bmaptheme;*.bmapui|所有文件 (*.*)|*.*"
            };
            if (dlg.ShowDialog(this) != true) return;
            OpenPath(dlg.FileName);
        }

        // 按路径打开（打开对话框、最近使用点击都走这里）
        private void OpenPath(string path)
        {
            if (!File.Exists(path))
            {
                MessageBox.Show(this, "文件不存在：\n" + path, "打开失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                RenderRecent();
                return;
            }
            try
            {
                string text = File.ReadAllText(path);
                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;
                string app = root.TryGetProperty("app", out var a) ? a.GetString() : null;

                string type;
                if (app == "brace-mindmap-ui") { LoadUiFrom(root); type = "UI方案"; }
                else if (app == "brace-mindmap-theme") { LoadThemeFrom(root); type = "主题色"; }
                else { MessageBox.Show(this, "无法识别：缺少正确的 app 标识（应为 brace-mindmap-theme 或 brace-mindmap-ui）", "打开失败", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

                _currentPath = path;
                TxtCurrent.Text = Path.GetFileName(path);
                TxtStatus.Text = "已打开：" + path;
                AddRecent(path, type);
                ShowEditor();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "打开失败（不是合法 JSON？）", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string Str(JsonElement e, string key)
        {
            return e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        }

        private void LoadThemeFrom(JsonElement e)
        {
            NewTheme();
            TxtThemeName.Text = Str(e, "name") ?? "自定义配色";
            TxtAccentHex.Text = Str(e, "accent") ?? "#5b8def";
            // 有值的字段切到手动并填入；没有的保持自动
            SetManualIfPresent(e, "accentSoft", ChkSoftAuto, TxtSoftHex);
            SetManualIfPresent(e, "sideGrad", ChkSideAuto, TxtSide);
            SetManualIfPresent(e, "thumbBlank", ChkTBlankAuto, TxtTBlank);
            SetManualIfPresent(e, "thumbSample", ChkTSampleAuto, TxtTSample);
            SetManualIfPresent(e, "thumbOpen", ChkTOpenAuto, TxtTOpen);
            RefreshAll();
        }

        private void SetManualIfPresent(JsonElement e, string key, CheckBox auto, TextBox box)
        {
            string v = Str(e, key);
            if (!string.IsNullOrEmpty(v)) { auto.IsChecked = false; box.Text = v; }
        }

        private void LoadUiFrom(JsonElement e)
        {
            NewUi();
            TxtUiName.Text = Str(e, "name") ?? "自定义 UI";
            string css = Str(e, "css");
            if (!string.IsNullOrEmpty(css))
            {
                RbModeB.IsChecked = true;
                TxtCss.Text = css;
            }
            else
            {
                RbModeA.IsChecked = true;
                string bs = Str(e, "base") ?? "default";
                int idx = Skins.BuiltinUI.FindIndex(x => x.Id == bs);
                CmbBase.SelectedIndex = idx >= 0 ? idx : 0;
                string ac = Str(e, "accent");
                if (!string.IsNullOrEmpty(ac)) TxtUiAccentHex.Text = ac; else TxtUiAccentHex.Text = "";
                string soft = Str(e, "accentSoft");
                if (!string.IsNullOrEmpty(soft)) { ChkUiSoftAuto.IsChecked = false; TxtUiSoftHex.Text = soft; }
            }
            RefreshAll();
        }
    }
}
