using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using ShapePath = System.Windows.Shapes.Path;
using IOPath = System.IO.Path;

namespace ThoughtCanvas
{
    public partial class MainWindow : Window
    {
        // ---- 颜色 ----
        static readonly Brush Ink        = new BrushConverter().ConvertFromString("#2C3140") as Brush;
        static readonly Brush Muted      = new BrushConverter().ConvertFromString("#8A90A0") as Brush;
        static readonly Brush Accent     = new BrushConverter().ConvertFromString("#5B6CF0") as Brush;
        static readonly Brush AccentSoft = new BrushConverter().ConvertFromString("#E8ECFF") as Brush;
        static readonly Brush CardBorder = new BrushConverter().ConvertFromString("#E2E5EC") as Brush;
        static readonly Brush BraceBrush = new BrushConverter().ConvertFromString("#B6BDCC") as Brush;

        // 整理模式：0 普通 / 1 纵向压缩 / 2 横向错位 —— 改变排版密度
        double BRANCH_GAP => doc.CompactMode == 1 ? 44 : doc.CompactMode == 2 ? 60 : 50;
        double V_GAP      => doc.CompactMode == 1 ? 10 : doc.CompactMode == 2 ? 14 : 26;

        Document doc = new Document();
        // 代理到 doc，旧有排版代码继续用 roots/curName/numbering 即可，且全进 .bmap + 撤销快照
        List<Topic> roots { get => doc.Roots; set => doc.Roots = value; }
        Topic selected, editing;
        readonly Dictionary<Topic, Border>    cardOf = new Dictionary<Topic, Border>();
        readonly Dictionary<Topic, TextBlock> textOf = new Dictionary<Topic, TextBlock>();
        readonly Dictionary<Topic, TextBox>   editOf = new Dictionary<Topic, TextBox>();
        readonly List<ShapePath> bracePaths = new List<ShapePath>();
        readonly List<string> undoStack = new List<string>();
        readonly List<string> redoStack = new List<string>();

        double scale = 1, offX = 60, offY = 60;
        string curName { get => doc.Name; set => doc.Name = value; }
        string numbering { get => doc.Numbering; set => doc.Numbering = value; }
        static readonly string[] BgPresets = { "", "#fff7d6", "#e3f5e1", "#e3f0ff", "#fde3ef", "#efe3fb", "#ffe7d3", "#eceff3" };

        bool readOnly;
        static readonly string RecentPath =
            IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ThoughtCanvas", "recent.txt");

        public MainWindow()
        {
            InitializeComponent();
            btnHome.Click    += (s, e) => ShowStart();
            btnUndo.Click    += (s, e) => Undo();
            btnRedo.Click    += (s, e) => Redo();
            btnFile.Click    += (s, e) => OpenFileMenu();
            btnAdd.Click     += (s, e) => { if (!readOnly) AddRootAt(ToWorld(new Point(viewport.ActualWidth / 2, viewport.ActualHeight / 2))); };
            btnTidy.Click    += (s, e) => Tidy();
            btnOutline.Click += (s, e) => ToggleOutline();
            btnOutlineClose.Click += (s, e) => ToggleOutline();
            btnExitFocus.Click += (s, e) => ExitFocus();
            btnRead.Click    += (s, e) => ToggleReadonly();
            btnFit.Click     += (s, e) => Fit();
            scSettings.Click += (s, e) => OpenSettings();
            scNew.MouseLeftButtonUp    += (s, e) => { HideStart(); DoNew(); };
            scSpider.MouseLeftButtonUp  += (s, e) => { HideStart(); DoNewSpider(); };
            btnLink.Click += (s, e) => AddLinkFromSelection();
            scOpen.MouseLeftButtonUp    += (s, e) => DoOpen();
            viewport.MouseLeftButtonDown += Viewport_MouseLeftButtonDown;
            viewport.MouseWheel += Viewport_MouseWheel;
            viewport.MouseDown += Viewport_MouseDownPan;
            viewport.MouseMove += Viewport_MouseMovePan;
            viewport.MouseUp += Viewport_MouseUpPan;
            PreviewKeyDown += Window_PreviewKeyDown;
            LoadSettings();
            ApplyLang();
            Closing += (s, e) => { if (!ConfirmDiscard()) e.Cancel = true; };
            Loaded += (s, e) =>
            {
                doc = new Document { Roots = { new Topic { Text = "中心主题", X = 2400, Y = 1500 } } };
                Rebuild();
                // 命令行/双击 .bmap 传入的文件
                var args = Environment.GetCommandLineArgs();
                string file = args.Length > 1 ? args[1] : null;
                if (!string.IsNullOrEmpty(file) && File.Exists(file)) { OpenPath(file); }
                else ShowStart();
            };
        }

        // ========== 开始页 ==========
        void ShowStart() { RenderRecent(); startPage.Visibility = Visibility.Visible; }
        void HideStart() { startPage.Visibility = Visibility.Collapsed; }

        void RenderRecent()
        {
            recentList.Children.Clear();
            foreach (var p in LoadRecent())
            {
                string path = p;
                var row = new Border { Padding = new Thickness(12, 8, 12, 8), CornerRadius = new CornerRadius(9), Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 0, 6), Background = Brushes.Transparent };
                var sp = new StackPanel();
                sp.Children.Add(new TextBlock { Text = IOPath.GetFileNameWithoutExtension(path), FontSize = 13, Foreground = Ink });
                sp.Children.Add(new TextBlock { Text = path, FontSize = 11, Foreground = Muted });
                row.Child = sp;
                row.MouseEnter += (s, e) => row.Background = (Brush)new BrushConverter().ConvertFromString("#F0F2F7");
                row.MouseLeave += (s, e) => row.Background = Brushes.Transparent;
                row.MouseLeftButtonUp += (s, e) => OpenPath(path);
                recentList.Children.Add(row);
            }
            if (recentList.Children.Count == 0)
                recentList.Children.Add(new TextBlock { Text = "（暂无最近文件）", Foreground = Muted, FontSize = 12.5 });
        }

        List<string> LoadRecent()
        {
            try { if (File.Exists(RecentPath)) return File.ReadAllLines(RecentPath).Where(File.Exists).Distinct().Take(8).ToList(); }
            catch { }
            return new List<string>();
        }
        void AddRecent(string path)
        {
            try
            {
                var list = LoadRecent();
                list.Remove(path);
                list.Insert(0, path);
                Directory.CreateDirectory(IOPath.GetDirectoryName(RecentPath));
                File.WriteAllLines(RecentPath, list.Take(8));
            }
            catch { }
        }

        void ToggleReadonly()
        {
            readOnly = !readOnly;
            if (readOnly && editing != null) CommitEdit();
            btnRead.Content = readOnly ? T("👁 阅读模式 ✓", "👁 Read ✓") : T("👁 阅读模式", "👁 Read");
        }

        void OpenFileMenu()
        {
            var m = new ContextMenu();
            MenuItem MI(string h, Action a) { var mi = new MenuItem { Header = h }; mi.Click += (s, e) => a(); return mi; }
            m.Items.Add(MI(T("新建大括号图", "New brace map"), () => DoNew()));
            m.Items.Add(MI(T("新建蜘蛛网图", "New spider map"), () => DoNewSpider()));
            m.Items.Add(MI(T("打开", "Open"), () => DoOpen()));
            m.Items.Add(new Separator());
            m.Items.Add(MI(T("保存", "Save"), () => DoSave()));
            m.Items.Add(MI(T("另存为…", "Save As…"), () => DoSaveAs()));
            m.Items.Add(new Separator());
            var export = new MenuItem { Header = T("导出", "Export") };
            export.Items.Add(MI(T("导出 PNG 图片", "Export PNG"), () => ExportImage(false)));
            export.Items.Add(MI(T("导出 JPG 图片", "Export JPG"), () => ExportImage(true)));
            export.Items.Add(MI(T("导出 PDF", "Export PDF"), () => ExportPdf()));
            m.Items.Add(export);
            m.Items.Add(MI(T("设置", "Settings"), () => OpenSettings()));
            m.PlacementTarget = btnFile; m.IsOpen = true;
        }

        void OpenPath(string path)
        {
            try
            {
                doc = BmapIO.Load(File.ReadAllText(path));
                curName = IOPath.GetFileNameWithoutExtension(path);
                curPath = path; dirty = false;
                undoStack.Clear(); redoStack.Clear();
                selected = editing = null;
                ApplyDocType();
                RefreshLabels();
                AddRecent(path);
                Rebuild(); HideStart(); Fit();
            }
            catch (Exception ex) { MessageBox.Show("打开失败：" + ex.Message); }
        }

        void RefreshLabels()
        {
            fname.Text = curName + (dirty ? " ●" : "");
            Title = "ThoughtCanvas — " + curName + (dirty ? " *" : "");
        }

        // ========== 坐标 ==========
        Point ToWorld(Point screen) => new Point((screen.X - offX) / scale, (screen.Y - offY) / scale);
        void ApplyTransform()
        {
            var tg = new TransformGroup();
            tg.Children.Add(new ScaleTransform(scale, scale));
            tg.Children.Add(new TranslateTransform(offX, offY));
            world.RenderTransform = tg;
        }
        static string F(double d) => d.ToString(CultureInfo.InvariantCulture);
        static bool Fin(double d) => !double.IsNaN(d) && !double.IsInfinity(d);

        // ========== 重建可视 + 排版 ==========
        readonly Dictionary<string, Topic> byId = new Dictionary<string, Topic>();
        Topic focusTopic;
        void Rebuild()
        {
            world.Children.Clear();
            cardOf.Clear(); textOf.Clear(); editOf.Clear(); bracePaths.Clear();
            byId.Clear();
            foreach (var t in AllTopics()) byId[t.Id] = t;
            focusTopic = (doc.FocusId != null && byId.TryGetValue(doc.FocusId, out var f)) ? f : null;
            ComputeNumbers();
            foreach (var r in RenderRoots()) CreateCards(r);
            Relayout();
            RenderOutline();
            UpdateFocusBar();
        }
        // 聚焦时只渲染该子树，否则渲染所有根
        IEnumerable<Topic> RenderRoots() => focusTopic != null ? new[] { focusTopic } : (IEnumerable<Topic>)roots;

        void ComputeNumbers()
        {
            foreach (var t in AllTopics()) t.Num = "";
            if (numbering == "" || IsSpider) return;
            foreach (var r in roots) NumberWalk(r, "");
        }
        void NumberWalk(Topic t, string prefix)
        {
            for (int i = 0; i < t.Children.Count; i++)
            {
                string tok = NumToken(i + 1, numbering);
                string n = prefix == "" ? tok : prefix + "." + tok;
                t.Children[i].Num = n;
                NumberWalk(t.Children[i], n);
            }
        }
        // 编号 token：num=1.2.3 / alpha=A.B.C / lalpha=a.b.c / roman=I.II.III
        static string NumToken(int n, string scheme)
        {
            switch (scheme)
            {
                case "alpha":  return AlphaOf(n);
                case "lalpha": return AlphaOf(n).ToLowerInvariant();
                case "roman":  return RomanOf(n);
                default:       return n.ToString();
            }
        }
        static string AlphaOf(int n) { string s = ""; while (n > 0) { n--; s = (char)('A' + n % 26) + s; n /= 26; } return s; }
        static string RomanOf(int n)
        {
            int[] v = { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
            string[] sym = { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < v.Length; i++) while (n >= v[i]) { sb.Append(sym[i]); n -= v[i]; }
            return sb.ToString();
        }
        // 卡片显示文字（带编号前缀；空主题显示占位）
        (string text, bool empty) CardDisplay(Topic t)
        {
            bool empty = string.IsNullOrWhiteSpace(t.Text);
            string body = empty ? "双击编辑" : t.Text;
            return ((string.IsNullOrEmpty(t.Num) ? "" : t.Num + "  ") + body, empty);
        }

        static Brush ParseBg(string c)
        {
            if (string.IsNullOrEmpty(c)) return Brushes.White;
            try { return (Brush)new BrushConverter().ConvertFromString(c); } catch { return Brushes.White; }
        }

        // ========== 富节点：标记 / 标签 / 备注 / 链接 / 代办 ==========
        class MarkerDef { public string Group; public string Color; public string Text; public int Prog = -1; public string Emoji; }
        static readonly Dictionary<string, MarkerDef> MarkerDefs = new Dictionary<string, MarkerDef>
        {
            ["p1"] = new MarkerDef { Group = "prio", Color = "#e3604f", Text = "1" },
            ["p2"] = new MarkerDef { Group = "prio", Color = "#ef8f3c", Text = "2" },
            ["p3"] = new MarkerDef { Group = "prio", Color = "#e9b938", Text = "3" },
            ["p4"] = new MarkerDef { Group = "prio", Color = "#5ab867", Text = "4" },
            ["p5"] = new MarkerDef { Group = "prio", Color = "#3f9bd6", Text = "5" },
            ["p6"] = new MarkerDef { Group = "prio", Color = "#7b6cf0", Text = "6" },
            ["t0"] = new MarkerDef { Group = "prog", Prog = 0 },
            ["t1"] = new MarkerDef { Group = "prog", Prog = 25 },
            ["t2"] = new MarkerDef { Group = "prog", Prog = 50 },
            ["t3"] = new MarkerDef { Group = "prog", Prog = 75 },
            ["t4"] = new MarkerDef { Group = "prog", Prog = 100 },
            ["flag"] = new MarkerDef { Group = "emoji", Emoji = "🚩" },
            ["star"] = new MarkerDef { Group = "emoji", Emoji = "⭐" },
            ["people"] = new MarkerDef { Group = "emoji", Emoji = "👤" },
            ["yes"] = new MarkerDef { Group = "emoji", Emoji = "✅" },
            ["no"] = new MarkerDef { Group = "emoji", Emoji = "❌" },
            ["q"] = new MarkerDef { Group = "emoji", Emoji = "❓" },
            ["excl"] = new MarkerDef { Group = "emoji", Emoji = "❗" },
            ["idea"] = new MarkerDef { Group = "emoji", Emoji = "💡" },
            ["heart"] = new MarkerDef { Group = "emoji", Emoji = "❤️" },
            ["like"] = new MarkerDef { Group = "emoji", Emoji = "👍" },
            ["fire"] = new MarkerDef { Group = "emoji", Emoji = "🔥" },
        };
        static readonly (string name, string[] keys)[] MarkerGroups =
        {
            ("优先级（同组互斥）", new[] { "p1", "p2", "p3", "p4", "p5", "p6" }),
            ("进度（同组互斥）",   new[] { "t0", "t1", "t2", "t3", "t4" }),
            ("旗帜 · 星 · 人物",    new[] { "flag", "star", "people" }),
            ("符号",               new[] { "yes", "no", "q", "excl", "idea", "heart", "like", "fire" }),
        };

        FrameworkElement MakeMarkerIcon(string key)
        {
            var d = MarkerDefs[key];
            if (d.Group == "prio")
                return new Border
                {
                    Width = 16, Height = 16, CornerRadius = new CornerRadius(8),
                    Background = ParseBg(d.Color),
                    Child = new TextBlock { Text = d.Text, Foreground = Brushes.White, FontSize = 10, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                };
            if (d.Group == "prog") return MakeProgressIcon(d.Prog);
            return new TextBlock { Text = d.Emoji, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
        }

        FrameworkElement MakeProgressIcon(int p)
        {
            const double cx = 8, cy = 8, r = 7;
            var g = new Grid { Width = 16, Height = 16 };
            g.Children.Add(new Ellipse { Width = 16, Height = 16, Stroke = Accent, StrokeThickness = 1.3, Fill = Brushes.White });
            if (p >= 100)
                g.Children.Add(new Ellipse { Width = 13, Height = 13, Fill = Accent, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            else if (p > 0)
            {
                double ang = p * 3.6 * Math.PI / 180.0;
                double ex = cx + r * Math.Sin(ang), ey = cy - r * Math.Cos(ang);
                int large = p > 50 ? 1 : 0;
                string dStr = $"M {F(cx)},{F(cy)} L {F(cx)},{F(cy - r)} A {F(r)},{F(r)} 0 {large} 1 {F(ex)},{F(ey)} Z";
                try { g.Children.Add(new ShapePath { Data = Geometry.Parse(dStr), Fill = Accent }); } catch { }
            }
            return g;
        }

        WrapPanel BuildMarkerRow(Topic t)
        {
            bool any = t.Markers.Count > 0 || !string.IsNullOrWhiteSpace(t.Note) || !string.IsNullOrWhiteSpace(t.Link);
            if (!any) return null;
            var wp = new WrapPanel { Margin = new Thickness(0, 5, 0, 0) };
            foreach (var k in t.Markers)
            {
                if (!MarkerDefs.ContainsKey(k)) continue;
                var ic = MakeMarkerIcon(k);
                ic.Margin = new Thickness(0, 0, 4, 0); ic.Cursor = Cursors.Hand; ic.ToolTip = "点击移除标记";
                string key = k;
                ic.MouseLeftButtonDown += (s, e) => { e.Handled = true; if (!readOnly) ToggleMarker(t, key); };
                wp.Children.Add(ic);
            }
            if (!string.IsNullOrWhiteSpace(t.Note))
            {
                var n = new TextBlock { Text = "📝", FontSize = 13, Margin = new Thickness(0, 0, 4, 0), Cursor = Cursors.Hand, ToolTip = "查看/编辑备注" };
                n.MouseLeftButtonDown += (s, e) => { e.Handled = true; OpenNote(t); };
                wp.Children.Add(n);
            }
            if (!string.IsNullOrWhiteSpace(t.Link))
            {
                var l = new TextBlock { Text = "🔗", FontSize = 13, Cursor = Cursors.Hand, ToolTip = t.Link };
                l.MouseLeftButtonDown += (s, e) => { e.Handled = true; OpenLink(t); };
                wp.Children.Add(l);
            }
            return wp;
        }

        WrapPanel BuildTagRow(Topic t)
        {
            if (t.Tags.Count == 0) return null;
            var wp = new WrapPanel { Margin = new Thickness(0, 5, 0, 0) };
            for (int i = 0; i < t.Tags.Count; i++)
            {
                int idx = i;
                var chip = new Border
                {
                    Background = AccentSoft, CornerRadius = new CornerRadius(8), Padding = new Thickness(7, 1, 7, 1),
                    Margin = new Thickness(0, 0, 4, 0), Cursor = Cursors.Hand, ToolTip = "点击删除标签",
                    Child = new TextBlock { Text = t.Tags[idx], FontSize = 11, Foreground = Accent }
                };
                chip.MouseLeftButtonDown += (s, e) => { e.Handled = true; if (!readOnly) { PushUndo(); t.Tags.RemoveAt(idx); Rebuild(); Select(t); } };
                wp.Children.Add(chip);
            }
            return wp;
        }

        void ToggleMarker(Topic t, string key)
        {
            if (readOnly || !MarkerDefs.TryGetValue(key, out var d)) return;
            PushUndo();
            if (t.Markers.Contains(key)) t.Markers.Remove(key);
            else
            {
                if (d.Group == "prio" || d.Group == "prog")
                    t.Markers.RemoveAll(k => MarkerDefs.TryGetValue(k, out var dd) && dd.Group == d.Group);
                t.Markers.Add(key);
            }
            Rebuild(); Select(t);
        }

        void OpenMarkerPicker(Topic t)
        {
            var w = new Window { Title = "标记", SizeToContent = SizeToContent.WidthAndHeight, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this, ResizeMode = ResizeMode.NoResize, Background = Brushes.White };
            var root = new StackPanel { Margin = new Thickness(18) };
            var groupsHost = new StackPanel();

            void Paint()
            {
                groupsHost.Children.Clear();
                foreach (var (name, keys) in MarkerGroups)
                {
                    groupsHost.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, FontSize = 12.5, Foreground = Ink, Margin = new Thickness(0, 8, 0, 6) });
                    var wp = new WrapPanel { MaxWidth = 320 };
                    foreach (var k in keys)
                    {
                        string key = k;
                        bool on = t.Markers.Contains(key);
                        var cell = new Border
                        {
                            Padding = new Thickness(6), Margin = new Thickness(0, 0, 6, 6), CornerRadius = new CornerRadius(8),
                            Cursor = Cursors.Hand, BorderThickness = new Thickness(2),
                            BorderBrush = on ? Accent : Brushes.Transparent, Background = on ? AccentSoft : Brushes.Transparent,
                            Child = MakeMarkerIcon(k)
                        };
                        cell.MouseLeftButtonUp += (s, e) => { ToggleMarker(t, key); Paint(); };
                        wp.Children.Add(cell);
                    }
                    groupsHost.Children.Add(wp);
                }
            }
            Paint();
            root.Children.Add(groupsHost);
            var close = new Button { Content = "完成", Style = (Style)Resources["Btn"], HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            close.Click += (s, e) => w.Close();
            root.Children.Add(close);
            w.Content = root;
            w.ShowDialog();
        }

        void OpenNote(Topic t)
        {
            string r = PromptText("备注", "为「" + (t.Text == "" ? "(空白主题)" : t.Text) + "」添加备注：", t.Note, multiline: true);
            if (r == null) return;
            PushUndo(); t.Note = r.Trim(); Rebuild(); Select(t);
        }
        void OpenLink(Topic t)
        {
            string r = PromptText("超链接", "链接地址（http://… 或文件路径）：", t.Link, multiline: false);
            if (r == null) return;
            PushUndo(); t.Link = r.Trim(); Rebuild(); Select(t);
        }
        void AddTag(Topic t)
        {
            string r = PromptText("标签", "输入标签（多个用逗号分隔）：", "", multiline: false);
            if (string.IsNullOrWhiteSpace(r)) return;
            PushUndo();
            foreach (var s in r.Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries))
            { var v = s.Trim(); if (v != "" && !t.Tags.Contains(v)) t.Tags.Add(v); }
            Rebuild(); Select(t);
        }
        void ToggleTodo(Topic t)
        {
            PushUndo(); t.Todo = !t.Todo; if (!t.Todo) t.Done = false; Rebuild(); Select(t);
        }

        // 通用输入框：返回 null 表示取消
        string PromptText(string title, string label, string initial, bool multiline)
        {
            var w = new Window { Title = title, Width = 420, Height = multiline ? 260 : 168, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this, ResizeMode = ResizeMode.NoResize, Background = Brushes.White };
            var sp = new StackPanel { Margin = new Thickness(18) };
            sp.Children.Add(new TextBlock { Text = label, Foreground = Ink, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap });
            var box = new TextBox
            {
                Text = initial ?? "", FontSize = 13.5, Padding = new Thickness(8, 6, 8, 6),
                AcceptsReturn = multiline, TextWrapping = TextWrapping.Wrap, MinHeight = multiline ? 110 : 0,
                VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled
            };
            sp.Children.Add(box);
            string result = null;
            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var ok = new Button { Content = "确定", Style = (Style)Resources["BtnPrimary"], MinWidth = 64 };
            var cancel = new Button { Content = "取消", Style = (Style)Resources["Btn"], MinWidth = 64, Margin = new Thickness(8, 0, 0, 0) };
            ok.Click += (s, e) => { result = box.Text; w.Close(); };
            cancel.Click += (s, e) => { result = null; w.Close(); };
            btns.Children.Add(ok); btns.Children.Add(cancel);
            sp.Children.Add(btns);
            w.Content = sp;
            box.Focus(); box.SelectAll();
            if (!multiline) box.KeyDown += (s, e) => { if (e.Key == Key.Enter) { result = box.Text; w.Close(); } };
            w.ShowDialog();
            return result;
        }

        void CreateCards(Topic t)
        {
            var tbk = new TextBlock
            {
                FontSize = 14, Foreground = Ink, TextWrapping = TextWrapping.Wrap,
                MaxWidth = 220, TextAlignment = TextAlignment.Center
            };
            var (disp, empty) = CardDisplay(t);
            tbk.Text = disp;
            if (empty) tbk.Foreground = Muted;
            if (t.Todo && t.Done) { tbk.TextDecorations = TextDecorations.Strikethrough; tbk.Foreground = Muted; }

            var ed = new TextBox
            {
                Visibility = Visibility.Collapsed, FontSize = 14, MinWidth = 70,
                BorderThickness = new Thickness(0), Background = Brushes.Transparent,
                TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 220,
                AcceptsReturn = false
            };
            ed.PreviewKeyDown += Edit_PreviewKeyDown;
            ed.LostKeyboardFocus += (s, e) => CommitEdit();

            var grid = new Grid();
            grid.Children.Add(tbk);
            grid.Children.Add(ed);

            // 内容竖排：[代办☑ + 文字] / 标记行 / 标签行
            var content = new StackPanel();
            if (t.Todo)
            {
                var line = new StackPanel { Orientation = Orientation.Horizontal };
                var chk = new CheckBox { IsChecked = t.Done, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
                chk.Checked   += (s, e) => { if (!readOnly) { PushUndo(); t.Done = true;  Rebuild(); Select(t); } };
                chk.Unchecked += (s, e) => { if (!readOnly) { PushUndo(); t.Done = false; Rebuild(); Select(t); } };
                chk.PreviewMouseDown += (s, e) => e.Handled = false;
                line.Children.Add(chk);
                line.Children.Add(grid);
                content.Children.Add(line);
            }
            else content.Children.Add(grid);

            var mkRow = BuildMarkerRow(t);
            if (mkRow != null) content.Children.Add(mkRow);
            var tagRow = BuildTagRow(t);
            if (tagRow != null) content.Children.Add(tagRow);

            var border = new Border
            {
                Background = ParseBg(t.Bg),
                BorderBrush = (t == selected) ? Accent : CardBorder,
                BorderThickness = new Thickness(t == selected ? 2 : 1.5),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14, 9, 14, 9),
                MinWidth = 96,
                Child = content,
                Tag = t
            };
            Panel.SetZIndex(border, 1);
            border.MouseLeftButtonDown += Card_MouseDown;
            border.MouseMove += Card_MouseMove;
            border.MouseLeftButtonUp += Card_MouseUp;
            border.ContextMenuOpening += (s, e) => Select(t);
            border.ContextMenu = BuildTopicMenu(t);

            cardOf[t] = border; textOf[t] = tbk; editOf[t] = ed;
            world.Children.Add(border);

            foreach (var c in t.Children) CreateCards(c);
        }

        void Relayout()
        {
            foreach (var kv in cardOf)
            {
                kv.Value.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                kv.Key.W = kv.Value.DesiredSize.Width;
                kv.Key.H = kv.Value.DesiredSize.Height;
            }
            foreach (var r in RenderRoots()) { MeasureTree(r); PlaceTree(r, r.X, r.Y); }
            foreach (var kv in cardOf)
            {
                Canvas.SetLeft(kv.Value, kv.Key.LX);
                Canvas.SetTop(kv.Value, kv.Key.CY - kv.Key.H / 2);
            }
            DrawBraces();
            DrawOverlays();
        }

        double MeasureTree(Topic t)
        {
            if (t.Children.Count == 0) { t.Sub = t.H; return t.Sub; }
            double total = 0;
            for (int i = 0; i < t.Children.Count; i++) { total += MeasureTree(t.Children[i]); if (i > 0) total += V_GAP; }
            t.Sub = Math.Max(t.H, total);
            return t.Sub;
        }

        void PlaceTree(Topic t, double leftX, double cy)
        {
            t.LX = leftX; t.CY = cy;
            if (t.Children.Count == 0) return;
            double childLeft = leftX + t.W + BRANCH_GAP;
            double total = 0;
            for (int i = 0; i < t.Children.Count; i++) { total += t.Children[i].Sub; if (i > 0) total += V_GAP; }
            double y = cy - total / 2;
            for (int i = 0; i < t.Children.Count; i++)
            {
                var c = t.Children[i];
                double cc = y + c.Sub / 2;
                // 横向错位：相邻子主题左缘交替错开，纵向更紧凑也不挤
                double zig = doc.CompactMode == 2 ? (i % 2) * 22 : 0;
                PlaceTree(c, childLeft + zig, cc);
                y += c.Sub + V_GAP;
            }
        }

        void DrawBraces()
        {
            foreach (var p in bracePaths) world.Children.Remove(p);
            bracePaths.Clear();
            foreach (var t in AllTopics())
            {
                if (t.Children.Count == 0) continue;
                if (!cardOf.ContainsKey(t) || !cardOf.ContainsKey(t.Children[0])) continue;   // 聚焦时未渲染的节点跳过
                double xR = t.LX + t.W, xCusp = xR + 14, xTip = xR + 24, xBase = xR + 30;
                double top = t.Children[0].CY, bot = t.Children[t.Children.Count - 1].CY, mid = t.CY;
                double y0 = Math.Min(top, mid - 16), y1 = Math.Max(bot, mid + 16);
                double r = Math.Min(14, (y1 - y0) / 4);
                if (!Fin(xR) || !Fin(y0) || !Fin(y1) || !Fin(mid) || !Fin(r)) continue;   // 防 NaN/∞ 导致 Geometry.Parse 崩溃
                string d = $"M {F(xBase)} {F(y0)} Q {F(xTip)} {F(y0)} {F(xTip)} {F(y0 + r)} L {F(xTip)} {F(mid - r)} " +
                           $"Q {F(xTip)} {F(mid)} {F(xCusp)} {F(mid)} Q {F(xTip)} {F(mid)} {F(xTip)} {F(mid + r)} " +
                           $"L {F(xTip)} {F(y1 - r)} Q {F(xTip)} {F(y1)} {F(xBase)} {F(y1)}";
                Geometry geo;
                try { geo = Geometry.Parse(d); } catch { continue; }
                var path = new ShapePath
                {
                    Data = geo, Stroke = BraceBrush, StrokeThickness = 2,
                    StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
                };
                Panel.SetZIndex(path, 0);
                world.Children.Add(path);
                bracePaths.Add(path);
            }
        }

        IEnumerable<Topic> AllTopics()
        {
            var stack = new Stack<Topic>(roots);
            var seen = new HashSet<Topic>();
            while (stack.Count > 0) { var t = stack.Pop(); if (!seen.Add(t)) continue; yield return t; foreach (var c in t.Children) stack.Push(c); }
        }

        // ========== 大纲视图（导图 ↔ 大纲 实时双向同步） ==========
        readonly Dictionary<Topic, TextBox> outlineRowOf = new Dictionary<Topic, TextBox>();
        readonly Dictionary<Topic, Border> outlineBorderOf = new Dictionary<Topic, Border>();
        bool outlineSyncing;

        bool OutlineOpen => outlinePanel.Visibility == Visibility.Visible;

        void ToggleOutline()
        {
            bool open = !OutlineOpen;
            outlinePanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            btnOutline.Content = open ? "📋 大纲 ✓" : "📋 大纲";
            if (open) RenderOutline();
        }

        void RenderOutline()
        {
            if (!OutlineOpen) return;
            outlineSyncing = true;
            outlineList.Children.Clear();
            outlineRowOf.Clear(); outlineBorderOf.Clear();
            foreach (var r in roots) AddOutlineRows(r, 0);
            if (outlineList.Children.Count == 0)
                outlineList.Children.Add(new TextBlock { Text = "（空，回到画布添加主题）", Foreground = Muted, FontSize = 12.5, Margin = new Thickness(6) });
            outlineSyncing = false;
        }

        void AddOutlineRows(Topic t, int depth)
        {
            outlineList.Children.Add(BuildOutlineRow(t, depth));
            foreach (var c in t.Children) AddOutlineRows(c, depth + 1);
        }

        Border BuildOutlineRow(Topic t, int depth)
        {
            var bullet = new TextBlock
            {
                Text = string.IsNullOrEmpty(t.Num) ? (depth == 0 ? "●" : (t.Children.Count > 0 ? "▸" : "·")) : t.Num,
                Foreground = depth == 0 ? Accent : Muted, FontSize = depth == 0 ? 13 : 12,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0), MinWidth = 14
            };
            var box = new TextBox
            {
                Text = t.Text, Tag = t, BorderThickness = new Thickness(0), Background = Brushes.Transparent,
                FontSize = depth == 0 ? 14.5 : 13.5, Foreground = Ink, Padding = new Thickness(0),
                FontWeight = depth == 0 ? FontWeights.SemiBold : FontWeights.Normal,
                IsReadOnly = readOnly, VerticalContentAlignment = VerticalAlignment.Center
            };
            box.TextChanged += OutlineText_Changed;
            box.PreviewKeyDown += OutlineRow_KeyDown;
            box.GotKeyboardFocus += (s, e) => { if (!outlineSyncing) Select(t); };
            outlineRowOf[t] = box;

            var dp = new DockPanel { Margin = new Thickness(depth * 20, 2, 4, 2) };
            DockPanel.SetDock(bullet, Dock.Left);
            dp.Children.Add(bullet);
            dp.Children.Add(box);

            var row = new Border
            {
                Child = dp, CornerRadius = new CornerRadius(6), Padding = new Thickness(4, 1, 4, 1),
                Background = (t == selected) ? AccentSoft : Brushes.Transparent
            };
            outlineBorderOf[t] = row;
            return row;
        }

        // 同步大纲行高亮（不重建，保持焦点）
        void HighlightOutline()
        {
            foreach (var kv in outlineBorderOf)
                kv.Value.Background = selSet.Contains(kv.Key) ? AccentSoft : Brushes.Transparent;
        }

        void OutlineText_Changed(object sender, TextChangedEventArgs e)
        {
            if (outlineSyncing) return;
            var box = (TextBox)sender; var t = (Topic)box.Tag;
            t.Text = box.Text; dirty = true;
            // 实时同步到画布卡片（不重建，避免大纲失焦）
            if (textOf.TryGetValue(t, out var disp))
            {
                var (txt, empty) = CardDisplay(t);
                disp.Text = txt; disp.Foreground = empty ? Muted : Ink;
                if (cardOf.TryGetValue(t, out var bd)) bd.UpdateLayout();
            }
        }

        void OutlineRow_KeyDown(object sender, KeyEventArgs e)
        {
            if (readOnly) return;
            var box = (TextBox)sender; var t = (Topic)box.Tag;
            if (e.Key == Key.Enter)
            {
                e.Handled = true; t.Text = box.Text;
                AddSiblingOutline(t);
            }
            else if (e.Key == Key.Tab)
            {
                e.Handled = true; t.Text = box.Text;
                if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) OutdentOutline(t); else IndentOutline(t);
            }
            else if (e.Key == Key.Back && box.Text.Length == 0 && box.CaretIndex == 0)
            {
                e.Handled = true; DeleteFromOutline(t);
            }
            else if (e.Key == Key.Up && box.CaretIndex == 0)
            {
                e.Handled = true; FocusOutlineNeighbor(t, -1);
            }
            else if (e.Key == Key.Down && box.CaretIndex == box.Text.Length)
            {
                e.Handled = true; FocusOutlineNeighbor(t, +1);
            }
        }

        // 大纲里按上下键跨行移动光标
        void FocusOutlineNeighbor(Topic t, int dir)
        {
            var order = new List<Topic>();
            foreach (var r in roots) OutlineOrder(r, order);
            int i = order.IndexOf(t) + dir;
            if (i >= 0 && i < order.Count) FocusOutlineRow(order[i]);
        }
        void OutlineOrder(Topic t, List<Topic> outList) { outList.Add(t); foreach (var c in t.Children) OutlineOrder(c, outList); }

        void AddSiblingOutline(Topic t)
        {
            var p = FindParent(t);
            PushUndo();
            var c = new Topic();
            if (p != null) p.Children.Insert(p.Children.IndexOf(t) + 1, c);
            else { c.X = t.X; c.Y = t.Y + 90; roots.Add(c); }
            Rebuild(); Select(c); FocusOutlineRow(c);
        }

        void IndentOutline(Topic t)
        {
            var p = FindParent(t);
            var arr = p != null ? p.Children : roots;
            int i = arr.IndexOf(t);
            if (i <= 0) return;                       // 没有前一个同级，无法缩进
            var prev = arr[i - 1];
            PushUndo();
            arr.RemoveAt(i);
            prev.Children.Add(t);
            Rebuild(); Select(t); FocusOutlineRow(t);
        }

        void OutdentOutline(Topic t)
        {
            var p = FindParent(t);
            if (p == null) return;                    // 已是根，无法升级
            var gp = FindParent(p);
            var parr = gp != null ? gp.Children : roots;
            PushUndo();
            p.Children.Remove(t);
            if (gp == null && t.X == 0 && t.Y == 0) { t.X = p.X; t.Y = p.Y + 90; }  // 升为根需有坐标
            parr.Insert(parr.IndexOf(p) + 1, t);
            Rebuild(); Select(t); FocusOutlineRow(t);
        }

        void DeleteFromOutline(Topic t)
        {
            var order = new List<Topic>();
            foreach (var r in roots) OutlineOrder(r, order);
            int i = order.IndexOf(t);
            Topic focusAfter = i > 0 ? order[i - 1] : null;
            DeleteTopic(t);
            if (focusAfter != null) FocusOutlineRow(focusAfter);
        }

        void FocusOutlineRow(Topic t)
        {
            // Rebuild 已重排大纲行，等布局完再聚焦
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (outlineRowOf.TryGetValue(t, out var box)) { box.Focus(); box.CaretIndex = box.Text.Length; }
            }), System.Windows.Threading.DispatcherPriority.Input);
        }

        // ========== 选择 / 编辑 ==========
        readonly HashSet<Topic> selSet = new HashSet<Topic>();
        void Select(Topic t)
        {
            selSet.Clear(); if (t != null) selSet.Add(t);
            selected = t; selOverlay = null;
            RepaintSelection();
            if (OutlineOpen) HighlightOutline();
        }
        // Ctrl+点击：把 t 加入/移出多选集（外框/概要需要多选）
        void SelectToggle(Topic t)
        {
            if (t == null) return;
            if (!selSet.Add(t)) selSet.Remove(t);
            selected = selSet.Contains(t) ? t : System.Linq.Enumerable.FirstOrDefault(selSet);
            selOverlay = null;
            RepaintSelection();
            if (OutlineOpen) HighlightOutline();
        }
        void RepaintSelection()
        {
            foreach (var kv in cardOf)
            {
                bool sel = selSet.Contains(kv.Key);
                kv.Value.BorderBrush = sel ? Accent : CardBorder;
                kv.Value.BorderThickness = new Thickness(sel ? 2 : 1.5);
            }
        }

        void BeginEdit(Topic t)
        {
            if (readOnly || t == null || !editOf.ContainsKey(t)) return;
            editing = t;
            Select(t);
            var ed = editOf[t]; var tbk = textOf[t];
            tbk.Visibility = Visibility.Collapsed;
            ed.Text = t.Text;
            ed.Visibility = Visibility.Visible;
            ed.Focus();
            ed.SelectAll();
        }

        void CommitEdit()
        {
            if (editing == null) return;
            var t = editing;
            if (editOf.TryGetValue(t, out var ed) && t.Text != ed.Text) { t.Text = ed.Text; dirty = true; }
            editing = null;
            Rebuild();
            Select(t);
        }

        void Edit_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var t = editing;
            if (t == null) return;
            if (IsSpider)
            {
                if (e.Key == Key.Enter) { e.Handled = true; CommitEdit(); AddSpiderNodeNear(t); }
                else if (e.Key == Key.Escape) { e.Handled = true; CommitEdit(); }
                return;
            }
            if (e.Key == Key.Enter) { e.Handled = true; CommitEdit(); AddSibling(t); }
            else if (e.Key == Key.Tab) { e.Handled = true; CommitEdit(); AddChild(t); }
            else if (e.Key == Key.Escape) { e.Handled = true; CommitEdit(); }
        }

        // ========== 增删 ==========
        Topic FindParent(Topic t)
        {
            foreach (var any in AllTopics())
                if (any.Children.Contains(t)) return any;
            return null;
        }

        void AddChild(Topic t)
        {
            if (readOnly) return;
            PushUndo();
            var c = new Topic();
            t.Children.Add(c);
            Rebuild(); Select(c); EnsureVisible(c); BeginEdit(c);
        }

        void AddSibling(Topic t)
        {
            if (readOnly) return;
            var p = FindParent(t);
            PushUndo();
            var c = new Topic();
            if (p != null) p.Children.Insert(p.Children.IndexOf(t) + 1, c);
            else { c.X = t.X; c.Y = t.Y + 90; roots.Add(c); }
            Rebuild(); Select(c); EnsureVisible(c); BeginEdit(c);
        }

        void DeleteTopic(Topic t)
        {
            if (readOnly || t == null) return;
            PushUndo();
            // 收集被删子树的所有 id，清理引用它们的连线/叠加层
            var gone = new HashSet<string>();
            void Collect(Topic x) { gone.Add(x.Id); foreach (var c in x.Children) Collect(c); }
            Collect(t);
            PruneReferences(gone);
            var p = FindParent(t);
            if (p != null) p.Children.Remove(t);
            else roots.Remove(t);
            Rebuild();
            Select(p);
        }

        // 删除节点后，移除指向这些 id 的连线/联系/标注/外框/概要成员
        void PruneReferences(HashSet<string> gone)
        {
            doc.Links.RemoveAll(l => gone.Contains(l.A) || gone.Contains(l.B));
            doc.Relations.RemoveAll(r => gone.Contains(r.A) || gone.Contains(r.B));
            doc.Callouts.RemoveAll(c => gone.Contains(c.Tb));
            foreach (var b in doc.Boundaries) b.Members.RemoveAll(gone.Contains);
            doc.Boundaries.RemoveAll(b => b.Members.Count == 0);
            foreach (var s in doc.Summaries) s.Members.RemoveAll(gone.Contains);
            doc.Summaries.RemoveAll(s => s.Members.Count == 0);
            if (doc.FocusId != null && gone.Contains(doc.FocusId)) doc.FocusId = null;
        }

        void AddRootAt(Point world)
        {
            if (readOnly) return;
            PushUndo();
            var t = new Topic { X = world.X - 50, Y = world.Y };
            roots.Add(t);
            Rebuild(); Select(t); BeginEdit(t);
        }

        ContextMenu BuildTopicMenu(Topic t)
        {
            var m = new ContextMenu();
            MenuItem MI(string h, Action a) { var mi = new MenuItem { Header = h }; mi.Click += (s, e) => a(); return mi; }
            m.Items.Add(MI(T("编辑文字", "Edit text"), () => BeginEdit(t)));
            if (IsSpider)
            {
                m.Items.Add(MI(T("新建相邻节点", "New adjacent node"), () => AddSpiderNodeNear(t)));
                m.Items.Add(MI(T("连线（需选中2项）", "Connect (select 2)"), () => AddLinkFromSelection()));
                m.Items.Add(MI(T("添加锚点（点边缘落点，Esc 取消）", "Add anchor (click edge, Esc cancels)"), () => StartAddAnchor(t)));
                if (t.Anchors.Count > 0)
                    m.Items.Add(MI(T("清除自定义锚点", "Clear custom anchors"), () => { PushUndo(); t.Anchors.Clear(); Relayout(); }));
            }
            else
            {
                m.Items.Add(MI(T("添加子主题", "Add subtopic"), () => AddChild(t)));
                m.Items.Add(MI(T("添加同级主题", "Add sibling"), () => AddSibling(t)));
            }
            m.Items.Add(new Separator());
            m.Items.Add(MI(T("标记…", "Markers…"), () => OpenMarkerPicker(t)));
            m.Items.Add(MI(T("添加标签…", "Add tag…"), () => AddTag(t)));
            m.Items.Add(MI(string.IsNullOrWhiteSpace(t.Note) ? T("添加备注…", "Add note…") : T("编辑备注…", "Edit note…"), () => OpenNote(t)));
            m.Items.Add(MI(string.IsNullOrWhiteSpace(t.Link) ? T("添加链接…", "Add link…") : T("编辑链接…", "Edit link…"), () => OpenLink(t)));
            m.Items.Add(MI(t.Todo ? T("取消代办", "Remove todo") : T("设为代办", "Make todo"), () => ToggleTodo(t)));
            m.Items.Add(new Separator());
            if (!IsSpider)
            {
                var overlay = new MenuItem { Header = T("叠加层", "Overlays") };
                overlay.Items.Add(MI(T("加外框（选中项）", "Add boundary (selected)"), () => AddBoundary()));
                overlay.Items.Add(MI(T("加概要（选中项）", "Add summary (selected)"), () => AddSummary()));
                overlay.Items.Add(MI(T("加标注…", "Add callout…"), () => AddCallout(t)));
                overlay.Items.Add(MI(T("加联系（需选中2项）", "Add relation (select 2)"), () => AddRelation()));
                m.Items.Add(overlay);
                m.Items.Add(MI(T("聚焦此主题（下钻）", "Focus (drill in)"), () => SetFocus(t)));
                m.Items.Add(new Separator());
            }
            var color = new MenuItem { Header = "背景颜色" };
            foreach (var c in BgPresets)
            {
                string cc = c;
                var item = new MenuItem { Header = c == "" ? "默认（无）" : c };
                item.Click += (s, e) => SetBg(t, cc);
                color.Items.Add(item);
            }
            color.Header = T("背景颜色", "Background");
            m.Items.Add(color);
            m.Items.Add(new Separator());
            m.Items.Add(MI(T("删除", "Delete"), () => DeleteTopic(t)));
            return m;
        }
        void SetBg(Topic t, string c) { if (readOnly) return; PushUndo(); t.Bg = c; Rebuild(); Select(t); }

        // ========== 键盘导航 ==========
        void Nav(Topic t, string dir)
        {
            Topic nx = null;
            if (dir == "left") nx = FindParent(t);
            else if (dir == "right") nx = t.Children.FirstOrDefault();
            else
            {
                var p = FindParent(t);
                var arr = p != null ? p.Children : roots;
                int i = arr.IndexOf(t);
                int j = i + (dir == "up" ? -1 : 1);
                if (j >= 0 && j < arr.Count) nx = arr[j];
            }
            if (nx != null) { Select(nx); EnsureVisible(nx); }
        }

        void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            if (ctrl)
            {
                if (e.Key == Key.S) { e.Handled = true; if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) DoSaveAs(); else DoSave(); return; }
                if (e.Key == Key.O) { e.Handled = true; DoOpen(); return; }
                if (e.Key == Key.N) { e.Handled = true; DoNew(); return; }
                if (e.Key == Key.Z) { e.Handled = true; Undo(); return; }
                if (e.Key == Key.Y) { e.Handled = true; Redo(); return; }
            }
            if (editing != null) return;           // 编辑中交给 TextBox
            // 选中叠加层时，Delete 删除它
            if (selOverlay != null && (e.Key == Key.Delete || e.Key == Key.Back))
            {
                e.Handled = true; DeleteSelectedOverlay(); return;
            }
            if (e.Key == Key.Escape && focusTopic != null) { e.Handled = true; ExitFocus(); return; }
            if (selected == null) return;
            if (IsSpider)   // 蜘蛛网：无层级，回车=新建自由节点，Tab 不分子级
            {
                if (e.Key == Key.Enter) { e.Handled = true; AddSpiderNodeNear(selected); }
                else if (e.Key == Key.F2) { e.Handled = true; BeginEdit(selected); }
                else if (e.Key == Key.Delete) { e.Handled = true; DeleteTopic(selected); }
                return;
            }
            // 可自定义快捷键（默认 Tab/Enter/F2/Delete）
            if (e.Key == shortcuts["addChild"])    { e.Handled = true; AddChild(selected); return; }
            if (e.Key == shortcuts["addSibling"])  { e.Handled = true; AddSibling(selected); return; }
            if (e.Key == shortcuts["edit"])        { e.Handled = true; BeginEdit(selected); return; }
            if (e.Key == shortcuts["delete"])      { e.Handled = true; DeleteTopic(selected); return; }
            switch (e.Key)
            {
                case Key.Left:   e.Handled = true; Nav(selected, "left"); break;
                case Key.Right:  e.Handled = true; Nav(selected, "right"); break;
                case Key.Up:     e.Handled = true; Nav(selected, "up"); break;
                case Key.Down:   e.Handled = true; Nav(selected, "down"); break;
            }
        }

        // ========== 卡片拖动（仅根节点） ==========
        Point cardDownScreen; bool cardDragging; Topic cardDragTopic;
        void Card_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var t = (Topic)((Border)sender).Tag;
            if (editing != null && editing != t) CommitEdit();
            e.Handled = true;
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) { SelectToggle(t); return; }
            Select(t);
            if (e.ClickCount == 2) { BeginEdit(t); return; }
            cardDownScreen = e.GetPosition(viewport);
            cardDragTopic = roots.Contains(t) ? t : null;
            cardDragging = false;
            ((Border)sender).CaptureMouse();
        }
        void Card_MouseMove(object sender, MouseEventArgs e)
        {
            if (cardDragTopic == null || e.LeftButton != MouseButtonState.Pressed) return;
            var p = e.GetPosition(viewport);
            if (!cardDragging && Math.Abs(p.X - cardDownScreen.X) + Math.Abs(p.Y - cardDownScreen.Y) < 4) return;
            cardDragging = true;
            cardDragTopic.X += (p.X - cardDownScreen.X) / scale;
            cardDragTopic.Y += (p.Y - cardDownScreen.Y) / scale;
            cardDownScreen = p;
            Relayout();
        }
        void Card_MouseUp(object sender, MouseButtonEventArgs e)
        {
            ((Border)sender).ReleaseMouseCapture();
            cardDragTopic = null; cardDragging = false;
        }

        // ========== 画布平移 / 缩放 / 空白双击 ==========
        void Viewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.Handled) return;                 // 点在卡片上已处理
            if (editing != null) CommitEdit();
            if (e.ClickCount == 2) AddRootAt(ToWorld(e.GetPosition(viewport)));
            else { bool hadOverlay = selOverlay != null; Select(null); if (hadOverlay) Relayout(); Keyboard.Focus(this); }
        }
        void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            double old = scale;
            scale *= e.Delta > 0 ? 1.1 : 1 / 1.1;
            scale = Math.Max(0.2, Math.Min(3, scale));
            var p = e.GetPosition(viewport);
            offX = p.X - (p.X - offX) * (scale / old);
            offY = p.Y - (p.Y - offY) * (scale / old);
            ApplyTransform();
        }
        Point panStart; bool panning;
        void Viewport_MouseDownPan(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle)
            {
                panning = true; panStart = e.GetPosition(viewport); viewport.CaptureMouse();
            }
        }
        void Viewport_MouseMovePan(object sender, MouseEventArgs e)
        {
            if (!panning) return;
            var p = e.GetPosition(viewport);
            offX += p.X - panStart.X; offY += p.Y - panStart.Y; panStart = p;
            ApplyTransform();
        }
        void Viewport_MouseUpPan(object sender, MouseButtonEventArgs e)
        {
            if (panning) { panning = false; viewport.ReleaseMouseCapture(); }
        }

        void EnsureVisible(Topic t)
        {
            if (t == null) return;
            Relayout();
            double sx = t.LX * scale + offX, sy = (t.CY - t.H / 2) * scale + offY;
            double ex = (t.LX + t.W) * scale + offX, ey = (t.CY + t.H / 2) * scale + offY;
            double m = 70, W = viewport.ActualWidth, H = viewport.ActualHeight;
            if (sx < m) offX += m - sx; else if (ex > W - m) offX += (W - m) - ex;
            if (sy < m) offY += m - sy; else if (ey > H - m) offY += (H - m) - ey;
            ApplyTransform();
        }

        void Fit()
        {
            scale = 1;
            if (roots.Count == 0) { offX = offY = 60; ApplyTransform(); return; }
            Relayout();
            double minX = 1e9, minY = 1e9, maxX = -1e9, maxY = -1e9;
            foreach (var t in AllTopics())
            {
                minX = Math.Min(minX, t.LX); minY = Math.Min(minY, t.CY - t.H / 2);
                maxX = Math.Max(maxX, t.LX + t.W); maxY = Math.Max(maxY, t.CY + t.H / 2);
            }
            offX = viewport.ActualWidth / 2 - (minX + maxX) / 2;
            offY = viewport.ActualHeight / 2 - (minY + maxY) / 2;
            ApplyTransform();
        }

        // ========== 撤销 / 重做 ==========
        void PushUndo() { undoStack.Add(BmapIO.Save(doc)); redoStack.Clear(); dirty = true; RefreshLabels(); }
        void Undo()
        {
            if (undoStack.Count == 0) return;
            redoStack.Add(BmapIO.Save(doc));
            var s = undoStack[undoStack.Count - 1];
            undoStack.RemoveAt(undoStack.Count - 1);
            doc = BmapIO.Load(s);
            selected = editing = null;
            ApplyDocType();
            RefreshLabels();
            Rebuild();
        }
        void Redo()
        {
            if (redoStack.Count == 0) return;
            undoStack.Add(BmapIO.Save(doc));
            var s = redoStack[redoStack.Count - 1];
            redoStack.RemoveAt(redoStack.Count - 1);
            doc = BmapIO.Load(s);
            selected = editing = null;
            ApplyDocType();
            RefreshLabels();
            Rebuild();
        }

        // ========== 文件 ==========
        string curPath;       // 当前文件磁盘路径（保存过/打开过才有）
        bool dirty;           // 有未保存改动

        void MarkDirty() { dirty = true; RefreshLabels(); }

        void DoNew()
        {
            if (!ConfirmDiscard()) return;
            undoStack.Clear(); redoStack.Clear();
            doc = new Document { Roots = { new Topic { Text = "中心主题", X = 2400, Y = 1500 } } };
            curPath = null; dirty = false;
            readOnly = false; btnRead.Content = T("👁 阅读模式", "👁 Read");
            ApplyDocType();
            RefreshLabels(); HideStart();
            Rebuild();
            Select(roots[0]);
            Fit();
        }

        void DoOpen()
        {
            if (!ConfirmDiscard()) return;
            var dlg = new OpenFileDialog { Filter = "思维导图 (*.bmap)|*.bmap|所有文件 (*.*)|*.*" };
            if (dlg.ShowDialog() != true) return;
            readOnly = false; btnRead.Content = T("👁 阅读模式", "👁 Read");
            OpenPath(dlg.FileName);
        }

        // 保存：已有路径直接写，否则走另存为
        void DoSave()
        {
            if (editing != null) CommitEdit();
            if (string.IsNullOrEmpty(curPath)) { DoSaveAs(); return; }
            WriteTo(curPath);
        }

        void DoSaveAs()
        {
            if (editing != null) CommitEdit();
            var dlg = new SaveFileDialog { Filter = "思维导图 (*.bmap)|*.bmap", FileName = curName + ".bmap" };
            if (dlg.ShowDialog() != true) return;
            WriteTo(dlg.FileName);
        }

        void WriteTo(string path)
        {
            try
            {
                curName = IOPath.GetFileNameWithoutExtension(path);
                File.WriteAllText(path, BmapIO.Save(doc));
                curPath = path; dirty = false;
                RefreshLabels();
                AddRecent(path);
            }
            catch (Exception ex) { MessageBox.Show(T("保存失败：", "Save failed: ") + ex.Message); }
        }

        // 关闭/新建/打开前，若有未保存改动则询问
        bool ConfirmDiscard()
        {
            if (!dirty) return true;
            var r = MessageBox.Show(T("有未保存的改动，要先保存吗？", "Save changes first?"), "ThoughtCanvas",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (r == MessageBoxResult.Cancel) return false;
            if (r == MessageBoxResult.Yes) { DoSave(); return !dirty; }   // 取消保存对话框则中止
            return true;   // No=放弃
        }
    }
}
