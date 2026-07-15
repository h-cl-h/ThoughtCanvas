using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using IOPath = System.IO.Path;

namespace BmapTextStyleEditor;

public partial class MainWindow : Window
{
    readonly JsonSerializerOptions json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    readonly Dictionary<DependencyObject, string> originalUiText = new(); readonly List<string> history = []; readonly HashSet<int> selectedLayers = []; readonly Dictionary<int, Point> groupDragStarts = []; int historyIndex = -1; bool restoringHistory, syncingLayerSelection, hasSynced, hasStandaloneSave, suppressLiveControlSync = true; bool english;
    TextBoxStyle style = new(); string tool = "select"; int selected = -1; Point down, layerDragStart; double dragLayerStartX, dragLayerStartY, dragTextStartX, dragTextStartY, resizeX, resizeY, resizeW, resizeH; int resizeHandle; bool dragging, resizing, dragChanged, selectedText, marqueeSelecting; FrameworkElement? draft; Rectangle? marqueeElement; Window? floatingTools; string? connectedTarget; string? connectedRoot; string? currentFile; string? lastSyncedId; string? lastSyncedSnapshot;

    public MainWindow()
    {
        InitializeComponent(); FontBox.ItemsSource = Fonts.SystemFontFamilies.Select(f => f.Source).Distinct().OrderBy(x => x).ToList(); FontBox.SelectedItem = FontBox.Items.Cast<string>().FirstOrDefault(x => x.Equals("Microsoft YaHei", StringComparison.OrdinalIgnoreCase)) ?? FontBox.Items.Cast<string>().FirstOrDefault(); SizeModeBox.SelectedIndex = 0; InputTypeBox.SelectedIndex = 0;
        SelectTool.Click += (_, _) => SetTool("select"); RectTool.Click += (_, _) => SetTool("rect"); RoundTool.Click += (_, _) => SetTool("round"); EllipseTool.Click += (_, _) => SetTool("ellipse"); LineTool.Click += (_, _) => SetTool("line"); ImportImageBtn.Click += (_, _) => ImportImage(); TextTool.Click += (_, _) => SetTool("text");
        DesignCanvas.MouseLeftButtonDown += CanvasDown; DesignCanvas.MouseMove += CanvasMove; DesignCanvas.MouseLeftButtonUp += CanvasUp;
        LayerList.PreviewMouseLeftButtonDown += (_, e) => layerDragStart = e.GetPosition(LayerList); LayerList.PreviewMouseMove += LayerListMouseMove; LayerList.Drop += LayerListDrop; LayerList.PreviewMouseRightButtonDown += LayerListRightClick;
        LayerList.SelectionChanged += (_, _) => { if (syncingLayerSelection) return; selectedLayers.Clear(); foreach (StyleLayer item in LayerList.SelectedItems) { var i = style.Layers.IndexOf(item); if (i >= 0) selectedLayers.Add(i); } selected = LayerList.SelectedIndex; if (selected >= 0) selectedText = false; LoadLayerFields(); RebuildCanvas(); };
        DeleteBtn.Click += (_, _) => DeleteSelected(); UpBtn.Click += (_, _) => MoveLayer(1); DownBtn.Click += (_, _) => MoveLayer(-1); TopBtn.Click += (_, _) => MoveLayerExtreme(true); BottomBtn.Click += (_, _) => MoveLayerExtreme(false); UndoBtn.Click += (_, _) => Undo(); RedoBtn.Click += (_, _) => Redo(); ApplyBtn.Click += (_, _) => { ReadControls(); ApplyLayerFields(); RebuildCanvas(); RefreshPreview(); CommitHistory(); Status(T("属性已应用", "Properties applied")); };
        NewBtn.Click += (_, _) => NewStyle(); OpenBtn.Click += (_, _) => OpenStyle(); SaveBtn.Click += (_, _) => SaveStandalone(); ConnectBtn.Click += (_, _) => Connect(); SyncBtn.Click += (_, _) => Sync(hasSynced); LaunchBtn.Click += (_, _) => Launch(); FloatToolsBtn.Click += (_, _) => ShowFloatingTools();
        HomeBtn.Click += (_, _) => ShowStart(); StartNewBtn.Click += (_, _) => { NewStyle(); ShowEditor(); }; StartOpenBtn.Click += (_, _) => { OpenStyle(); if (currentFile != null) ShowEditor(); }; StartConnectBtn.Click += (_, _) => { Connect(); ShowEditor(); }; LanguageBtn.Click += (_, _) => ApplyLanguage(!english); StartLanguageBtn.Click += (_, _) => ApplyLanguage(!english);
        WorkTabs.SelectionChanged += (_, _) => { if (WorkTabs.SelectedIndex == 1) { ReadControls(); RefreshPreview(); UpdateSyncState(); } }; SizeModeBox.SelectionChanged += LivePreviewStyleChanged;
        InputTypeBox.SelectionChanged += LivePreviewRulesChanged; MaxLengthBox.TextChanged += LivePreviewRulesChanged; PatternBox.TextChanged += LivePreviewRulesChanged; RequiredBox.Checked += LivePreviewRulesChanged; RequiredBox.Unchecked += LivePreviewRulesChanged;
        foreach (var box in new[] { NameBox, IdBox, BgBox, BorderBox, ColorBox, RadiusBox, FontSizeBox, AspectBox }) box.TextChanged += LiveStyleControlChanged;
        FontBox.SelectionChanged += LiveStyleControlChanged;
        foreach (var box in new[] { LayerFillBox, LayerStrokeBox, XBox, YBox, WBox, HBox }) box.TextChanged += LiveLayerControlChanged;
        PreviewText.TextChanged += (_, _) => RefreshPreview(); PreviewText.PreviewTextInput += ValidatePreviewTextInput; DataObject.AddPastingHandler(PreviewText, PreviewPaste); KeyDown += OnKey;
        NewStyle(); suppressLiveControlSync = false; ReadInputRuleControls(); RestoreConnection();
        CaptureUiText(this); ShowStart();
    }

    static double Num(string? value, double fallback = 0) => double.TryParse(value, out var n) && double.IsFinite(n) ? n : fallback;
    static double Ratio(string? value, double fallback = 0) { var s = (value ?? "").Trim(); var parts = s.Split('/'); if (parts.Length == 2) { var a = Num(parts[0]); var b = Num(parts[1]); var r = b > 0 ? a / b : fallback; return a > 0 && double.IsFinite(r) ? r : fallback; } var n = Num(s, fallback); return n > 0 && double.IsFinite(n) ? n : fallback; }
    static Brush BrushOf(string? value, string fallback = "#000000") { try { return (Brush)new BrushConverter().ConvertFromString(string.IsNullOrWhiteSpace(value) ? fallback : value)!; } catch { return (Brush)new BrushConverter().ConvertFromString(fallback)!; } }
    string T(string zh, string en) => english ? en : zh;
    void ShowStart() { StartPage.Visibility = Visibility.Visible; EditorRoot.Visibility = Visibility.Collapsed; }
    void ShowEditor() { StartPage.Visibility = Visibility.Collapsed; EditorRoot.Visibility = Visibility.Visible; }
    void CaptureUiText(DependencyObject root) { if (root is TextBlock tb && tb != ConnectionText && tb != StatusText) originalUiText.TryAdd(tb, tb.Text); else if (root is HeaderedContentControl hc && hc.Header is string hs) originalUiText.TryAdd(hc, hs); else if (root is ContentControl cc && cc.Content is string cs) originalUiText.TryAdd(cc, cs); foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) CaptureUiText(child); }
    static readonly Dictionary<string, string> En = new() {
        ["开始页"]="Home",["新建"]="New",["打开样式"]="Open style",["保存独立文件"]="Save file",["撤销"]="Undo",["重做"]="Redo",["连接主程序…"]="Connect…",["保存并同步"]="Save & sync",["打开主程序"]="Launch app",["浮动工具"]="Floating tools",
        ["绘制工具"]="Tools",["选择 / 移动"]="Select / Move",["矩形"]="Rectangle",["圆角矩形"]="Rounded rectangle",["椭圆"]="Ellipse",["直线"]="Line",["导入图片…"]="Import image…",["文本区域（重画会替换）"]="Text region (redraw to replace)",
        ["比例吸附：1:1 / 4:3 / 3:2 / 16:9"]="Ratio snap: 1:1 / 4:3 / 3:2 / 16:9",["吸附其他图层的边缘和中心"]="Snap to layer edges and centers",["网格吸附"]="Snap to grid",["网格大小（像素）"]="Grid size (pixels)",["绘制长宽比（0=自由，例如 1、1.5、16/9）"]="Draw aspect ratio (0=free; e.g. 1, 1.5, 16/9)",
        ["在中央设计框内拖动绘制。使用选择工具移动图形；右侧图层面板可拖动排序。"]="Drag on the canvas to draw. Use Select to move shapes; drag layers on the right to reorder.",
        ["设计画布"]="Design",["独立预览"]="Preview",["图层"]="Layers",["上移"]="Move up",["下移"]="Move down",["置顶"]="To top",["置底"]="To bottom",["删除"]="Delete",
        ["样式属性"]="Style properties",["样式名称"]="Style name",["样式 ID（英文/数字/短横线）"]="Style ID (letters, numbers, hyphens)",["底色"]="Background",["边框色"]="Border",["文字色"]="Text color",["圆角"]="Corner radius",["字体（读取 Windows 已安装字体）"]="Font (installed Windows fonts)",["字号"]="Font size",["尺寸策略"]="Sizing",["固定长宽比"]="Fixed aspect ratio",["自由拉伸"]="Free stretch",["文本框长宽比（可手动输入，如 1、1.5、16/9）"]="Text-box aspect ratio (e.g. 1, 1.5, 16/9)",["写字区域目标宽度（像素）"]="Text-region target width (px)",["写字区域目标高度（像素）"]="Text-region target height (px)",
        ["输入限制"]="Input rules",["最多字符（0=不限）"]="Maximum characters (0=unlimited)",["允许类型"]="Allowed input",["任意"]="Any",["仅数字"]="Numbers only",["仅字母"]="Letters only",["仅中文"]="Chinese only",["数字和字母"]="Letters and numbers",["自定义正则"]="Custom regex",["不允许空值"]="Required",
        ["选中图层"]="Selected layer",["填充（图形默认实心）"]="Fill (solid by default)",["描边（始终实线）"]="Stroke (always solid)",["位置和尺寸（相对画布百分比）"]="Position and size (% of canvas)",["宽度"]="Width",["高度"]="Height",["应用属性并刷新预览"]="Apply and refresh",
        ["BMAP"]="BMAP",["文本框样式编辑器"]="Text Box Style Editor",["设计、预览并同步 ThoughtCanvas 文本框样式"]="Design, preview, and sync ThoughtCanvas text-box styles",["开始"]="Start",["新建设计"]="New design",["连接主程序"]="Connect app",["可以不连接主程序独立设计和预览；连接后可直接同步样式。"]="Design and preview independently, or connect to ThoughtCanvas for direct sync.",
        ["未连接主程序（可独立使用）"]="Not connected (standalone mode)",["在这里输入文字，测试样式和拉伸效果"]="Type here to test the style and resizing"
    };
    void ApplyLanguage(bool useEnglish) { english = useEnglish; UiState.English = english; foreach (var pair in originalUiText) { var value = english && En.TryGetValue(pair.Value, out var translated) ? translated : pair.Value; if (pair.Key is TextBlock tb) tb.Text = value; else if (pair.Key is HeaderedContentControl hc) hc.Header = value; else if (pair.Key is ContentControl cc) cc.Content = value; } Title = english ? "BMAP Text Box Style Editor V0.0.1" : "BMAP 文本框样式编辑器 V0.0.1"; LanguageBtn.Content = StartLanguageBtn.Content = english ? "中文" : "EN"; if (connectedTarget == null) ConnectionText.Text = T("未连接主程序（可独立使用）", "Not connected (standalone mode)"); else SetConnected(connectedTarget); LayerList.Items.Refresh(); UpdateSaveButton(); UpdateSyncState(); Status(T("就绪", "Ready")); }
    void Status(string text) => StatusText.Text = text;
    static string ConnectionConfigPath
    {
        get
        {
            var testOrPortableData = Environment.GetEnvironmentVariable("BMAP_TEXT_STYLE_EDITOR_DATA_DIR");
            var directory = string.IsNullOrWhiteSpace(testOrPortableData)
                ? IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BMAPTextStyleEditor")
                : testOrPortableData;
            return IOPath.Combine(directory, "connection.json");
        }
    }
    void SetConnected(string target) { connectedTarget = target; connectedRoot = IOPath.GetDirectoryName(target); ConnectBtn.Visibility = Visibility.Collapsed; StartConnectBtn.Visibility = Visibility.Collapsed; ConnectionText.Foreground = BrushOf("#238B57"); ConnectionText.FontWeight = FontWeights.SemiBold; ConnectionText.Text = T("● 已连接：", "● Connected: ") + target; }
    void RestoreConnection() { try { if (!File.Exists(ConnectionConfigPath)) return; var target = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(ConnectionConfigPath), json)?.GetValueOrDefault("target"); if (!string.IsNullOrWhiteSpace(target) && File.Exists(target)) SetConnected(target); } catch { } }
    void SaveConnection() { if (connectedTarget == null) return; Directory.CreateDirectory(IOPath.GetDirectoryName(ConnectionConfigPath)!); File.WriteAllText(ConnectionConfigPath, JsonSerializer.Serialize(new Dictionary<string, string> { ["target"] = connectedTarget }, json), new System.Text.UTF8Encoding(false)); }
    string Snapshot() { ModelSafety.Normalize(style); return JsonSerializer.Serialize(style, json); }
    void UpdateSaveButton()
    {
        SaveBtn.Content = hasStandaloneSave ? T("✓ 已保存", "✓ Saved") : T("保存独立文件", "Save file");
    }
    void UpdateSyncState(string? currentSnapshot = null)
    {
        if (SyncBtn == null) return;
        currentSnapshot ??= Snapshot();
        hasSynced = lastSyncedSnapshot != null && string.Equals(currentSnapshot, lastSyncedSnapshot, StringComparison.Ordinal);
        SyncBtn.Content = hasSynced ? T("✓ 已同步 · 点击同步其他版本", "✓ Synced · click for another version") : T("保存并同步", "Save & sync");
        SyncBtn.Background = BrushOf(hasSynced ? "#238B57" : "#5B8DEF");
        SyncBtn.ToolTip = hasSynced
            ? T("点击后可重新选择另一个 ThoughtCanvas 版本并同步", "Click to select and sync another ThoughtCanvas version")
            : T("保存当前样式并同步到已连接的 ThoughtCanvas", "Save the current style to the connected ThoughtCanvas");
    }
    void ResetHistory() { history.Clear(); history.Add(Snapshot()); historyIndex = 0; UpdateHistoryButtons(); }
    void CommitHistory() { if (restoringHistory) return; string state; try { state = Snapshot(); } catch (Exception ex) { App.LogException(ex); Status(T("本次操作未写入撤销历史", "The operation was not added to undo history")); return; } UpdateSyncState(state); if (historyIndex >= 0 && history[historyIndex] == state) return; if (historyIndex + 1 < history.Count) history.RemoveRange(historyIndex + 1, history.Count - historyIndex - 1); history.Add(state); historyIndex = history.Count - 1; if (history.Count > 100) { history.RemoveAt(0); historyIndex--; } UpdateHistoryButtons(); }
    void UpdateHistoryButtons() { if (UndoBtn == null) return; UndoBtn.IsEnabled = historyIndex > 0; RedoBtn.IsEnabled = historyIndex >= 0 && historyIndex < history.Count - 1; }
    void RestoreHistory(int index) { if (index < 0 || index >= history.Count) return; restoringHistory = true; try { style = JsonSerializer.Deserialize<TextBoxStyle>(history[index], json) ?? new(); historyIndex = index; selected = -1; selectedLayers.Clear(); selectedText = false; LoadControls(); UpdateHistoryButtons(); } finally { restoringHistory = false; } UpdateSyncState(); }
    void Undo() { if (historyIndex <= 0) return; RestoreHistory(historyIndex - 1); Status(T("已撤销", "Undone")); }
    void Redo() { if (historyIndex < 0 || historyIndex >= history.Count - 1) return; RestoreHistory(historyIndex + 1); Status(T("已重做", "Redone")); }
    void LayerVisibilityChanged(object sender, RoutedEventArgs e) { CommitHistory(); RebuildCanvas(); RefreshPreview(); }
    void LayerLockChanged(object sender, RoutedEventArgs e) { CommitHistory(); RebuildCanvas(); }
    void SetTool(string value) { tool = value; var label = value == "select" ? T("选择/移动", "Select/Move") : value == "rect" ? T("矩形", "Rectangle") : value == "round" ? T("圆角矩形", "Rounded rectangle") : value == "ellipse" ? T("椭圆", "Ellipse") : value == "line" ? T("直线", "Line") : T("文本区域", "Text region"); Status(T("当前工具：", "Tool: ") + label); }
    static T? Ancestor<T>(DependencyObject? x) where T : DependencyObject { while (x != null) { if (x is T t) return t; x = VisualTreeHelper.GetParent(x); } return null; }
    void LayerListMouseMove(object sender, MouseEventArgs e) { if (e.LeftButton != MouseButtonState.Pressed || LayerList.SelectedItem is not StyleLayer layer) return; var p = e.GetPosition(LayerList); if (Math.Abs(p.X - layerDragStart.X) < 5 && Math.Abs(p.Y - layerDragStart.Y) < 5) return; DragDrop.DoDragDrop(LayerList, new DataObject(typeof(StyleLayer), layer), DragDropEffects.Move); }
    void LayerListDrop(object sender, DragEventArgs e) { if (!e.Data.GetDataPresent(typeof(StyleLayer))) return; var source = (StyleLayer)e.Data.GetData(typeof(StyleLayer)); var item = Ancestor<ListBoxItem>(e.OriginalSource as DependencyObject); var target = item?.DataContext as StyleLayer; int old = style.Layers.IndexOf(source), at = target == null ? style.Layers.Count - 1 : style.Layers.IndexOf(target); if (old < 0 || at < 0 || old == at) return; style.Layers.RemoveAt(old); if (old < at) at--; style.Layers.Insert(Math.Clamp(at, 0, style.Layers.Count), source); RefreshLayerList(style.Layers.IndexOf(source)); CommitHistory(); }
    void LayerListRightClick(object sender, MouseButtonEventArgs e) { var item = Ancestor<ListBoxItem>(e.OriginalSource as DependencyObject); if (item == null) return; selected = LayerList.ItemContainerGenerator.IndexFromContainer(item); LayerList.SelectedIndex = selected; item.ContextMenu = LayerContextMenu(selected); }
    void ShowFloatingTools() { if (floatingTools != null) { floatingTools.Activate(); return; } var panel = new WrapPanel { Margin = new Thickness(8) }; foreach (var x in new[] { (T("选择", "Select"), "select"), (T("矩形", "Rectangle"), "rect"), (T("圆角", "Rounded"), "round"), (T("椭圆", "Ellipse"), "ellipse"), (T("直线", "Line"), "line"), (T("文本区域", "Text region"), "text") }) { var b = new Button { Content = x.Item1, Margin = new Thickness(3), MinWidth = 72 }; var id = x.Item2; b.Click += (_, _) => SetTool(id); panel.Children.Add(b); } var imageButton = new Button { Content = T("导入图片…", "Import image…"), Margin = new Thickness(3), MinWidth = 92 }; imageButton.Click += (_, _) => ImportImage(); panel.Children.Add(imageButton); floatingTools = new Window { Title = T("工具", "Tools"), Owner = this, ShowInTaskbar = false, WindowStyle = WindowStyle.ToolWindow, ResizeMode = ResizeMode.NoResize, SizeToContent = SizeToContent.WidthAndHeight, Content = panel, Topmost = true }; floatingTools.Closed += (_, _) => floatingTools = null; floatingTools.Show(); }

    void ImportImage()
    {
        var dialog = new OpenFileDialog { Title = T("导入图片", "Import image"), Filter = T("图片|*.png;*.jpg;*.jpeg;*.gif;*.bmp", "Images|*.png;*.jpg;*.jpeg;*.gif;*.bmp") };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var imported = ImageImportPipeline.Encode(dialog.FileName);
            var imageAspect = imported.PixelWidth / Math.Max(1d, imported.PixelHeight);
            const double canvasAspect = 900d / 520d;
            double width, height;
            if (imageAspect >= canvasAspect) { width = 100; height = Math.Min(100, 100 * canvasAspect / imageAspect); }
            else { height = 100; width = Math.Min(100, 100 * imageAspect / canvasAspect); }
            var layer = new StyleLayer
            {
                Type = "image", Name = IOPath.GetFileNameWithoutExtension(dialog.FileName),
                X = (100 - width) / 2, Y = (100 - height) / 2, W = width, H = height,
                Fill = "transparent", Stroke = "transparent", StrokeWidth = 0,
                ImageData = imported.DataUrl
            };
            style.Layers.Add(layer); selected = style.Layers.Count - 1; selectedLayers.Clear(); selectedLayers.Add(selected);
            RefreshLayerList(selected); CommitHistory(); SetTool("select");
            Status(T("图片已导入", "Image imported"));
        }
        catch (Exception ex) { MessageBox.Show(T("无法导入图片：", "Could not import image: ") + ex.Message); }
    }
    Point SnapPoint(Point p, int ignoreIndex, out bool snapped)
    {
        snapped = false; double x = p.X, y = p.Y;
        if (GridSnapBox.IsChecked == true) { var g = Math.Clamp(Num(GridSizeBox.Text, 10), 2, 100); x = Math.Round(x / g) * g; y = Math.Round(y / g) * g; snapped = true; }
        if (ObjectSnapBox.IsChecked == true) { double bestX = 8.1, bestY = 8.1; for (int i = 0; i < style.Layers.Count; i++) { if (i == ignoreIndex) continue; var l = style.Layers[i]; foreach (var tx in new[] { l.X * 9, (l.X + l.W / 2) * 9, (l.X + l.W) * 9 }) { var d = tx - p.X; if (Math.Abs(d) < Math.Abs(bestX)) bestX = d; } foreach (var ty in new[] { l.Y * 5.2, (l.Y + l.H / 2) * 5.2, (l.Y + l.H) * 5.2 }) { var d = ty - p.Y; if (Math.Abs(d) < Math.Abs(bestY)) bestY = d; } } if (Math.Abs(bestX) <= 8) { x = p.X + bestX; snapped = true; } if (Math.Abs(bestY) <= 8) { y = p.Y + bestY; snapped = true; } }
        return new Point(Math.Clamp(x, 0, 900), Math.Clamp(y, 0, 520));
    }
    void SnapLayer(StyleLayer layer, int index, ref double x, ref double y, out bool snapped)
    {
        snapped = false; if (GridSnapBox.IsChecked == true) { var g = Math.Clamp(Num(GridSizeBox.Text, 10), 2, 100); x = Math.Round(x * 9 / g) * g / 9; y = Math.Round(y * 5.2 / g) * g / 5.2; snapped = true; }
        if (ObjectSnapBox.IsChecked != true) return; double dxBest = 8.1, dyBest = 8.1; var ownX = new[] { x * 9, (x + layer.W / 2) * 9, (x + layer.W) * 9 }; var ownY = new[] { y * 5.2, (y + layer.H / 2) * 5.2, (y + layer.H) * 5.2 };
        for (int i = 0; i < style.Layers.Count; i++) { if (i == index || (groupDragStarts.Count > 1 && selectedLayers.Contains(i))) continue; var other = style.Layers[i]; foreach (var a in ownX) foreach (var b in new[] { other.X * 9, (other.X + other.W / 2) * 9, (other.X + other.W) * 9 }) { var d = b - a; if (Math.Abs(d) < Math.Abs(dxBest)) dxBest = d; } foreach (var a in ownY) foreach (var b in new[] { other.Y * 5.2, (other.Y + other.H / 2) * 5.2, (other.Y + other.H) * 5.2 }) { var d = b - a; if (Math.Abs(d) < Math.Abs(dyBest)) dyBest = d; } }
        if (Math.Abs(dxBest) <= 8) { x += dxBest / 9; snapped = true; } if (Math.Abs(dyBest) <= 8) { y += dyBest / 5.2; snapped = true; }
    }

    void NewStyle()
    {
        style = new(); selected = -1; selectedLayers.Clear(); selectedText = false; currentFile = null; lastSyncedId = null; lastSyncedSnapshot = null; hasStandaloneSave = false; NameBox.Text = T("我的文本框样式", "My text-box style"); IdBox.Text = "my-text-style"; BgBox.Text = "#FFFFFF"; BorderBox.Text = "#5B8DEF"; ColorBox.Text = "#2C3140"; RadiusBox.Text = "12"; FontSizeBox.Text = "14"; SizeModeBox.SelectedIndex = 0; AspectBox.Text = "1.8"; MaxLengthBox.Text = "0"; PatternBox.Text = ""; RequiredBox.IsChecked = false; LayerList.ItemsSource = style.Layers; RebuildCanvas(); RefreshPreview(); ResetHistory(); UpdateSaveButton(); UpdateSyncState(); Status(T("已新建空白样式", "New blank style"));
    }

    void SyncLayerSelection()
    {
        syncingLayerSelection = true;
        try
        {
            LayerList.SelectedItems.Clear();
            foreach (var i in selectedLayers.Where(i => i >= 0 && i < style.Layers.Count).OrderBy(i => i)) LayerList.SelectedItems.Add(style.Layers[i]);
            if (selected >= 0 && selected < style.Layers.Count) LayerList.ScrollIntoView(style.Layers[selected]);
        }
        finally { syncingLayerSelection = false; }
    }

    void CanvasDown(object sender, MouseButtonEventArgs e)
    {
        var raw = e.GetPosition(DesignCanvas); dragging = true; dragChanged = false; DesignCanvas.CaptureMouse();
        if (tool == "select")
        {
            down = raw; var hit = HitLayer(raw); var hitText = hit < 0 && HitTextRegion(raw); var keepSelectedText = selectedText; marqueeSelecting = false; groupDragStarts.Clear();
            if (hit >= 0)
            {
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { if (!selectedLayers.Add(hit)) selectedLayers.Remove(hit); }
                else if (!selectedLayers.Contains(hit)) { selectedLayers.Clear(); selectedLayers.Add(hit); keepSelectedText = false; }
                if (!selectedLayers.Contains(hit)) { selected = selectedLayers.LastOrDefault(-1); dragging = false; DesignCanvas.ReleaseMouseCapture(); SyncLayerSelection(); RebuildCanvas(); return; }
                selected = hit; selectedText = keepSelectedText;
                if (selectedLayers.Any(i => style.Layers[i].IsLocked)) { dragging = false; DesignCanvas.ReleaseMouseCapture(); SyncLayerSelection(); RebuildCanvas(); Status(T("所选图层包含锁定项", "The selection contains a locked layer")); return; }
                foreach (var i in selectedLayers) groupDragStarts[i] = new Point(style.Layers[i].X, style.Layers[i].Y); if (selectedText) { dragTextStartX = style.TextRegion.X; dragTextStartY = style.TextRegion.Y; }
                dragLayerStartX = style.Layers[hit].X; dragLayerStartY = style.Layers[hit].Y; SyncLayerSelection(); RebuildCanvas();
            }
            else if (hitText)
            {
                selected = -1; selectedLayers.Clear(); selectedText = true; SyncLayerSelection(); dragTextStartX = style.TextRegion.X; dragTextStartY = style.TextRegion.Y; Status(T("已选择文字输入区域", "Text input region selected")); RebuildCanvas();
            }
            else
            {
                selected = -1; selectedLayers.Clear(); SyncLayerSelection(); marqueeSelecting = true;
                marqueeElement = new Rectangle { Fill = BrushOf("#1A347FFF"), Stroke = BrushOf("#347FFF"), StrokeThickness = 1.5, IsHitTestVisible = false };
                Canvas.SetLeft(marqueeElement, raw.X); Canvas.SetTop(marqueeElement, raw.Y); DesignCanvas.Children.Add(marqueeElement);
            }
            return;
        }
        down = SnapPoint(raw, -1, out _);
        draft = tool == "ellipse" ? new Ellipse() : new Rectangle();
        if (tool == "line") draft = new Line { X1 = 0, Y1 = 0 };
        if (draft is Shape s) { s.Fill = tool == "line" || tool == "text" ? Brushes.Transparent : BrushOf("#DDE9FF"); s.Stroke = BrushOf(tool == "text" ? "#E64D5F" : "#5B8DEF"); s.StrokeThickness = tool == "text" ? 2 : 1.5; if (tool == "text") s.StrokeDashArray = new DoubleCollection { 6, 4 }; }
        Canvas.SetLeft(draft, down.X); Canvas.SetTop(draft, down.Y); DesignCanvas.Children.Add(draft);
    }

    void CanvasMove(object sender, MouseEventArgs e)
    {
        var raw = e.GetPosition(DesignCanvas); if (resizing) { ResizeSelected(raw); return; } if (!dragging) return;
        if (tool == "select" && marqueeSelecting && marqueeElement != null) { var marqueeX = Math.Min(down.X, raw.X); var marqueeY = Math.Min(down.Y, raw.Y); Canvas.SetLeft(marqueeElement, marqueeX); Canvas.SetTop(marqueeElement, marqueeY); marqueeElement.Width = Math.Abs(raw.X - down.X); marqueeElement.Height = Math.Abs(raw.Y - down.Y); return; }
        if (tool == "select" && selected >= 0 && selected < style.Layers.Count && groupDragStarts.Count > 0)
        {
            var primary = style.Layers[selected]; double nx = dragLayerStartX + (raw.X - down.X) / 9.0, ny = dragLayerStartY + (raw.Y - down.Y) / 5.2; SnapLayer(primary, selected, ref nx, ref ny, out var layerSnapped); double groupDx = nx - dragLayerStartX, groupDy = ny - dragLayerStartY;
            double minDx = groupDragStarts.Max(p => -p.Value.X), maxDx = groupDragStarts.Min(p => 100 - style.Layers[p.Key].W - p.Value.X), minDy = groupDragStarts.Max(p => -p.Value.Y), maxDy = groupDragStarts.Min(p => 100 - style.Layers[p.Key].H - p.Value.Y); if (selectedText) { minDx = Math.Max(minDx, -dragTextStartX); maxDx = Math.Min(maxDx, 100 - style.TextRegion.W - dragTextStartX); minDy = Math.Max(minDy, -dragTextStartY); maxDy = Math.Min(maxDy, 100 - style.TextRegion.H - dragTextStartY); } groupDx = Math.Clamp(groupDx, minDx, maxDx); groupDy = Math.Clamp(groupDy, minDy, maxDy);
            foreach (var pair in groupDragStarts) { style.Layers[pair.Key].X = pair.Value.X + groupDx; style.Layers[pair.Key].Y = pair.Value.Y + groupDy; } if (selectedText) { style.TextRegion.X = dragTextStartX + groupDx; style.TextRegion.Y = dragTextStartY + groupDy; }
            dragChanged = Math.Abs(groupDx) > .001 || Math.Abs(groupDy) > .001; RebuildCanvas(); LoadLayerFields(); if (layerSnapped) Status(T("已吸附", "Snapped")); return;
        }
        if (tool == "select" && selectedText) { var tr = style.TextRegion; tr.X = Math.Clamp(dragTextStartX + (raw.X - down.X) / 9, 0, 100 - tr.W); tr.Y = Math.Clamp(dragTextStartY + (raw.Y - down.Y) / 5.2, 0, 100 - tr.H); dragChanged = true; RebuildCanvas(); return; }
        var p = SnapPoint(raw, selected, out var pointSnapped);
        if (draft == null) return; double dx = p.X - down.X, dy = p.Y - down.Y, w = Math.Abs(dx), h = Math.Abs(dy);
        if (tool != "text" && tool != "line" && w > 0 && h > 0) { double ratio = Ratio(DrawRatioBox.Text), rawRatio = w / h; if (ratio <= 0 && SquareSnapBox.IsChecked == true) { double[] presets = [1, 4.0 / 3, 3.0 / 2, 16.0 / 9, 2]; var nearest = presets.OrderBy(v => Math.Abs(rawRatio - v)).First(); if (Math.Abs(rawRatio - nearest) / nearest <= (nearest == 1 ? .14 : .075)) ratio = nearest; } if (ratio > 0) { if (rawRatio > ratio) h = w / ratio; else w = h * ratio; pointSnapped = true; } }
        double x = dx < 0 ? down.X - w : down.X, y = dy < 0 ? down.Y - h : down.Y; Canvas.SetLeft(draft, x); Canvas.SetTop(draft, y); draft.Width = Math.Max(1, w); draft.Height = Math.Max(1, h);
        if (draft is Line line) { line.X2 = Math.Max(1, w); line.Y2 = Math.Max(1, h); }
        if (pointSnapped) Status(T("吸附生效：网格 / 图层边缘中心 / 比例", "Snap: grid / layer edges and centers / ratio"));
    }

    void CanvasUp(object sender, MouseButtonEventArgs e)
    {
        if (resizing) { resizing = false; DesignCanvas.ReleaseMouseCapture(); RebuildCanvas(); LoadLayerFields(); CommitHistory(); return; }
        if (!dragging) return; dragging = false; DesignCanvas.ReleaseMouseCapture();
        if (tool == "select")
        {
            if (marqueeSelecting)
            {
                var raw = e.GetPosition(DesignCanvas); var selection = new Rect(Math.Min(down.X, raw.X), Math.Min(down.Y, raw.Y), Math.Abs(raw.X - down.X), Math.Abs(raw.Y - down.Y)); selectedLayers.Clear();
                if (selection.Width >= 3 && selection.Height >= 3) for (int i = 0; i < style.Layers.Count; i++) { var l = style.Layers[i]; if (l.IsVisible && selection.IntersectsWith(new Rect(l.X * 9, l.Y * 5.2, l.W * 9, l.H * 5.2))) selectedLayers.Add(i); }
                var tr = style.TextRegion; selectedText = selection.Width >= 3 && selection.Height >= 3 && tr.W > 0 && tr.H > 0 && selection.IntersectsWith(new Rect(tr.X * 9, tr.Y * 5.2, tr.W * 9, tr.H * 5.2)); selected = selectedLayers.Count == 0 ? -1 : selectedLayers.Max(); marqueeSelecting = false; marqueeElement = null; SyncLayerSelection(); RebuildCanvas(); LoadLayerFields(); var count = selectedLayers.Count + (selectedText ? 1 : 0); Status(T($"已选择 {count} 个对象", $"{count} objects selected")); return;
            }
            if (dragChanged) CommitHistory(); return;
        }
        if (draft == null) return;
        double x = Canvas.GetLeft(draft), y = Canvas.GetTop(draft), w = draft.Width, h = draft.Height; DesignCanvas.Children.Remove(draft); draft = null;
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(w) || !double.IsFinite(h) || w < 4 || h < 4) return;
        if (tool == "text") { style.TextRegion = new TextRegion { X = x / 9, Y = y / 5.2, W = w / 9, H = h / 5.2 }; Status(T("文字区域已创建", "Text region created")); }
        else { style.Layers.Add(new StyleLayer { Type = tool == "round" ? "rect" : tool, X = x / 9, Y = y / 5.2, W = w / 9, H = h / 5.2, Fill = tool == "line" ? "transparent" : "#DDE9FF", Stroke = "#5B8DEF", StrokeWidth = 1.5, Radius = tool == "round" ? 14 : 0 }); selected = style.Layers.Count - 1; selectedLayers.Clear(); selectedLayers.Add(selected); }
        LayerList.Items.Refresh(); SyncLayerSelection(); RebuildCanvas(); RefreshPreview(); CommitHistory(); SetTool("select");
    }

    int HitLayer(Point p)
    {
        for (int i = style.Layers.Count - 1; i >= 0; i--) { var l = style.Layers[i]; if (!l.IsVisible) continue; if (p.X >= l.X * 9 && p.X <= (l.X + l.W) * 9 && p.Y >= l.Y * 5.2 && p.Y <= (l.Y + l.H) * 5.2) return i; } return -1;
    }
    bool HitTextRegion(Point p) { var t = style.TextRegion; return t.W > 0 && t.H > 0 && p.X >= t.X * 9 && p.X <= (t.X + t.W) * 9 && p.Y >= t.Y * 5.2 && p.Y <= (t.Y + t.H) * 5.2; }

    Geometry ClipGeometry(StyleLayer source, StyleLayer current, double sx, double sy)
    {
        var x = (source.X - current.X) * sx; var y = (source.Y - current.Y) * sy; var w = Math.Max(1, source.W * sx); var h = Math.Max(1, source.H * sy);
        Geometry g = source.Type == "ellipse" ? new EllipseGeometry(new Rect(x, y, w, h)) : new RectangleGeometry(new Rect(x, y, w, h), source.Radius, source.Radius);
        if (Math.Abs(source.Rotation) > .01) g.Transform = new RotateTransform(source.Rotation, x + w / 2, y + h / 2); return g;
    }
    static ImageSource? ImageSourceFromData(string? value)
    {
        if (!ModelSafety.IsSupportedImageData(value)) return null;
        try
        {
            var comma = value!.IndexOf(',');
            using var stream = new MemoryStream(Convert.FromBase64String(value[(comma + 1)..]));
            var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); return bitmap;
        }
        catch { return null; }
    }
    ContextMenu LayerContextMenu(int index)
    {
        var menu = new ContextMenu(); var info = new MenuItem { Header = index > 0 ? T("作用于下方相邻图层", "Uses the adjacent layer below") : T("最底层没有下方图层", "Bottom layer has no layer below"), IsEnabled = false }; menu.Items.Add(info); menu.Items.Add(new Separator()); var cut = new MenuItem { Header = T("剪切", "Clip") }; var mask = new MenuItem { Header = T("蒙版", "Mask") }; menu.Items.Add(cut); menu.Items.Add(mask); menu.Items.Add(new Separator());
        MenuItem Add(MenuItem parent, string title, Action action, bool check, bool enabled = true) { var item = new MenuItem { Header = title, IsCheckable = true, IsChecked = check, IsEnabled = enabled }; item.Click += (_, _) => action(); parent.Items.Add(item); return item; }
        var l = style.Layers[index];
        Add(cut, T("不剪切", "No clip"), () => SetLayerEffect(index, "none", "none"), l.ClipMode == "none");
        Add(cut, T("将本图形作为切刀：保留下方图层的相交部分", "Use this shape as cutter: keep intersection on layer below"), () => SetLayerEffect(index, "intersect", "none"), l.ClipMode == "intersect", index > 0);
        Add(cut, T("将本图形作为切刀：从下方图层挖掉本图形", "Use this shape as cutter: subtract it from layer below"), () => SetLayerEffect(index, "subtract", "none"), l.ClipMode == "subtract", index > 0);
        Add(mask, T("没有蒙版", "No mask"), () => SetLayerEffect(index, "none", "none"), l.MaskMode == "none");
        Add(mask, T("下方图层 Alpha 蒙版", "Alpha mask from layer below"), () => SetLayerEffect(index, "none", "alpha"), l.MaskMode == "alpha", index > 0);
        Add(mask, T("反向 Alpha 蒙版", "Inverse alpha mask"), () => SetLayerEffect(index, "none", "inverseAlpha"), l.MaskMode == "inverseAlpha", index > 0);
        Add(mask, T("下方图层亮度蒙版", "Luminance mask from layer below"), () => SetLayerEffect(index, "none", "luminance"), l.MaskMode == "luminance", index > 0);
        Add(mask, T("反向亮度蒙版", "Inverse luminance mask"), () => SetLayerEffect(index, "none", "inverseLuminance"), l.MaskMode == "inverseLuminance", index > 0);
        var up = new MenuItem { Header = T("上移一层", "Move layer up") }; up.Click += (_, _) => MoveLayer(1); menu.Items.Add(up); var down = new MenuItem { Header = T("下移一层", "Move layer down") }; down.Click += (_, _) => MoveLayer(-1); menu.Items.Add(down); return menu;
    }
    void SetLayerEffect(int index, string clip, string mask) { if (index < 0 || index >= style.Layers.Count) return; style.Layers[index].ClipMode = clip; style.Layers[index].MaskMode = mask; RefreshLayerList(index); CommitHistory(); Status(clip == "subtract" ? T("剪切已生效：橙色实线是切刀轮廓，内部透明区域是挖孔", "Cut applied: orange outline is the cutter; the transparent interior is the hole") : clip == "intersect" ? T("相交剪切已生效：仅保留下方图层与切刀重叠部分", "Intersection applied: only the overlap with the layer below remains") : T("图层效果已应用", "Layer effect applied")); }
    FrameworkElement VisualFor(StyleLayer l, double sx, double sy, StyleLayer? effectSource = null, int layerIndex = -1)
    {
        FrameworkElement el;
        if (l.Type == "image")
        {
            el = new Image { Source = ImageSourceFromData(l.ImageData), Stretch = Stretch.Fill };
        }
        else
        {
            Shape shape = l.Type == "ellipse" ? new Ellipse() : l.Type == "line" ? new Line { X1 = 0, Y1 = 0, X2 = l.W * sx, Y2 = l.H * sy } : new Rectangle { RadiusX = l.Radius, RadiusY = l.Radius };
            shape.Fill = l.Type == "line" ? Brushes.Transparent : BrushOf(l.Fill, "#DDE9FF"); shape.Stroke = BrushOf(l.Stroke, "#5B8DEF"); shape.StrokeThickness = l.StrokeWidth; el = shape;
        }
        el.Width = Math.Max(1, l.W * sx); el.Height = Math.Max(1, l.H * sy); el.Opacity = l.Opacity; el.RenderTransform = new RotateTransform(l.Rotation, el.Width / 2, el.Height / 2); Canvas.SetLeft(el, l.X * sx); Canvas.SetTop(el, l.Y * sy);
        var effectMode = effectSource?.MaskMode is "inverseAlpha" or "inverseLuminance" ? "subtract" : effectSource?.MaskMode is "alpha" or "luminance" ? "intersect" : effectSource?.ClipMode ?? "none";
        if (effectSource != null && effectMode != "none") { var source = ClipGeometry(effectSource, l, sx, sy); el.Clip = effectMode == "subtract" ? Geometry.Combine(new RectangleGeometry(new Rect(0, 0, el.Width, el.Height)), source, GeometryCombineMode.Exclude, null) : source; }
        if (layerIndex >= 0) { el.ContextMenu = LayerContextMenu(layerIndex); el.MouseRightButtonDown += (_, _) => { selected = layerIndex; LoadLayerFields(); }; el.ContextMenu.Closed += (_, _) => { if (selected >= 0 && selected < style.Layers.Count) LayerList.SelectedIndex = selected; }; }
        return el;
    }
    void AddResizeHandles(StyleLayer l)
    {
        var left = l.X * 9; var top = l.Y * 5.2; var right = (l.X + l.W) * 9; var bottom = (l.Y + l.H) * 5.2; var cx = (left + right) / 2; var cy = (top + bottom) / 2;
        var points = new[] { (left, top), (cx, top), (right, top), (right, cy), (right, bottom), (cx, bottom), (left, bottom), (left, cy) };
        var cursors = new[] { Cursors.SizeNWSE, Cursors.SizeNS, Cursors.SizeNESW, Cursors.SizeWE, Cursors.SizeNWSE, Cursors.SizeNS, Cursors.SizeNESW, Cursors.SizeWE };
        for (int i = 0; i < points.Length; i++) { int handle = i; var h = new Rectangle { Width = 10, Height = 10, Fill = Brushes.White, Stroke = BrushOf("#347FFF"), StrokeThickness = 2, Cursor = cursors[i] }; Canvas.SetLeft(h, points[i].Item1 - 5); Canvas.SetTop(h, points[i].Item2 - 5);
            h.MouseLeftButtonDown += (_, e) => { if (l.IsLocked) { Status(T("图层已锁定", "Layer is locked")); e.Handled = true; return; } resizing = true; resizeHandle = handle; down = e.GetPosition(DesignCanvas); resizeX = l.X; resizeY = l.Y; resizeW = l.W; resizeH = l.H; DesignCanvas.CaptureMouse(); e.Handled = true; }; DesignCanvas.Children.Add(h); }
    }
    void ResizeSelected(Point p) { if (!resizing || selected < 0 || selected >= style.Layers.Count) return; double dx = (p.X - down.X) / 9, dy = (p.Y - down.Y) / 5.2, x = resizeX, y = resizeY, w = resizeW, h = resizeH; bool moveLeft = resizeHandle is 0 or 6 or 7, moveRight = resizeHandle is 2 or 3 or 4, moveTop = resizeHandle is 0 or 1 or 2, moveBottom = resizeHandle is 4 or 5 or 6; if (moveLeft) { x = resizeX + dx; w = resizeW - dx; } if (moveRight) w = resizeW + dx; if (moveTop) { y = resizeY + dy; h = resizeH - dy; } if (moveBottom) h = resizeH + dy; if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && (moveLeft || moveRight) && (moveTop || moveBottom)) { var ratio = resizeW / Math.Max(.01, resizeH); if (w / Math.Max(.01, h) > ratio) h = w / ratio; else w = h * ratio; if (moveLeft) x = resizeX + resizeW - w; if (moveTop) y = resizeY + resizeH - h; } if (w < 1) { if (moveLeft) x -= 1 - w; w = 1; } if (h < 1) { if (moveTop) y -= 1 - h; h = 1; } var target = style.Layers[selected]; target.X = Math.Clamp(x, 0, 99); target.Y = Math.Clamp(y, 0, 99); target.W = Math.Clamp(w, 1, 100 - target.X); target.H = Math.Clamp(h, 1, 100 - target.Y); RebuildCanvas(); LoadLayerFields(); }
    void RebuildCanvas()
    {
        DesignCanvas.Children.Clear();
        for (int i = 0; i < style.Layers.Count; i++) { var layer = style.Layers[i]; if (!layer.IsVisible) continue; bool cutter = i > 0 && (layer.ClipMode != "none" || layer.MaskMode != "none"); if (cutter && !selectedLayers.Contains(i)) continue; var effectSource = i + 1 < style.Layers.Count && style.Layers[i + 1].IsVisible ? style.Layers[i + 1] : null; var el = VisualFor(layer, 9, 5.2, cutter ? null : effectSource, i); if (cutter && el is Shape cutterOutline) { cutterOutline.Fill = Brushes.Transparent; cutterOutline.Stroke = BrushOf("#FF8A00"); cutterOutline.StrokeThickness = 2.5; cutterOutline.Opacity = 1; el.ToolTip = T("橙色实线仅表示切刀轮廓；内部透明区域就是挖孔", "The orange outline marks the cutter; its transparent interior is the hole"); } if (selectedLayers.Contains(i)) el.Effect = new DropShadowEffect { Color = Color.FromRgb(52, 131, 255), BlurRadius = i == selected ? 14 : 9, ShadowDepth = 0, Opacity = i == selected ? .9 : .65 }; DesignCanvas.Children.Add(el); }
        var tr = style.TextRegion; if (tr.W > 0 && tr.H > 0) { var r = new Rectangle { Width = tr.W * 9, Height = tr.H * 5.2, Stroke = BrushOf(selectedText ? "#347FFF" : "#E64D5F"), StrokeThickness = selectedText ? 3 : 2, StrokeDashArray = new DoubleCollection { 7, 4 }, Fill = BrushOf("#FFF5F688") }; Canvas.SetLeft(r, tr.X * 9); Canvas.SetTop(r, tr.Y * 5.2); DesignCanvas.Children.Add(r);
        var label = new TextBlock { Text = "文字输入区域", Foreground = BrushOf("#B93345"), FontSize = 13 }; Canvas.SetLeft(label, tr.X * 9 + 8); Canvas.SetTop(label, tr.Y * 5.2 + 6); DesignCanvas.Children.Add(label); }
        if (selected >= 0 && selected < style.Layers.Count && !resizing) AddResizeHandles(style.Layers[selected]);
    }

    void LoadLayerFields() { if (selected < 0 || selected >= style.Layers.Count) return; var old = suppressLiveControlSync; suppressLiveControlSync = true; try { var l = style.Layers[selected]; LayerFillBox.Text = l.Fill; LayerStrokeBox.Text = l.Stroke; XBox.Text = l.X.ToString("0.##"); YBox.Text = l.Y.ToString("0.##"); WBox.Text = l.W.ToString("0.##"); HBox.Text = l.H.ToString("0.##"); } finally { suppressLiveControlSync = old; } }
    void ApplyLayerFields() { if (selected < 0 || selected >= style.Layers.Count) return; var l = style.Layers[selected]; l.Fill = string.IsNullOrWhiteSpace(LayerFillBox.Text) ? "#DDE9FF" : LayerFillBox.Text.Trim(); l.Stroke = string.IsNullOrWhiteSpace(LayerStrokeBox.Text) ? "#5B8DEF" : LayerStrokeBox.Text.Trim(); l.X = Num(XBox.Text, l.X); l.Y = Num(YBox.Text, l.Y); l.W = Num(WBox.Text, l.W); l.H = Num(HBox.Text, l.H); LayerList.Items.Refresh(); }
    void DeleteSelected() { var deleting = selectedLayers.Where(i => i >= 0 && i < style.Layers.Count).OrderByDescending(i => i).ToList(); if (deleting.Count == 0 && selected >= 0 && selected < style.Layers.Count) deleting.Add(selected); if (deleting.Count == 0) return; foreach (var i in deleting) style.Layers.RemoveAt(i); selectedLayers.Clear(); RefreshLayerList(Math.Min(deleting.Min(), style.Layers.Count - 1)); CommitHistory(); }
    void RefreshLayerList(int index) { LayerList.ItemsSource = null; LayerList.ItemsSource = style.Layers; selected = index; selectedLayers.Clear(); if (index >= 0) selectedLayers.Add(index); SyncLayerSelection(); RebuildCanvas(); RefreshPreview(); }
    void MoveLayer(int delta) { if (selected < 0 || selected >= style.Layers.Count) return; int n = Math.Clamp(selected + delta, 0, style.Layers.Count - 1); if (n == selected) { Status(delta > 0 ? T("已经是最上层", "Already at top") : T("已经是最下层", "Already at bottom")); return; } var x = style.Layers[selected]; style.Layers.RemoveAt(selected); style.Layers.Insert(n, x); RefreshLayerList(n); CommitHistory(); Status(delta > 0 ? T("图层已上移", "Layer moved up") : T("图层已下移", "Layer moved down")); }
    void MoveLayerExtreme(bool top) { if (selected < 0 || selected >= style.Layers.Count) return; var x = style.Layers[selected]; style.Layers.RemoveAt(selected); int n = top ? style.Layers.Count : 0; style.Layers.Insert(n, x); RefreshLayerList(n); CommitHistory(); Status(top ? T("图层已置顶", "Layer moved to top") : T("图层已置底", "Layer moved to bottom")); }

    void ReadControls()
    {
        style.Name = string.IsNullOrWhiteSpace(NameBox.Text) ? "自定义样式" : NameBox.Text.Trim(); var id = Regex.Replace(IdBox.Text.Trim().ToLowerInvariant(), "[^a-z0-9_-]+", "-").Trim('-'); style.Id = string.IsNullOrEmpty(id) ? "custom-style" : id;
        style.Bg = BgBox.Text.Trim(); style.Border = BorderBox.Text.Trim(); style.Color = ColorBox.Text.Trim(); style.Radius = Num(RadiusBox.Text, 12); style.FontFamily = FontBox.SelectedItem?.ToString() ?? SystemFonts.MessageFontFamily.Source; style.FontSize = Num(FontSizeBox.Text, 14);
        style.ReplaceFrame = true; style.TextSizing.Mode = TextSizingModes.Normalize((SizeModeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString()); style.TextSizing.Aspect = Ratio(AspectBox.Text, 1.8); ReadInputRuleControls();
    }
    void ReadInputRuleControls()
    {
        style.TextRules.MaxLength = Math.Clamp((int)Num(MaxLengthBox.Text), 0, 1_000_000);
        style.TextRules.Type = (InputTypeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "any";
        style.TextRules.Pattern = PatternBox.Text;
        style.TextRules.Required = RequiredBox.IsChecked == true;
        PreviewText.MaxLength = style.TextRules.MaxLength;
    }
    void LivePreviewRulesChanged(object? sender, RoutedEventArgs e)
    {
        if (suppressLiveControlSync) return;
        ReadInputRuleControls(); RefreshPreview(); UpdateSyncState();
    }
    void LivePreviewStyleChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (suppressLiveControlSync) return;
        ReadControls(); UpdateSizingControlState(); RefreshPreview(); UpdateSyncState();
    }
    void LiveStyleControlChanged(object? sender, RoutedEventArgs e)
    {
        if (suppressLiveControlSync) return;
        ReadControls(); UpdateSizingControlState(); RefreshPreview(); UpdateSyncState();
    }
    void LiveLayerControlChanged(object? sender, RoutedEventArgs e)
    {
        if (suppressLiveControlSync || selected < 0 || selected >= style.Layers.Count) return;
        ApplyLayerFields(); RebuildCanvas(); RefreshPreview(); UpdateSyncState();
    }
    void UpdateSizingControlState()
    {
        var uniform = TextSizingModes.Normalize((SizeModeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString()) == TextSizingModes.Uniform;
        AspectBox.IsEnabled = uniform;
    }
    void LoadControls()
    {
        ModelSafety.Normalize(style);
        suppressLiveControlSync = true;
        try
        {
            NameBox.Text = style.Name; IdBox.Text = style.Id; BgBox.Text = style.Bg; BorderBox.Text = style.Border; ColorBox.Text = style.Color; RadiusBox.Text = style.Radius.ToString(); FontSizeBox.Text = style.FontSize.ToString(); AspectBox.Text = style.TextSizing.Aspect.ToString("0.###"); MaxLengthBox.Text = style.TextRules.MaxLength.ToString(); PatternBox.Text = style.TextRules.Pattern; RequiredBox.IsChecked = style.TextRules.Required;
            FontBox.SelectedItem = FontBox.Items.Cast<string>().FirstOrDefault(x => x.Equals(style.FontFamily, StringComparison.OrdinalIgnoreCase)) ?? FontBox.Items.Cast<string>().FirstOrDefault(); SizeModeBox.SelectedIndex = Math.Max(0, SizeModeBox.Items.Cast<ComboBoxItem>().ToList().FindIndex(x => x.Tag?.ToString() == style.TextSizing.Mode)); InputTypeBox.SelectedIndex = Math.Max(0, InputTypeBox.Items.Cast<ComboBoxItem>().ToList().FindIndex(x => x.Tag?.ToString() == style.TextRules.Type));
        }
        finally { suppressLiveControlSync = false; }
        UpdateSizingControlState();
        PreviewText.MaxLength = style.TextRules.MaxLength; LayerList.ItemsSource = style.Layers; RebuildCanvas(); RefreshPreview();
    }
    void RefreshPreview()
    {
        ModelSafety.Normalize(style);
        bool replace = style.ReplaceFrame && style.Layers.Any(x => x.IsVisible); ApplyPreviewSizing(); double pw = double.IsNaN(PreviewCard.Width) ? 420 : PreviewCard.Width, ph = double.IsNaN(PreviewCard.Height) ? 150 : PreviewCard.Height; PreviewCard.Padding = replace ? new Thickness(0) : new Thickness(30); PreviewCard.Background = replace ? Brushes.Transparent : BrushOf(style.Bg, "#FFFFFF"); PreviewCard.BorderBrush = replace ? Brushes.Transparent : BrushOf(style.Border, "#5B8DEF"); PreviewCard.BorderThickness = replace ? new Thickness(0) : new Thickness(style.BorderWidth); PreviewCard.CornerRadius = new CornerRadius(style.Radius); PreviewText.Foreground = BrushOf(style.Color, "#2C3140"); PreviewText.FontFamily = new FontFamily(style.FontFamily); PreviewText.FontSize = style.FontSize; PreviewText.VerticalContentAlignment = VerticalAlignment.Center; PreviewLayers.Width = pw; PreviewLayers.Height = ph; PreviewLayers.Children.Clear(); for (int i = 0; i < style.Layers.Count; i++) { var layer = style.Layers[i]; if (!layer.IsVisible || (i > 0 && (layer.ClipMode != "none" || layer.MaskMode != "none"))) continue; var effectSource = i + 1 < style.Layers.Count && style.Layers[i + 1].IsVisible ? style.Layers[i + 1] : null; PreviewLayers.Children.Add(VisualFor(layer, pw / 100, ph / 100, effectSource)); } var tr = style.TextRegion; if (replace && tr.W > 0 && tr.H > 0) { PreviewText.HorizontalAlignment = HorizontalAlignment.Left; PreviewText.VerticalAlignment = VerticalAlignment.Top; PreviewText.Width = tr.W * pw / 100; PreviewText.Height = tr.H * ph / 100; PreviewText.Margin = new Thickness(tr.X * pw / 100, tr.Y * ph / 100, 0, 0); } else { PreviewText.HorizontalAlignment = HorizontalAlignment.Stretch; PreviewText.VerticalAlignment = VerticalAlignment.Center; PreviewText.Width = double.NaN; PreviewText.Height = double.NaN; PreviewText.Margin = new Thickness(0); }
        ApplyPreviewValidity();
    }
    void ApplyPreviewSizing()
    {
        if (PreviewCard == null || style == null) return;
        var size = CalculateMeasuredPreviewSize();
        PreviewCard.Width = size.Width; PreviewCard.Height = size.Height;
    }

    PreviewDimensions CalculateMeasuredPreviewSize()
    {
        const double maximum = 10_000;
        var text = PreviewText?.Text ?? "";
        var baseline = PreviewSizingCalculator.Calculate(style, text, MeasureText("字", double.PositiveInfinity));
        var region = style.TextRegion;
        if (text.Length == 0 || region.W <= 0 || region.H <= 0) return baseline;

        var widthFraction = Math.Clamp(region.W / 100, .001, 1);
        var heightFraction = Math.Clamp(region.H / 100, .001, 1);
        var mode = TextSizingModes.Normalize(style.TextSizing.Mode);

        bool Fits(double cardWidth, double cardHeight)
        {
            var required = MeasurePreviewText(cardWidth * widthFraction);
            return required.Width <= cardWidth * widthFraction + .5 && required.Height <= cardHeight * heightFraction + .5;
        }

        if (Fits(baseline.Width, baseline.Height)) return baseline;

        if (mode == TextSizingModes.Uniform)
        {
            var maximumScale = Math.Min(maximum / baseline.Width, maximum / baseline.Height);
            var low = 1d; var high = 1d;
            while (high < maximumScale && !Fits(baseline.Width * high, baseline.Height * high)) high = Math.Min(maximumScale, high * 2);
            if (!Fits(baseline.Width * high, baseline.Height * high)) return new(baseline.Width * high, baseline.Height * high);
            for (var i = 0; i < 32; i++)
            {
                var middle = (low + high) / 2;
                if (Fits(baseline.Width * middle, baseline.Height * middle)) high = middle; else low = middle;
            }
            return new(baseline.Width * high, baseline.Height * high);
        }

        // 自由拉伸也先使用完整构图的最小尺寸。只有基准文字区域已经放不下
        // 实测文字时，才扩大需要的轴；图层和文字区域仍由同一个 PreviewCard
        // 坐标系统一缩放。
        var baseRegionWidth = Math.Max(1, baseline.Width * widthFraction);
        var natural = MeasurePreviewText(double.PositiveInfinity);
        var targetRegionWidth = Math.Max(baseRegionWidth, Math.Min(420, natural.Width));
        var width = Math.Min(maximum, targetRegionWidth / widthFraction);
        var wrapped = MeasurePreviewText(width * widthFraction);
        var height = Math.Min(maximum, Math.Max(baseline.Height, wrapped.Height / heightFraction));
        return new(width, height);
    }

    PreviewDimensions MeasureText(string text, double regionWidth)
    {
        var padding = PreviewText?.Padding ?? new Thickness(6, 4, 6, 4);
        var horizontalInsets = padding.Left + padding.Right + 4;
        var verticalInsets = padding.Top + padding.Bottom + 4;
        FontFamily family;
        try { family = new FontFamily(style.FontFamily); } catch { family = SystemFonts.MessageFontFamily; }
        var probe = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = family,
            FontSize = Math.Clamp(style.FontSize, 1, 500),
            FontWeight = FontWeight.FromOpenTypeWeight((int)Math.Clamp(style.FontWeight, 1, 999))
        };
        var availableWidth = double.IsFinite(regionWidth) ? Math.Max(1, regionWidth - horizontalInsets) : double.PositiveInfinity;
        probe.Measure(new Size(availableWidth, double.PositiveInfinity));
        var width = probe.DesiredSize.Width + horizontalInsets;
        var height = probe.DesiredSize.Height + verticalInsets;
        return new(
            double.IsFinite(width) && width > 0 ? width : Math.Max(1, style.FontSize + horizontalInsets),
            double.IsFinite(height) && height > 0 ? height : Math.Max(1, style.FontSize * 1.4 + verticalInsets));
    }
    PreviewDimensions MeasurePreviewText(double regionWidth) => MeasureText(PreviewText?.Text ?? "", regionWidth);
    string ProposedPreviewText(string inserted) { var start = PreviewText.SelectionStart; return PreviewText.Text.Remove(start, PreviewText.SelectionLength).Insert(start, inserted); }
    bool IsAllowed(string value) => InputRuleValidator.IsAllowed(value, style.TextRules);
    void ApplyPreviewValidity()
    {
        var valid = InputRuleValidator.IsCompleteValueValid(PreviewText.Text, style.TextRules);
        PreviewText.BorderThickness = valid ? new Thickness(0) : new Thickness(1.5);
        PreviewText.BorderBrush = valid ? Brushes.Transparent : BrushOf("#E64D5F");
    }
    void ValidatePreviewTextInput(object sender, TextCompositionEventArgs e) { if (!IsAllowed(ProposedPreviewText(e.Text))) { e.Handled = true; Status(T("输入不符合当前限制", "Input does not match the current rules")); } }
    void PreviewPaste(object sender, DataObjectPastingEventArgs e) { var text = e.DataObject.GetData(DataFormats.UnicodeText) as string ?? e.DataObject.GetData(DataFormats.Text) as string ?? ""; if (!IsAllowed(ProposedPreviewText(text))) { e.CancelCommand(); Status(T("粘贴内容不符合当前限制", "Pasted text does not match the current rules")); } }

    void OpenStyle()
    {
        var d = new OpenFileDialog { Filter = T("BMAP 文本框样式|*.bmaptextstyle;*.json|所有文件|*.*", "BMAP text-box style|*.bmaptextstyle;*.json|All files|*.*") }; if (d.ShowDialog() != true) return;
        try { var lib = JsonSerializer.Deserialize<StyleLibrary>(File.ReadAllText(d.FileName), json); if (lib?.Styles.Count > 0) style = lib.Styles[0]; else style = JsonSerializer.Deserialize<TextBoxStyle>(File.ReadAllText(d.FileName), json) ?? new(); currentFile = d.FileName; lastSyncedId = null; lastSyncedSnapshot = null; hasStandaloneSave = true; selected = -1; selectedLayers.Clear(); selectedText = false; LoadControls(); ResetHistory(); UpdateSaveButton(); UpdateSyncState(); Status(T("已打开 ", "Opened ") + d.FileName); } catch (Exception ex) { MessageBox.Show(T("无法打开样式：", "Could not open style: ") + ex.Message); }
    }
    void SaveStandalone()
    {
        ReadControls(); if (!EnsureTextRegion()) return; var d = new SaveFileDialog { Filter = T("BMAP 文本框样式|*.bmaptextstyle", "BMAP text-box style|*.bmaptextstyle"), FileName = style.Id + ".bmaptextstyle" }; if (!string.IsNullOrEmpty(currentFile)) d.InitialDirectory = IOPath.GetDirectoryName(currentFile); if (d.ShowDialog() != true) return; WriteLibrary(d.FileName, new StyleLibrary { Styles = [style] }); currentFile = d.FileName; hasStandaloneSave = true; UpdateSaveButton(); Status(T("样式已保存", "Style saved"));
    }
    bool EnsureTextRegion() { if (style.TextRegion.W > 0 && style.TextRegion.H > 0) return true; ShowEditor(); WorkTabs.SelectedIndex = 0; SetTool("text"); MessageBox.Show(T("请先使用“文本区域”工具画出文字输入区域，然后再保存。", "Draw a text input region with the Text Region tool before saving."), T("需要文字输入区域", "Text region required"), MessageBoxButton.OK, MessageBoxImage.Information); return false; }
    void WriteLibrary(string file, StyleLibrary lib) { foreach (var item in lib.Styles) ModelSafety.Normalize(item); Directory.CreateDirectory(IOPath.GetDirectoryName(file)!); var tmp = file + ".tmp"; File.WriteAllText(tmp, JsonSerializer.Serialize(lib, json), new System.Text.UTF8Encoding(false)); File.Copy(tmp, file, true); File.Delete(tmp); }

    bool Connect()
    {
        var d = new OpenFileDialog { Title = T("选择 ThoughtCanvas 主程序、源码 main.js 或桌面快捷方式", "Select ThoughtCanvas, source main.js, or a desktop shortcut"), Filter = T("ThoughtCanvas|*.exe;*.lnk;main.js|可执行文件|*.exe|快捷方式|*.lnk|main.js|main.js", "ThoughtCanvas|*.exe;*.lnk;main.js|Executable|*.exe|Shortcut|*.lnk|main.js|main.js") }; if (d.ShowDialog() != true) return false;
        try { var target = ResolveTarget(d.FileName); if (!File.Exists(target)) throw new FileNotFoundException(T("快捷方式指向的目标不存在", "Shortcut target does not exist"), target); SetConnected(target); SaveConnection(); Status(T("连接成功", "Connected")); return true; } catch (Exception ex) { MessageBox.Show(T("连接失败：", "Connection failed: ") + ex.Message); return false; }
    }
    static string ResolveTarget(string file)
    {
        if (!file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return file; var t = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("系统快捷方式服务不可用"); dynamic shell = Activator.CreateInstance(t)!; dynamic shortcut = shell.CreateShortcut(file); return (string)shortcut.TargetPath;
    }
    void Sync(bool chooseAnother = false)
    {
        if (chooseAnother) { if (!Connect()) return; } else if (connectedRoot == null || connectedTarget == null) { if (!Connect()) return; } ReadControls(); if (!EnsureTextRegion()) return;
        var target = connectedTarget; var targetRoot = connectedRoot; if (target == null || targetRoot == null) return; var roots = new List<string>(); if (target.EndsWith("main.js", StringComparison.OrdinalIgnoreCase)) roots.Add(targetRoot); else { roots.Add(targetRoot); roots.Add(IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ThoughtCanvas")); }
        var destinations = roots.Distinct(StringComparer.OrdinalIgnoreCase).Select(root => IOPath.Combine(root, "text-styles", "custom-text-styles.json")).ToList(); var libraries = new List<(string File, StyleLibrary Library)>(); foreach (var file in destinations) { StyleLibrary lib; try { lib = JsonSerializer.Deserialize<StyleLibrary>(File.ReadAllText(file), json) ?? new(); } catch { lib = new(); } libraries.Add((file, lib)); }
        var updatingSameStyle = lastSyncedId != null && lastSyncedId.Equals(style.Id, StringComparison.OrdinalIgnoreCase); if (!updatingSameStyle) { var uniqueId = StyleIdentity.UniqueId(style.Id, libraries.Select(x => x.Library)); if (!uniqueId.Equals(style.Id, StringComparison.OrdinalIgnoreCase)) { style.Id = uniqueId; IdBox.Text = uniqueId; } }
        int written = 0; var errors = new List<string>(); foreach (var entry in libraries) { try { var old = updatingSameStyle ? entry.Library.Styles.FindIndex(x => x.Id.Equals(style.Id, StringComparison.OrdinalIgnoreCase)) : -1; if (old >= 0) entry.Library.Styles[old] = style; else entry.Library.Styles.Add(style); WriteLibrary(entry.File, entry.Library); written++; } catch (Exception ex) { errors.Add(ex.Message); } }
        if (written > 0) { lastSyncedId = style.Id; lastSyncedSnapshot = Snapshot(); SetConnected(target); SaveConnection(); UpdateSyncState(lastSyncedSnapshot); Status(T("已同步", "Synced")); } else MessageBox.Show(T("同步失败：", "Sync failed: ") + string.Join(Environment.NewLine, errors));
    }
    void Launch() { if (connectedTarget == null) { Connect(); if (connectedTarget == null) return; } try { Process.Start(new ProcessStartInfo(connectedTarget) { UseShellExecute = true }); } catch (Exception ex) { MessageBox.Show(T("无法打开主程序：", "Could not launch ThoughtCanvas: ") + ex.Message); } }
    static bool IsTextEditingFocus(IInputElement? focused) => focused is TextBox or PasswordBox || focused is ComboBox { IsEditable: true };
    bool HandleHistoryShortcut(Key key, ModifierKeys modifiers, bool textInputFocused)
    {
        if (textInputFocused || !modifiers.HasFlag(ModifierKeys.Control)) return false;
        if (key == Key.Z)
        {
            if (modifiers.HasFlag(ModifierKeys.Shift)) Redo(); else Undo();
            return true;
        }
        if (key == Key.Y) { Redo(); return true; }
        return false;
    }
    void OnKey(object sender, KeyEventArgs e)
    {
        var modifiers = e.KeyboardDevice.Modifiers;
        if (HandleHistoryShortcut(e.Key, modifiers, IsTextEditingFocus(Keyboard.FocusedElement))) { e.Handled = true; return; }
        // TextBox 自己维护逐字输入的撤销栈；普通 ComboBox 获得焦点时也不触发
        // 画布的工具字母快捷键。历史快捷键已在上面单独处理。
        if (Keyboard.FocusedElement is TextBox or PasswordBox or ComboBox) return;
        if (e.Key == Key.Delete) { DeleteSelected(); e.Handled = true; return; }
        if (e.Key == Key.Escape || e.Key == Key.V) { SetTool("select"); e.Handled = true; return; }
        if (e.Key == Key.R) { SetTool("rect"); e.Handled = true; return; }
        if (e.Key == Key.E) { SetTool("ellipse"); e.Handled = true; return; }
        if (e.Key == Key.L) { SetTool("line"); e.Handled = true; return; }
        if (e.Key == Key.T) { SetTool("text"); e.Handled = true; return; }
        if (modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.OemCloseBrackets) { MoveLayer(modifiers.HasFlag(ModifierKeys.Shift) ? 999 : 1); e.Handled = true; return; }
        if (modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.OemOpenBrackets) { MoveLayer(modifiers.HasFlag(ModifierKeys.Shift) ? -999 : -1); e.Handled = true; return; }
        if (selected >= 0 && selected < style.Layers.Count && e.Key is Key.Left or Key.Right or Key.Up or Key.Down) { var l = style.Layers[selected]; if (l.IsLocked) { Status(T("图层已锁定", "Layer is locked")); return; } double step = modifiers.HasFlag(ModifierKeys.Shift) ? .1 : 1; if (e.Key == Key.Left) l.X -= step; if (e.Key == Key.Right) l.X += step; if (e.Key == Key.Up) l.Y -= step; if (e.Key == Key.Down) l.Y += step; l.X = Math.Clamp(l.X, 0, 100 - l.W); l.Y = Math.Clamp(l.Y, 0, 100 - l.H); RebuildCanvas(); LoadLayerFields(); CommitHistory(); e.Handled = true; }
    }
}
