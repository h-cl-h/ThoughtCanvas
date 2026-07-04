using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace BmapUiEditor
{
    /// <summary>
    /// 应用框架：开始页 / 最近使用 / 组件库 / 打开保存(含 V0.0.1 旧档迁移) / 部件调色面板 / WebView2 真实预览。
    /// 画布与图层交互在 MainWindowEditor.cs（partial）。
    /// </summary>
    public partial class MainWindow : Window
    {
        private bool _ready;
        private bool _webReady;
        private string _currentPath;
        private bool _dirty;
        private bool _loadingPanel;

        private static readonly string RecentPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BMAP界面编辑器", "recent.json");

        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        // 一键整套原版时的图层顺序（从底到顶）
        private static readonly string[] FullSetOrder =
        {
            "stage", "brace", "link", "card", "cardSel", "toolbar", "title", "btn", "btnPrimary", "fname", "floatbar", "menu"
        };

        public MainWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Closing += OnClosingWindow;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Prefs.Load();
            InitAdorners();
            InitDesign();
            WireEvents();
            WireShapePanel();
            WireInkImagePanels();
            WirePen();
            WireTextPanel();
            NewDoc();
            _ready = true;
            ShowHome();
        }

        private void OnClosingWindow(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_dirty) return;
            var r = MessageBox.Show(this, "有没保存的修改，直接退出会丢失。仍然退出？", "未保存",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) e.Cancel = true;
        }

        // ================= WebView2 =================
        private string _pendingInject;   // 真实预览：导航完成后要注入的皮肤 CSS
        private bool _realLoaded;
        private string _realUrl;

        private async System.Threading.Tasks.Task EnsureWebAsync()
        {
            if (_webReady) return;
            try
            {
                await Web.EnsureCoreWebView2Async();
                Web.CoreWebView2.Settings.AreDevToolsEnabled = false;
                Web.CoreWebView2.Settings.IsStatusBarEnabled = false;
                Web.CoreWebView2.NavigationCompleted += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(_pendingInject)) InjectSkin(_pendingInject);
                };
                _webReady = true;
            }
            catch (Exception ex)
            {
                SetStatus("预览初始化失败（缺少 WebView2 运行时？）：" + ex.Message);
            }
        }

        private void InjectSkin(string css)
        {
            try
            {
                string js = "(function(){var id='bmapskin';var s=document.getElementById(id);"
                    + "if(!s){s=document.createElement('style');s.id=id;document.head.appendChild(s);}"
                    + "s.textContent=" + JsonSerializer.Serialize(css) + ";})();";
                Web.CoreWebView2.ExecuteScriptAsync(js);
            }
            catch { }
        }

        /// <summary>真实预览：把 ThoughtCanvas 的 index.html 加载进来，注入你的皮肤 CSS（所见=真软件）。</summary>
        private void LoadRealPreview(string css)
        {
            try
            {
                string folder = Path.GetDirectoryName(Prefs.TcPath);
                string file = Path.GetFileName(Prefs.TcPath);
                string url = "https://bmaptc/" + file;
                if (_realLoaded && _realUrl == url)
                {
                    _pendingInject = css;
                    InjectSkin(css);   // 已加载：只更新皮肤，不重新加载（不打断你在预览里的操作）
                    return;
                }
                Web.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "bmaptc", folder, Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);
                _pendingInject = css;
                _realUrl = url;
                _realLoaded = true;
                Web.CoreWebView2.Navigate(url);
                SetStatus("真实预览：已加载 ThoughtCanvas 本体并套上你的皮肤。（预览里可点“新建”看节点/画布皮肤）");
            }
            catch (Exception ex)
            {
                SetStatus("真实预览加载失败：" + ex.Message + "（可在 ⚙偏好 改回内置模拟）");
            }
        }

        // ================= 页面切换 =================
        private void ShowHome()
        {
            EditorRoot.Visibility = Visibility.Collapsed;
            HomeRoot.Visibility = Visibility.Visible;
            RenderRecent();
        }

        private void ShowEditor()
        {
            HomeRoot.Visibility = Visibility.Collapsed;
            EditorRoot.Visibility = Visibility.Visible;
        }

        // ================= 最近使用 =================
        private class RecentItem { public string path { get; set; } public string name { get; set; } public string type { get; set; } }

        private List<RecentItem> LoadRecent()
        {
            try
            {
                if (File.Exists(RecentPath))
                    return JsonSerializer.Deserialize<List<RecentItem>>(File.ReadAllText(RecentPath)) ?? new List<RecentItem>();
            }
            catch { }
            return new List<RecentItem>();
        }

        private void SaveRecent(List<RecentItem> list)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(RecentPath));
                File.WriteAllText(RecentPath, JsonSerializer.Serialize(list, JsonOpts), new System.Text.UTF8Encoding(false));
            }
            catch { }
        }

        private void AddRecent(string path)
        {
            var list = LoadRecent();
            list.RemoveAll(x => string.Equals(x.path, path, StringComparison.OrdinalIgnoreCase));
            list.Insert(0, new RecentItem { path = path, name = Path.GetFileName(path), type = "界面方案" });
            if (list.Count > 12) list = list.GetRange(0, 12);
            SaveRecent(list);
        }

        private void RenderRecent()
        {
            RecentList.Children.Clear();
            var list = LoadRecent().Where(x => !string.IsNullOrEmpty(x.path) && File.Exists(x.path)).ToList();
            if (list.Count == 0)
            {
                RecentList.Children.Add(new TextBlock { Text = "暂无最近文件 —— 新建或打开后会出现在这里", Foreground = BrushOf("#8a90a0"), FontSize = 13 });
                return;
            }
            foreach (var it in list) RecentList.Children.Add(CreateRecentRow(it));
        }

        private Border CreateRecentRow(RecentItem it)
        {
            var b = new Border
            {
                Background = BrushOf("#ffffff"),
                BorderBrush = BrushOf("#eceef2"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 0, 0, 8),
                Cursor = Cursors.Hand
            };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var ic = new TextBlock { Text = "🖼", FontSize = 20, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            Grid.SetColumn(ic, 0);
            var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            mid.Children.Add(new TextBlock { Text = it.name, FontSize = 14, FontWeight = FontWeights.Medium, Foreground = BrushOf("#23262e") });
            mid.Children.Add(new TextBlock { Text = it.path, FontSize = 11.5, Foreground = BrushOf("#8a90a0"), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0) });
            Grid.SetColumn(mid, 1);
            var chip = new Border { Background = BrushOf("#eef1f6"), CornerRadius = new CornerRadius(7), Padding = new Thickness(9, 3, 9, 3), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            chip.Child = new TextBlock { Text = it.type ?? "界面方案", FontSize = 11.5, Foreground = BrushOf("#5b6070") };
            Grid.SetColumn(chip, 2);

            g.Children.Add(ic); g.Children.Add(mid); g.Children.Add(chip);
            b.Child = g;
            string p = it.path;
            b.MouseLeftButtonUp += (s, e) => OpenPath(p);
            return b;
        }

        internal static SolidColorBrush BrushOf(string hex)
        {
            var c = ColorUtil.ParseHex(hex);
            return c != null ? new SolidColorBrush(Color.FromRgb(c.Value.R, c.Value.G, c.Value.B)) : Brushes.Transparent;
        }

        // ================= 事件挂接 =================
        private void WireEvents()
        {
            BtnHome.Click += (s, e) => ShowHome();
            HomeNewBtn.Click += (s, e) => { if (ConfirmDiscard()) { NewDoc(); ShowEditor(); } };
            HomeOpenBtn.Click += (s, e) => OpenFile();
            BtnNew.Click += (s, e) => { if (ConfirmDiscard()) NewDoc(); };
            BtnOpen.Click += (s, e) => OpenFile();
            BtnSave.Click += (s, e) => Save(false);
            BtnSaveAs.Click += (s, e) => Save(true);

            TxtName.TextChanged += (s, e) => { if (_ready) MarkDirty(); };

            BtnUndo.Click += (s, e) => Undo();
            BtnRedo.Click += (s, e) => Redo();
            BtnPrefs.Click += (s, e) => OpenPrefs();

            BtnAddRect.Click += (s, e) => SetTool("rect");
            BtnAddRound.Click += (s, e) => SetTool("roundrect");
            BtnAddEllipse.Click += (s, e) => SetTool("ellipse");
            BtnAddLine.Click += (s, e) => SetTool("line");
            BtnAddText.Click += (s, e) => AddTextRegionTool();
            BtnAddImage.Click += (s, e) => ImportImage();

            DrawLayer.MouseLeftButtonDown += DrawDown;
            DrawLayer.MouseMove += DrawMove;
            DrawLayer.MouseLeftButtonUp += DrawUp;

            BtnLayerUp.Click += (s, e) => MoveSelectedLayer(1);
            BtnLayerDown.Click += (s, e) => MoveSelectedLayer(-1);
            BtnLayerTop.Click += (s, e) => MoveSelectedLayerTo(true);
            BtnLayerBottom.Click += (s, e) => MoveSelectedLayerTo(false);
            BtnLayerDel.Click += (s, e) => DeleteSelected();

            RbViewCanvas.Checked += (s, e) =>
            {
                if (!_ready) return;
                WebHost.Visibility = Visibility.Collapsed;   // WebView2 是原生窗口，必须收起才能露出画布
                CanvasView.Visibility = Visibility.Visible;
            };
            RbViewPreview.Checked += async (s, e) =>
            {
                if (!_ready) return;
                if (BtnPen.IsChecked == true) BtnPen.IsChecked = false;
                ClearTool();
                CanvasView.Visibility = Visibility.Collapsed;
                WebHost.Visibility = Visibility.Visible;
                await EnsureWebAsync();
                RefreshPreview();
            };

            Stage.MouseLeftButtonDown += (s, e) => ClearSelection();
            GhostHost.MouseLeftButtonDown += (s, e) => ClearSelection();   // 点空白（参考层）取消选中
            PreviewKeyDown += OnKey;
        }

        private bool ConfirmDiscard()
        {
            if (!_dirty) return true;
            var r = MessageBox.Show(this, "当前方案有没保存的修改，继续会丢失。继续？", "未保存",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            return r == MessageBoxResult.Yes;
        }

        private void OnKey(object sender, KeyEventArgs e)
        {
            if (EditorRoot.Visibility != Visibility.Visible) return;
            if (Keyboard.FocusedElement is TextBox) return;
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                if (e.Key == Key.Z) { Undo(); e.Handled = true; return; }
                if (e.Key == Key.Y) { Redo(); e.Handled = true; return; }
            }
            if (e.Key == Key.Escape) { ClearTool(); ClearSelection(); e.Handled = true; return; }
            if (_sel == null) return;

            double step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 10 : 1;
            switch (e.Key)
            {
                case Key.Delete: DeleteSelected(); e.Handled = true; break;
                case Key.Left: NudgeSelected(-step, 0); e.Handled = true; break;
                case Key.Right: NudgeSelected(step, 0); e.Handled = true; break;
                case Key.Up: NudgeSelected(0, -step); e.Handled = true; break;
                case Key.Down: NudgeSelected(0, step); e.Handled = true; break;
            }
        }

        // ================= 新建 =================
        private void NewDoc()
        {
            _designs.Clear();
            _activeTarget = null;
            ClearElements();
            ResetUndo();
            _currentPath = null;
            TxtName.Text = "自定义界面";
            ClearSelection();
            SwitchTarget("card", firstLoad: true);   // 默认从"文本框/节点"开始设计
            _dirty = false;
            UpdateTitle();
            SetStatus("选左侧一个部件 → 在设计框里画它的新样子、放文本区 → 导出后这个部件就用你画的样子且保留功能。");
            RbViewCanvas.IsChecked = true;
            if (BtnPen.IsChecked == true) BtnPen.IsChecked = false;
        }

        // ================= 部件面板 =================
        internal void BuildCompPanel(ComponentElement ce)
        {
            _loadingPanel = true;
            CompTitle.Text = "部件：" + ce.Name;
            PanelCompProps.Children.Clear();

            foreach (var prop in ce.Props)
            {
                var p = prop;
                PanelCompProps.Children.Add(new TextBlock { Text = p.Label, Margin = new Thickness(0, 10, 0, 3), Foreground = BrushOf("#5b6070") });

                var row = new StackPanel { Orientation = Orientation.Horizontal };
                var sw = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(5), BorderBrush = BrushOf("#cccccc"), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 6, 0) };
                SetSwatch(sw, p.Value);
                var tb = new TextBox { Width = 96, Text = p.Value };
                var pick = new Button { Content = "取色", Height = 28, Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(6, 0, 0, 0), Cursor = Cursors.Hand };

                tb.TextChanged += (s, e) =>
                {
                    if (_loadingPanel) return;
                    string n = ColorUtil.NormalizeHex(tb.Text);
                    if (n == null) { SetStatus("颜色没认出来：填 6 位十六进制，如 0a8a6f（# 可省略，3 位简写也行）"); return; }
                    PushUndo("comp:" + ce.CompId + ":" + p.Key);
                    p.Value = n;
                    SetSwatch(sw, n);
                    RebuildComponentView(ce);
                    MarkDirty();
                };
                pick.Click += (s, e) => PickColor(tb);

                row.Children.Add(sw); row.Children.Add(tb); row.Children.Add(pick);
                PanelCompProps.Children.Add(row);

                // 选中光环支持一键从主色推导（与 ThoughtCanvas 的 ×1.7 完全一致）
                if (ce.CompId == "cardSel" && p.Key == "ring")
                {
                    var auto = new Button { Content = "从选中边框自动推导", Height = 26, Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Cursor = Cursors.Hand };
                    auto.Click += (s, e) => { tb.Text = ColorUtil.HexDark(ce.Get("accent"), 1.7); };
                    PanelCompProps.Children.Add(auto);
                }
            }

            var reset = new Button { Content = "恢复此部件原版色", Height = 26, Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Cursor = Cursors.Hand };
            reset.Click += (s, e) =>
            {
                PushUndo();
                foreach (var p in ce.Props) p.Value = p.Default;
                RebuildComponentView(ce);
                BuildCompPanel(ce);
                MarkDirty();
            };
            PanelCompProps.Children.Add(reset);
            _loadingPanel = false;
        }

        // ================= 取色 / 色块 =================
        internal void PickColor(TextBox target)
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

        internal static void SetSwatch(Border b, string hex)
        {
            var p = ColorUtil.ParseHex(hex);
            b.Background = p != null
                ? new SolidColorBrush(Color.FromRgb(p.Value.R, p.Value.G, p.Value.B))
                : Brushes.Transparent;
        }

        // ================= 状态 / 标题 / 预览 =================
        internal void SetStatus(string t) { TxtStatus.Text = t; }

        internal void MarkDirty()
        {
            _dirty = true;
            UpdateTitle();
            if (WebHost.Visibility == Visibility.Visible) RefreshPreview();
        }

        private void UpdateTitle()
        {
            string f = string.IsNullOrEmpty(_currentPath) ? "未命名" : Path.GetFileName(_currentPath);
            TxtCurrent.Text = f + (_dirty ? " ●" : "");
            Title = "BMAP 界面编辑器 V0.0.3 — " + f + (_dirty ? " ●" : "");
        }

        private void RefreshPreview()
        {
            if (!_webReady) return;
            var designs = CollectDesigns();
            string css = CssBuilder.BuildSkins(designs, TxtName.Text);

            if (Prefs.PreviewSource == 1 && !string.IsNullOrEmpty(Prefs.TcPath) && File.Exists(Prefs.TcPath))
            {
                LoadRealPreview(css);   // 真实软件预览
                return;
            }

            _pendingInject = null;
            string html = "<!doctype html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\"><style>"
                + Preview.BaseCss + "</style><style>" + Preview.TestCss + "</style><style>" + css
                + "</style><style>" + PreviewDimCss(designs) + "</style></head><body>" + Preview.TestMarkup + "</body></html>";
            try { Web.NavigateToString(html); } catch { }
        }

        /// <summary>真实预览里，把"还没设计"的原版分组淡化或隐藏（已设计的部件保持完整）。</summary>
        private string PreviewDimCss(Dictionary<string, List<CanvasElement>> designs)
        {
            int mode = Prefs.PreviewOriginalMode;
            if (mode == 0) return "";   // 完整显示
            string v = mode == 2 ? "0" : Prefs.PreviewDim.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            System.Func<string[], bool> designed = ids => ids.Any(designs.ContainsKey);
            var sb = new System.Text.StringBuilder();
            // 分组淡化：整组还没设计才淡（避免把子元素的设计一起淡掉——opacity 是相乘的）
            if (!designed(new[] { "toolbar", "btn", "btnPrimary" }))
                sb.Append("#toolbar{opacity:" + v + "!important;}");
            if (!designed(new[] { "card", "cardSel" }))
                sb.Append("#stage>.node{opacity:" + v + "!important;}");
            sb.Append("#wire{opacity:" + v + "!important;}");
            if (!designed(new[] { "menu" }))
                sb.Append(".popmenu{opacity:" + v + "!important;}");
            if (!designed(new[] { "floatbar" }))
                sb.Append("#floatBar{opacity:" + v + "!important;}");
            return sb.ToString();
        }

        // ================= 偏好设置 =================
        private void OpenPrefs()
        {
            var win = new Window
            {
                Title = "偏好设置",
                Width = 400,
                SizeToContent = SizeToContent.Height,
                MaxHeight = 640,
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Background = BrushOf("#f5f6f8"),
                FontFamily = this.FontFamily,
                FontSize = 13
            };
            var sp = new StackPanel { Margin = new Thickness(20, 16, 20, 16) };

            Func<string, TextBlock> Head = t => new TextBlock
            { Text = t, FontWeight = FontWeights.Bold, FontSize = 14, Foreground = BrushOf("#23262e"), Margin = new Thickness(0, 14, 0, 6) };
            Func<string, TextBlock> Note = t => new TextBlock
            { Text = t, TextWrapping = TextWrapping.Wrap, Foreground = BrushOf("#8a90a0"), FontSize = 11.5, Margin = new Thickness(0, 2, 0, 0) };

            // ===== 高级模式 =====
            sp.Children.Add(Head("高级模式"));

            var chkAdv = new CheckBox
            {
                Content = "开启高级模式（自由编辑，不限定设计框；可自己画文本区/固定文字）",
                IsChecked = Prefs.Advanced
            };
            sp.Children.Add(chkAdv);

            var advBox = new StackPanel { Margin = new Thickness(0, 8, 0, 0), Visibility = Prefs.Advanced ? Visibility.Visible : Visibility.Collapsed };

            advBox.Children.Add(new TextBlock { Text = "编辑区限制", Foreground = BrushOf("#5b6070"), Margin = new Thickness(0, 4, 0, 3) });
            var cmbFrame = new ComboBox { Width = 220, HorizontalAlignment = HorizontalAlignment.Left, Height = 28 };
            cmbFrame.ItemsSource = new[] { "限定到设计框（只显示框内）", "自由：框外也照样导出" };
            cmbFrame.SelectedIndex = Prefs.ConstrainFrame ? 0 : 1;
            cmbFrame.SelectionChanged += (s, e) =>
            {
                Prefs.ConstrainFrame = cmbFrame.SelectedIndex == 0;
                Prefs.Save(); DrawFrame();
                if (WebHost.Visibility == Visibility.Visible) RefreshPreview();
            };
            advBox.Children.Add(cmbFrame);

            advBox.Children.Add(new TextBlock { Text = "文本区", Foreground = BrushOf("#5b6070"), Margin = new Thickness(0, 12, 0, 3) });
            var cmbTxt = new ComboBox { Width = 220, HorizontalAlignment = HorizontalAlignment.Left, Height = 28 };
            cmbTxt.ItemsSource = new[] { "用部件本来的文字（有功能）", "允许自己画/打固定文字" };
            cmbTxt.SelectedIndex = Prefs.CustomTextRegion ? 1 : 0;
            cmbTxt.SelectionChanged += (s, e) =>
            {
                Prefs.CustomTextRegion = cmbTxt.SelectedIndex == 1;
                Prefs.Save();
                if (_sel is TextRegionElement) SyncTextPanel();
                if (WebHost.Visibility == Visibility.Visible) RefreshPreview();
            };
            advBox.Children.Add(cmbTxt);
            sp.Children.Add(advBox);

            chkAdv.Click += (s, e) =>
            {
                Prefs.Advanced = chkAdv.IsChecked == true;
                Prefs.Save();
                advBox.Visibility = Prefs.Advanced ? Visibility.Visible : Visibility.Collapsed;
                DrawFrame();
                if (_sel is TextRegionElement) SyncTextPanel();
                if (WebHost.Visibility == Visibility.Visible) RefreshPreview();
            };
            sp.Children.Add(Note("不开高级模式：只能限定到设计框 + 用部件本来的文字（最省心，保证有功能）。"));

            // ===== 真实预览：没改过的原版 =====
            sp.Children.Add(Head("真实预览 · 没改过的原版部分"));

            var cmb = new ComboBox { Width = 160, HorizontalAlignment = HorizontalAlignment.Left, Height = 28 };
            cmb.ItemsSource = new[] { "完整显示", "淡化", "隐藏" };
            cmb.SelectedIndex = Prefs.PreviewOriginalMode;
            sp.Children.Add(cmb);

            var lblD = new TextBlock { Foreground = BrushOf("#5b6070"), Margin = new Thickness(0, 12, 0, 3) };
            var sldD = new Slider { Minimum = 5, Maximum = 80, Value = Math.Round(Prefs.PreviewDim * 100), TickFrequency = 1, IsSnapToTickEnabled = true };
            var noteD = Note("只淡化/隐藏你没动过的部分；改过颜色的部件保持完整，这样一眼就能看出你做了什么。");
            Action setLblD = () => lblD.Text = "淡化程度：" + Math.Round(sldD.Value) + "%";
            Action syncDimEnabled = () =>
            {
                lblD.Visibility = sldD.Visibility = cmb.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
            };
            setLblD();
            syncDimEnabled();
            sldD.ValueChanged += (s, e) => { Prefs.PreviewDim = Math.Round(sldD.Value) / 100.0; setLblD(); Prefs.Save(); RefreshPreview(); };
            cmb.SelectionChanged += (s, e) =>
            {
                Prefs.PreviewOriginalMode = cmb.SelectedIndex;
                Prefs.Save(); syncDimEnabled(); RefreshPreview();
            };
            sp.Children.Add(lblD);
            sp.Children.Add(sldD);
            sp.Children.Add(noteD);

            // ===== 真实预览来源 =====
            sp.Children.Add(Head("真实预览用哪个"));
            var cmbSrc = new ComboBox { Width = 260, HorizontalAlignment = HorizontalAlignment.Left, Height = 28 };
            cmbSrc.ItemsSource = new[] { "内置模拟（自带，够看皮肤）", "真实软件（加载 ThoughtCanvas 本体）" };
            cmbSrc.SelectedIndex = Prefs.PreviewSource;
            sp.Children.Add(cmbSrc);

            var lblPath = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = BrushOf("#8a90a0"), FontSize = 11.5, Margin = new Thickness(0, 6, 0, 0) };
            Action setPath = () => lblPath.Text = string.IsNullOrEmpty(Prefs.TcPath) ? "未指定 index.html" : "已指定：" + Prefs.TcPath;
            setPath();
            var btnPick = new Button { Content = "指定 ThoughtCanvas 的 index.html…", Height = 28, Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Cursor = Cursors.Hand };
            var srcBox = new StackPanel { Visibility = Prefs.PreviewSource == 1 ? Visibility.Visible : Visibility.Collapsed };
            btnPick.Click += (s, e) =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "ThoughtCanvas 主程序 (index.html)|index.html;*.html|所有文件 (*.*)|*.*" };
                if (dlg.ShowDialog(win) == true)
                {
                    Prefs.TcPath = dlg.FileName; Prefs.Save(); setPath();
                    _realLoaded = false;
                    if (WebHost.Visibility == Visibility.Visible) RefreshPreview();
                }
            };
            srcBox.Children.Add(btnPick);
            srcBox.Children.Add(lblPath);
            sp.Children.Add(srcBox);
            cmbSrc.SelectionChanged += (s, e) =>
            {
                Prefs.PreviewSource = cmbSrc.SelectedIndex; Prefs.Save();
                srcBox.Visibility = Prefs.PreviewSource == 1 ? Visibility.Visible : Visibility.Collapsed;
                _realLoaded = false;
                if (WebHost.Visibility == Visibility.Visible) RefreshPreview();
            };
            sp.Children.Add(Note("真实软件预览＝把 ThoughtCanvas 本体加载进来套上你的皮肤，所见即真软件；预览里可点“新建”看节点/画布皮肤。文件对话框等 Electron 功能在预览里用不了（只影响预览，不影响导出）。"));

            // ===== 保存 =====
            sp.Children.Add(Head("保存"));
            var chkWarn = new CheckBox { Content = "保存时提示还有部件没绘制", IsChecked = Prefs.WarnUndrawn };
            chkWarn.Click += (s, e) => { Prefs.WarnUndrawn = chkWarn.IsChecked == true; Prefs.Save(); };
            sp.Children.Add(chkWarn);

            var close = new Button { Content = "关闭", Height = 30, Width = 88, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0), Cursor = Cursors.Hand };
            close.Click += (s, e) => win.Close();
            sp.Children.Add(close);

            win.Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = sp };
            win.ShowDialog();
        }

        // ================= 保存 / 打开 =================
        private class ProjectDto
        {
            public List<ElementDto> elements { get; set; }
        }

        private class ElementDto
        {
            public string type { get; set; }
            public string kind { get; set; }   // V0.0.1 旧档的图形类型字段
            public string name { get; set; }
            public bool visible { get; set; } = true;
            public string comp { get; set; }
            public Dictionary<string, string> props { get; set; }
            public double x { get; set; }
            public double y { get; set; }
            public double w { get; set; }
            public double h { get; set; }
            public double x2 { get; set; }
            public double y2 { get; set; }
            public string fill { get; set; }
            public bool noFill { get; set; }
            public string stroke { get; set; }
            public double strokeW { get; set; }
            public double radius { get; set; }
            public double opacity { get; set; } = 1;
            public bool isMask { get; set; }
            public List<double[]> points { get; set; }
            public string color { get; set; }
            public double width { get; set; }
            public string base64 { get; set; }
            public string mime { get; set; }
            // 文本区
            public string text { get; set; }
            public bool customText { get; set; }
            public double fontSize { get; set; } = 14;
            public string alignH { get; set; }
            public string alignV { get; set; }
            public bool bold { get; set; }
        }

        // V0.0.1 旧档
        private class V1Project
        {
            public Dictionary<string, Dictionary<string, string>> regions { get; set; }
            public List<ElementDto> shapes { get; set; }
        }

        private ElementDto ToDto(CanvasElement el)
        {
            var d = new ElementDto { name = el.Name, visible = el.Visible };
            var ce = el as ComponentElement;
            if (ce != null)
            {
                d.type = "component";
                d.comp = ce.CompId;
                d.props = ce.Props.ToDictionary(p => p.Key, p => p.Value);
                return d;
            }
            var s = el as ShapeElement;
            if (s != null)
            {
                d.type = s.Kind;
                d.x = s.X; d.y = s.Y; d.w = s.W; d.h = s.H; d.x2 = s.X2; d.y2 = s.Y2;
                d.fill = s.Fill; d.noFill = s.NoFill; d.stroke = s.Stroke; d.strokeW = s.StrokeW;
                d.radius = s.Radius; d.opacity = s.Opacity; d.isMask = s.IsMask;
                return d;
            }
            var k = el as InkElement;
            if (k != null)
            {
                d.type = "ink";
                d.points = k.Points.Select(p => new[] { Math.Round(p.X, 1), Math.Round(p.Y, 1) }).ToList();
                d.color = k.Color; d.width = k.Width; d.opacity = k.Opacity;
                return d;
            }
            var im = el as ImageElement;
            if (im != null)
            {
                d.type = "image";
                d.x = im.X; d.y = im.Y; d.w = im.W; d.h = im.H;
                d.base64 = im.Base64; d.mime = im.Mime; d.opacity = im.Opacity;
                return d;
            }
            var tr = el as TextRegionElement;
            if (tr != null)
            {
                d.type = "textregion";
                d.x = tr.X; d.y = tr.Y; d.w = tr.W; d.h = tr.H;
                d.text = tr.Text; d.customText = tr.CustomText; d.fontSize = tr.FontSize;
                d.color = tr.Color; d.alignH = tr.AlignH; d.alignV = tr.AlignV; d.bold = tr.Bold;
                return d;
            }
            return null;
        }

        private CanvasElement FromDto(ElementDto d)
        {
            if (d == null) return null;
            if (string.IsNullOrEmpty(d.type)) d.type = d.kind;   // 兼容 V0.0.1 旧档
            if (string.IsNullOrEmpty(d.type)) return null;
            if (d.type == "component")
            {
                var def = ComponentLib.Find(d.comp);
                if (def == null) return null;
                var ce = def.CreateInstance();
                ce.Name = string.IsNullOrEmpty(d.name) ? def.Name : d.name;
                ce.Visible = d.visible;
                if (d.props != null)
                    foreach (var kv in d.props)
                    {
                        var p = ce.Prop(kv.Key);
                        string n = ColorUtil.NormalizeHex(kv.Value);
                        if (p != null && n != null) p.Value = n;
                    }
                return ce;
            }
            if (d.type == "ink")
            {
                var k = new InkElement
                {
                    Name = string.IsNullOrEmpty(d.name) ? "手绘" : d.name,
                    Visible = d.visible,
                    Color = ColorUtil.NormalizeHex(d.color) ?? "#5b8def",
                    Width = d.width <= 0 ? 3 : d.width,
                    Opacity = Clamp01(d.opacity)
                };
                if (d.points != null)
                    foreach (var p in d.points)
                        if (p != null && p.Length >= 2) k.Points.Add(new Point(p[0], p[1]));
                return k.Points.Count >= 2 ? k : null;
            }
            if (d.type == "image")
            {
                if (string.IsNullOrEmpty(d.base64)) return null;
                return new ImageElement
                {
                    Name = string.IsNullOrEmpty(d.name) ? "图片" : d.name,
                    Visible = d.visible,
                    Base64 = d.base64,
                    Mime = string.IsNullOrEmpty(d.mime) ? "image/png" : d.mime,
                    X = d.x, Y = d.y, W = Math.Max(4, d.w), H = Math.Max(4, d.h),
                    Opacity = Clamp01(d.opacity)
                };
            }
            if (d.type == "textregion")
            {
                return new TextRegionElement
                {
                    Name = string.IsNullOrEmpty(d.name) ? "文本区" : d.name,
                    Visible = d.visible,
                    X = d.x, Y = d.y, W = Math.Max(8, d.w), H = Math.Max(8, d.h),
                    Text = d.text ?? "文字",
                    CustomText = d.customText,
                    FontSize = d.fontSize <= 0 ? 14 : d.fontSize,
                    Color = ColorUtil.NormalizeHex(d.color) ?? "#23262e",
                    AlignH = string.IsNullOrEmpty(d.alignH) ? "left" : d.alignH,
                    AlignV = string.IsNullOrEmpty(d.alignV) ? "middle" : d.alignV,
                    Bold = d.bold
                };
            }
            // 图形
            var s = new ShapeElement
            {
                Kind = d.type,
                Name = string.IsNullOrEmpty(d.name) ? "图形" : d.name,
                Visible = d.visible,
                X = d.x, Y = d.y, W = d.w, H = d.h, X2 = d.x2, Y2 = d.y2,
                Fill = ColorUtil.NormalizeHex(d.fill) ?? "#5b8def",
                NoFill = d.noFill,
                Stroke = ColorUtil.NormalizeHex(d.stroke) ?? "#3a6bd8",
                StrokeW = d.strokeW,
                Radius = d.radius,
                Opacity = Clamp01(d.opacity),
                IsMask = d.isMask
            };
            if (s.Kind != "rect" && s.Kind != "roundrect" && s.Kind != "ellipse" && s.Kind != "line") return null;
            return s;
        }

        private static double Clamp01(double v)
        {
            if (v <= 0) return 1.0;
            return Math.Min(1.0, v);
        }

        private static bool DesignHasDrawn(List<CanvasElement> list)
        {
            if (list == null) return false;
            return list.Any(e => (e is ShapeElement s && !s.IsMask) || e is InkElement || e is ImageElement);
        }

        /// <summary>保存前，若还有部件没绘制外观，列出并问用户：套用原版继续保存 / 回去绘制。返回 true=继续保存。</summary>
        private bool ConfirmUndrawn(Dictionary<string, List<CanvasElement>> map)
        {
            if (!Prefs.WarnUndrawn) return true;
            var undrawn = DesignTargetLib.All
                .Where(t => !(map.TryGetValue(t.Id, out var l) && DesignHasDrawn(l)))
                .Select(t => t.Name).ToList();
            if (undrawn.Count == 0) return true;

            var win = new Window
            {
                Title = "还有部件没设计",
                Width = 420, SizeToContent = SizeToContent.Height, MaxHeight = 620,
                Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize, Background = BrushOf("#f5f6f8"), FontFamily = this.FontFamily, FontSize = 13
            };
            var sp = new StackPanel { Margin = new Thickness(20, 16, 20, 16) };
            sp.Children.Add(new TextBlock
            {
                Text = "以下 " + undrawn.Count + " 个部件还没有绘制。导入 ThoughtCanvas 后，它们会自动保持原版外观：",
                TextWrapping = TextWrapping.Wrap, Foreground = BrushOf("#23262e"), Margin = new Thickness(0, 0, 0, 10)
            });
            var listBox = new ListBox { MaxHeight = 240, BorderBrush = BrushOf("#e2e5ec") };
            foreach (var n in undrawn) listBox.Items.Add(new TextBlock { Text = "· " + n, Foreground = BrushOf("#5b6070") });
            sp.Children.Add(listBox);

            var chkNo = new CheckBox { Content = "以后保存不再提示", Margin = new Thickness(0, 12, 0, 0) };
            sp.Children.Add(chkNo);

            bool proceed = false;
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var btnBack = new Button { Content = "回去继续绘制", Height = 32, Padding = new Thickness(14, 0, 14, 0), Margin = new Thickness(0, 0, 8, 0), Cursor = Cursors.Hand };
            var btnGo = new Button { Content = "套用原版并保存", Height = 32, Padding = new Thickness(14, 0, 14, 0), Cursor = Cursors.Hand, FontWeight = FontWeights.Bold };
            btnBack.Click += (s, e) => { proceed = false; win.Close(); };
            btnGo.Click += (s, e) => { proceed = true; win.Close(); };
            row.Children.Add(btnBack); row.Children.Add(btnGo);
            sp.Children.Add(row);

            win.Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = sp };
            win.ShowDialog();

            if (chkNo.IsChecked == true) { Prefs.WarnUndrawn = false; Prefs.Save(); }
            return proceed;
        }

        private void Save(bool forceDialog)
        {
            string name = string.IsNullOrWhiteSpace(TxtName.Text) ? "自定义界面" : TxtName.Text.Trim();
            if (_activeTarget != null) _designs[_activeTarget] = SnapshotJson();

            var map = CollectDesigns();
            if (!ConfirmUndrawn(map)) return;   // 提示还没画的部件，用户选择继续绘制则中止

            string css = CssBuilder.BuildSkins(map, name);

            // 每个部件的设计各存一份（给本工具重开继续改）；css 给 ThoughtCanvas 导入
            var designsMap = new Dictionary<string, ProjectDto>();
            foreach (var kv in _designs)
            {
                ProjectDto dto = null;
                try { dto = JsonSerializer.Deserialize<ProjectDto>(kv.Value); } catch { }
                if (dto != null && dto.elements != null && dto.elements.Count > 0) designsMap[kv.Key] = dto;
            }

            var obj = new { app = "brace-mindmap-ui", name, css, editor = "bmap-ui-editor", version = 3, activeTarget = _activeTarget, designs = designsMap };
            string json = JsonSerializer.Serialize(obj, JsonOpts);

            string path = _currentPath;
            if (forceDialog || string.IsNullOrEmpty(path))
            {
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "UI 方案文件 (*.bmapui)|*.bmapui|所有文件 (*.*)|*.*",
                    FileName = SanitizeFileName(name) + ".bmapui",
                    DefaultExt = ".bmapui"
                };
                if (dlg.ShowDialog(this) != true) return;
                path = dlg.FileName;
            }

            try
            {
                File.WriteAllText(path, json, new System.Text.UTF8Encoding(false));
                _currentPath = path;
                _dirty = false;
                UpdateTitle();
                SetStatus("已保存：" + path + " —— 在 ThoughtCanvas「设置 → 外观 → 导入」里选它即可生效。");
                AddRecent(path);
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

        private void OpenFile()
        {
            if (!ConfirmDiscard()) return;
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "UI 方案文件 (*.bmapui)|*.bmapui|所有文件 (*.*)|*.*"
            };
            if (dlg.ShowDialog(this) != true) return;
            OpenPath(dlg.FileName);
        }

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
                if (app != "brace-mindmap-ui")
                {
                    MessageBox.Show(this, "这不是 .bmapui 界面方案文件（缺少 brace-mindmap-ui 标识）。", "打开失败",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                bool hasNew = root.TryGetProperty("designs", out var designsEl) && designsEl.ValueKind == JsonValueKind.Object;
                bool hasOld = root.TryGetProperty("project", out var proj);
                if (!hasNew && !hasOld)
                {
                    MessageBox.Show(this,
                        "这个 .bmapui 不是本工具做的（没有画布数据），暂时没法在画布里编辑。",
                        "无法编辑", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                NewDoc();
                _designs.Clear();

                if (hasNew)
                {
                    foreach (var prop in designsEl.EnumerateObject())
                        if (DesignTargetLib.Find(prop.Name) != null)
                            _designs[prop.Name] = prop.Value.GetRawText();
                    string act = root.TryGetProperty("activeTarget", out var at) ? at.GetString() : null;
                    if (string.IsNullOrEmpty(act) || DesignTargetLib.Find(act) == null)
                        act = _designs.Keys.FirstOrDefault() ?? "card";
                    _activeTarget = null;
                    SwitchTarget(act, firstLoad: true);
                }
                else
                {
                    // 老档（V0.0.2 装饰模型）：整体并入"文本框"设计，best-effort
                    if (proj.TryGetProperty("elements", out _))
                        _designs["card"] = "{\"elements\":" + (proj.TryGetProperty("elements", out var els) ? els.GetRawText() : "[]") + "}";
                    _activeTarget = null;
                    SwitchTarget("card", firstLoad: true);
                    SetStatus("这是老版本文件，已尽量并入「文本框」设计，可能需要你再调整。");
                }

                TxtName.Text = root.TryGetProperty("name", out var n) ? (n.GetString() ?? "自定义界面") : "自定义界面";

                _currentPath = path;
                _dirty = false;
                UpdateTitle();
                AddRecent(path);
                ShowEditor();
                if (hasNew) SetStatus("已打开：" + path);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "打开失败（不是合法 JSON？）", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>V0.0.1 旧档（regions+shapes）迁移成部件+图形。旧版是"整张复刻图"，故补齐全套部件。</summary>
        private void MigrateV1(JsonElement proj)
        {
            var v1 = JsonSerializer.Deserialize<V1Project>(proj.GetRawText());
            if (v1 == null) return;
            Func<string, string, string, string> R = (reg, key, fb) =>
            {
                Dictionary<string, string> d;
                string v;
                if (v1.regions != null && v1.regions.TryGetValue(reg, out d) && d.TryGetValue(key, out v))
                    return ColorUtil.NormalizeHex(v) ?? fb;
                return fb;
            };

            var map = new Dictionary<string, Dictionary<string, string>>
            {
                { "stage",      new Dictionary<string, string> { { "bg", R("stage", "bg", "#f5f6f8") }, { "grid", R("stage", "grid", "#e7e9ee") } } },
                { "brace",      new Dictionary<string, string> { { "color", R("brace", "color", "#9aa3b8") } } },
                { "link",       new Dictionary<string, string> { { "color", R("brace", "color", "#9aa3b8") }, { "anchor", R("sel", "accent", "#5b8def") } } },
                { "card",       new Dictionary<string, string> { { "bg", R("card", "bg", "#ffffff") }, { "border", R("card", "border", "#e2e5ec") }, { "text", R("card", "text", "#23262e") } } },
                { "cardSel",    new Dictionary<string, string> { { "bg", R("card", "bg", "#ffffff") }, { "accent", R("sel", "accent", "#5b8def") }, { "ring", R("sel", "accentSoft", "#e8f0ff") }, { "text", R("card", "text", "#23262e") } } },
                { "toolbar",    new Dictionary<string, string> { { "bg", R("toolbar", "bg", "#ffffff") }, { "border", R("toolbar", "border", "#e2e5ec") } } },
                { "title",      new Dictionary<string, string> { { "text", R("text", "ink", "#23262e") }, { "dot", R("sel", "accent", "#5b8def") } } },
                { "btn",        new Dictionary<string, string> { { "bg", R("btn", "bg", "#ffffff") }, { "text", R("btn", "text", "#23262e") }, { "border", R("btn", "border", "#e2e5ec") } } },
                { "btnPrimary", new Dictionary<string, string> { { "bg", R("btnPrimary", "bg", "#5b8def") }, { "text", R("btnPrimary", "text", "#ffffff") } } },
                { "fname",      new Dictionary<string, string> { { "color", R("text", "muted", "#8a90a0") } } },
            };

            foreach (var id in new[] { "stage", "brace", "link", "card", "cardSel", "toolbar", "title", "btn", "btnPrimary", "fname" })
            {
                var def = ComponentLib.Find(id);
                if (def == null) continue;
                var ce = def.CreateInstance();
                Dictionary<string, string> vals;
                if (map.TryGetValue(id, out vals))
                    foreach (var kv in vals)
                    {
                        var p = ce.Prop(kv.Key);
                        if (p != null) p.Value = kv.Value;
                    }
                AddElementSilent(ce);
            }

            if (v1.shapes != null)
                foreach (var d in v1.shapes)
                {
                    var el = FromDto(d);
                    if (el != null) AddElementSilent(el);
                }
        }
    }
}
