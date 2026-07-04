using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Text.Json;
using System.Windows.Shapes;
using IoPath = System.IO.Path;

namespace BmapUiEditor
{
    /// <summary>
    /// 画布与图层交互（partial）：添加部件/图形/手绘/图片、拖动缩放、自动吸附、
    /// 图层面板（排序/显隐/删除）、遮罩（裁剪下一层）、画笔模式、各类属性面板。
    /// </summary>
    public partial class MainWindow
    {
        internal readonly List<CanvasElement> _elements = new List<CanvasElement>();   // 索引越大越在上层
        private readonly Dictionary<CanvasElement, UIElement> _views = new Dictionary<CanvasElement, UIElement>();
        private readonly Dictionary<ShapeElement, Line> _lineHits = new Dictionary<ShapeElement, Line>();
        private CanvasElement _sel;
        private bool _syncingLayers;
        private bool _syncingShape;
        private int _shapeCounter, _inkCounter, _imgCounter;

        // 拖拽状态
        private bool _moving;
        private int _handle = -1;            // 0..7 缩放手柄；100/101 直线端点；-1 无
        private Point _down;
        private double _oX, _oY, _oW, _oH, _oX2, _oY2;
        private List<Point> _inkOrig;
        private bool _dragUndoPending;       // 真正动了才记撤销，防止点一下就多一条

        // 绘制工具（拖出图形）
        private string _tool;
        private bool _drawing;
        private Point _drawStart;
        private ShapeElement _drawShape;
        private bool _lockSquare;            // 锁定 1:1

        private const double SnapTol = 5;
        private const double SqTol = 8;      // 1:1 磁吸阈值

        // ================= 撤销 / 重做（整画布快照） =================
        private readonly List<string> _undoStack = new List<string>();
        private readonly List<string> _redoStack = new List<string>();
        private string _lastUndoKey;
        private DateTime _lastUndoTime;

        private string SnapshotJson()
        {
            var dto = new ProjectDto { elements = _elements.Select(ToDto).Where(x => x != null).ToList() };
            return JsonSerializer.Serialize(dto);
        }

        /// <summary>在"即将改动"之前调用。coalesceKey 相同且间隔小于 1 秒的连续改动合并成一步（滑滑块/敲颜色不炸栈）。</summary>
        internal void PushUndo(string coalesceKey = null)
        {
            if (coalesceKey != null && coalesceKey == _lastUndoKey
                && (DateTime.Now - _lastUndoTime).TotalMilliseconds < 1000)
            {
                _lastUndoTime = DateTime.Now;
                return;
            }
            _undoStack.Add(SnapshotJson());
            if (_undoStack.Count > 60) _undoStack.RemoveAt(0);
            _redoStack.Clear();
            _lastUndoKey = coalesceKey;
            _lastUndoTime = DateTime.Now;
        }

        internal void ResetUndo()
        {
            _undoStack.Clear();
            _redoStack.Clear();
            _lastUndoKey = null;
        }

        internal void Undo()
        {
            if (_undoStack.Count == 0) { SetStatus("没有可撤销的操作了。"); return; }
            _redoStack.Add(SnapshotJson());
            string json = _undoStack[_undoStack.Count - 1];
            _undoStack.RemoveAt(_undoStack.Count - 1);
            _lastUndoKey = null;
            RestoreSnapshot(json);
            SetStatus("已撤销（Ctrl+Y 重做）。");
        }

        internal void Redo()
        {
            if (_redoStack.Count == 0) { SetStatus("没有可重做的操作了。"); return; }
            _undoStack.Add(SnapshotJson());
            string json = _redoStack[_redoStack.Count - 1];
            _redoStack.RemoveAt(_redoStack.Count - 1);
            _lastUndoKey = null;
            RestoreSnapshot(json);
            SetStatus("已重做。");
        }

        private void RestoreSnapshot(string json)
        {
            ProjectDto dto = null;
            try { dto = JsonSerializer.Deserialize<ProjectDto>(json); } catch { }
            ClearElements();
            if (dto != null && dto.elements != null)
                foreach (var d in dto.elements)
                {
                    var el = FromDto(d);
                    if (el != null) AddElementSilent(el);
                }
            RebuildZ();
            ApplyMasks();
            RefreshLayers();
            ClearSelection();
            MarkDirty();
        }

        // ================= 原版参考底图（ghost）+ 部件浓度 =================

        /// <summary>（皮肤设计模型下已停用参考底图；保留方法以兼容调用点。）</summary>
        internal void RefreshGhost()
        {
            if (GhostHost != null) GhostHost.Children.Clear();
        }

        /// <summary>已添加的原版部件按偏好浓度显示；选中的那块提亮但不到正常。你画的图形/手绘/图片不受影响。</summary>
        internal void ApplyComponentOpacity()
        {
            foreach (var el in _elements)
            {
                var ce = el as ComponentElement;
                if (ce == null) continue;
                UIElement v;
                if (!_views.TryGetValue(ce, out v)) continue;
                if (!ce.Visible) continue;   // 隐藏由 Visibility 处理
                v.Opacity = ReferenceEquals(ce, _sel) ? Prefs.SelectedOpacity : Prefs.OriginalOpacity;
            }
        }

        // ================= 选中框 / 手柄 / 辅助线 =================
        private Rectangle _outline;
        private readonly Rectangle[] _handles = new Rectangle[8];
        private readonly Rectangle[] _ends = new Rectangle[2];
        private Line _vGuide, _hGuide;
        private readonly List<Rectangle> _regionRects = new List<Rectangle>();

        private void InitAdorners()
        {
            _outline = new Rectangle
            {
                Stroke = BrushOf("#5b8def"),
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 4, 3 },
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed
            };
            Overlay.Children.Add(_outline);
            Panel.SetZIndex(_outline, 1000);

            var cursors = new[] { Cursors.SizeNWSE, Cursors.SizeNS, Cursors.SizeNESW, Cursors.SizeWE,
                                  Cursors.SizeNWSE, Cursors.SizeNS, Cursors.SizeNESW, Cursors.SizeWE };
            for (int i = 0; i < 8; i++)
            {
                var h = new Rectangle
                {
                    Width = 9, Height = 9,
                    Fill = Brushes.White,
                    Stroke = BrushOf("#5b8def"),
                    StrokeThickness = 1.2,
                    Cursor = cursors[i],
                    Tag = i,
                    Visibility = Visibility.Collapsed
                };
                h.MouseLeftButtonDown += HandleDown;
                h.MouseMove += DragMove;
                h.MouseLeftButtonUp += DragUp;
                Overlay.Children.Add(h);
                Panel.SetZIndex(h, 1001);
                _handles[i] = h;
            }
            for (int i = 0; i < 2; i++)
            {
                var h = new Rectangle
                {
                    Width = 11, Height = 11, RadiusX = 5.5, RadiusY = 5.5,
                    Fill = Brushes.White,
                    Stroke = BrushOf("#5b8def"),
                    StrokeThickness = 1.2,
                    Cursor = Cursors.Cross,
                    Tag = 100 + i,
                    Visibility = Visibility.Collapsed
                };
                h.MouseLeftButtonDown += HandleDown;
                h.MouseMove += DragMove;
                h.MouseLeftButtonUp += DragUp;
                Overlay.Children.Add(h);
                Panel.SetZIndex(h, 1001);
                _ends[i] = h;
            }

            _vGuide = new Line { Stroke = BrushOf("#e0533d"), StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 5, 3 }, IsHitTestVisible = false, Visibility = Visibility.Collapsed, Y1 = 0, Y2 = 700 };
            _hGuide = new Line { Stroke = BrushOf("#e0533d"), StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 5, 3 }, IsHitTestVisible = false, Visibility = Visibility.Collapsed, X1 = 0, X2 = 1200 };
            Overlay.Children.Add(_vGuide);
            Overlay.Children.Add(_hGuide);
            Panel.SetZIndex(_vGuide, 999);
            Panel.SetZIndex(_hGuide, 999);
        }

        // ================= 添加元素 =================
        private static string KindName(string kind)
        {
            switch (kind)
            {
                case "roundrect": return "圆角矩形";
                case "ellipse": return "圆形";
                case "line": return "直线";
                default: return "矩形";
            }
        }

        private static string ElementIcon(CanvasElement el)
        {
            if (el is ComponentElement) return "🧩";
            if (el is InkElement) return "🖊";
            if (el is ImageElement) return "🖼";
            if (el is TextRegionElement) return "🅣";
            var s = el as ShapeElement;
            if (s != null)
                switch (s.Kind)
                {
                    case "roundrect": return "▢";
                    case "ellipse": return "◯";
                    case "line": return "╱";
                    default: return "▭";
                }
            return "·";
        }

        internal void AddElementSilent(CanvasElement el, int index = -1)
        {
            if (index < 0 || index > _elements.Count) index = _elements.Count;
            _elements.Insert(index, el);
            CreateView(el);
            UpdateView(el);
        }

        internal void AddElement(CanvasElement el, int index = -1)
        {
            PushUndo();
            RbViewCanvas.IsChecked = true;
            AddElementSilent(el, index);
            RebuildZ();
            ApplyMasks();
            RefreshLayers();
            SelectElement(el);
            MarkDirty();
        }

        internal void AddComponent(string id)
        {
            var def = ComponentLib.Find(id);
            if (def == null) return;
            var exist = _elements.OfType<ComponentElement>().FirstOrDefault(c => c.CompId == id);
            if (exist != null)
            {
                SelectElement(exist);
                SetStatus("「" + def.Name + "」已经在画布上了，点右侧改它的颜色。");
                return;
            }
            var ce = def.CreateInstance();
            AddElement(ce, id == "stage" ? 0 : -1);
            SetStatus("已添加原版「" + def.Name + "」：点它改颜色；不改就保持原版。");
        }

        /// <summary>给当前选中的图形/手绘/图片盖一个遮罩（插到它正上方，立刻能看到裁剪效果）。</summary>
        internal void AddMaskOverSelected()
        {
            var target = _sel;
            if (target == null || !IsMaskTarget(target))
            {
                SetStatus("先选中一个图形 / 手绘 / 图片，再点「🎭 加遮罩」。");
                return;
            }
            var r = CssBuilder.BBoxOfElement(target);
            var m = new ShapeElement
            {
                Kind = "roundrect",
                IsMask = true,
                Name = "遮罩 " + (++_shapeCounter),
                Radius = Math.Max(6, Math.Min(24, Math.Min(r.Width, r.Height) / 4)),
                X = r.X + r.Width * 0.12,
                Y = r.Y + r.Height * 0.12,
                W = Math.Max(20, r.Width * 0.76),
                H = Math.Max(20, r.Height * 0.76)
            };
            AddElement(m, _elements.IndexOf(target) + 1);
            SetStatus("遮罩已盖在「" + target.Name + "」上：只露出遮罩范围内的部分。拖动/缩放这个虚线框试试；不要了就删掉它。");
        }

        // ================= 绘制工具：选工具 → 在画布上拖出图形 =================
        internal void SetTool(string kind)
        {
            if (_tool == kind) { ClearTool(); return; }
            _tool = kind;
            if (BtnPen.IsChecked == true) BtnPen.IsChecked = false;
            RbViewCanvas.IsChecked = true;
            DrawLayer.Visibility = Visibility.Visible;
            HighlightToolButtons();
            SetStatus("在设计框里按住拖出一个「" + KindName(kind) + "」——长宽接近时磁吸成正方形/正圆（按住 Shift 强制 1:1）。");
        }

        internal void ClearTool()
        {
            _tool = null;
            DrawLayer.Visibility = Visibility.Collapsed;
            HighlightToolButtons();
        }

        private void HighlightToolButtons()
        {
            SetBtnActive(BtnAddRect, _tool == "rect");
            SetBtnActive(BtnAddRound, _tool == "roundrect");
            SetBtnActive(BtnAddEllipse, _tool == "ellipse");
            SetBtnActive(BtnAddLine, _tool == "line");
        }

        private static void SetBtnActive(Button b, bool on)
        {
            b.Background = on ? BrushOf("#dbe8ff") : Brushes.White;
            b.FontWeight = on ? FontWeights.Bold : FontWeights.Normal;
        }

        private void DrawDown(object sender, MouseButtonEventArgs e)
        {
            if (_tool == null) return;
            var p = e.GetPosition(Stage);
            _drawStart = p;
            _drawing = true;
            PushUndo();
            var s = new ShapeElement { Kind = _tool, Name = KindName(_tool) + " " + (++_shapeCounter) };
            if (_tool == "line") { s.X = p.X; s.Y = p.Y; s.X2 = p.X; s.Y2 = p.Y; s.NoFill = true; s.StrokeW = 3; s.Stroke = "#5b8def"; }
            else { s.X = p.X; s.Y = p.Y; s.W = 1; s.H = 1; }
            _drawShape = s;
            AddElementSilent(s);
            RebuildZ();
            UpdateView(s);
            _sel = s;
            DrawLayer.CaptureMouse();
            Focus();
            e.Handled = true;
        }

        private void DrawMove(object sender, MouseEventArgs e)
        {
            if (!_drawing || _drawShape == null) return;
            var cur = e.GetPosition(Stage);
            var s = _drawShape;
            if (s.Kind == "line") { s.X2 = cur.X; s.Y2 = cur.Y; UpdateView(s); return; }

            double dx = cur.X - _drawStart.X, dy = cur.Y - _drawStart.Y;
            double w = Math.Abs(dx), h = Math.Abs(dy);
            bool forceSq = _lockSquare || (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            bool snapped = false;
            double m = Math.Max(w, h);
            if (forceSq) { w = h = m; snapped = true; }
            else if (Math.Abs(w - h) <= Math.Max(SqTol, 0.1 * m)) { w = h = m; snapped = true; }

            s.W = Math.Max(1, w); s.H = Math.Max(1, h);
            s.X = dx < 0 ? _drawStart.X - s.W : _drawStart.X;
            s.Y = dy < 0 ? _drawStart.Y - s.H : _drawStart.Y;
            UpdateView(s);
            if (snapped) SetStatus((s.Kind == "ellipse" ? "正圆" : "正方形") + " · 磁吸 1:1（" + Math.Round(s.W) + "×" + Math.Round(s.H) + "）");
        }

        private void DrawUp(object sender, MouseButtonEventArgs e)
        {
            if (!_drawing) return;
            _drawing = false;
            DrawLayer.ReleaseMouseCapture();
            var s = _drawShape;
            _drawShape = null;
            if (s != null)
            {
                if (s.Kind == "line")
                {
                    if (Math.Abs(s.X2 - s.X) + Math.Abs(s.Y2 - s.Y) < 6) { s.X2 = s.X + 140; }   // 只是点了一下 → 给默认长度
                }
                else if (s.W < 6 && s.H < 6)
                {
                    s.W = s.Kind == "ellipse" ? 90 : 140; s.H = 90;   // 只是点了一下 → 给默认尺寸
                }
                UpdateView(s);
            }
            RebuildZ();
            ApplyMasks();
            RefreshLayers();
            if (s != null) SelectElement(s);
            ClearTool();   // 画完一个回到选择态；想再画点一次工具
            MarkDirty();
            e.Handled = true;
        }

        internal void AddShape(string kind)
        {
            var s = new ShapeElement { Kind = kind };
            double off = (_shapeCounter % 8) * 24;
            _shapeCounter++;
            s.Name = KindName(kind) + " " + _shapeCounter;
            if (kind == "line")
            {
                s.X = 520 + off; s.Y = 150 + off; s.X2 = 680 + off; s.Y2 = 150 + off;
                s.NoFill = true; s.StrokeW = 3; s.Stroke = "#5b8def";
            }
            else if (kind == "ellipse") { s.X = 560 + off; s.Y = 120 + off; s.W = 90; s.H = 90; }
            else { s.X = 540 + off; s.Y = 110 + off; s.W = 140; s.H = 90; }
            AddElement(s);
            SetStatus("已添加「" + KindName(kind) + "」：拖动摆放（自动吸附），右侧改颜色；放到顶栏/卡片上=装饰它们。");
        }

        private void ImportImage()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "图片 (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg" };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                byte[] bytes = File.ReadAllBytes(dlg.FileName);
                string ext = IoPath.GetExtension(dlg.FileName).ToLowerInvariant();
                string mime = ext == ".png" ? "image/png" : "image/jpeg";
                var bi = BitmapFromBytes(bytes);
                double w = bi.PixelWidth, h = bi.PixelHeight;
                double scale = Math.Min(1.0, Math.Min(420.0 / Math.Max(1, w), 320.0 / Math.Max(1, h)));
                w = Math.Max(8, w * scale);
                h = Math.Max(8, h * scale);
                var im = new ImageElement
                {
                    Name = "图片 " + (++_imgCounter),
                    Base64 = Convert.ToBase64String(bytes),
                    Mime = mime,
                    X = 600 - w / 2, Y = 377 - h / 2, W = w, H = h
                };
                AddElement(im);
                SetStatus("图片已导入：拖动摆放、拖角缩放；在它上面放一个「遮罩」图形可裁成圆形/圆角。");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "导入失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static BitmapImage BitmapFromBytes(byte[] bytes)
        {
            var bi = new BitmapImage();
            using (var ms = new MemoryStream(bytes))
            {
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.StreamSource = ms;
                bi.EndInit();
            }
            bi.Freeze();
            return bi;
        }

        // ================= 视图创建 / 刷新 =================
        private void CreateView(CanvasElement el)
        {
            UIElement v = null;

            var ce = el as ComponentElement;
            if (ce != null) v = BuildComponentView(ce);

            var s = el as ShapeElement;
            if (s != null)
            {
                Shape sh;
                switch (s.Kind)
                {
                    case "ellipse": sh = new Ellipse(); break;
                    case "line": sh = new Line { StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round }; break;
                    default: sh = new Rectangle(); break;
                }
                sh.Tag = el;
                sh.Cursor = Cursors.SizeAll;
                HookDrag(sh);
                v = sh;
                if (s.Kind == "line")
                {
                    var hit = new Line { Stroke = Brushes.Transparent, StrokeThickness = 14, Tag = el, Cursor = Cursors.SizeAll };
                    HookDrag(hit);
                    _lineHits[s] = hit;
                    Stage.Children.Add(hit);
                }
            }

            var k = el as InkElement;
            if (k != null)
            {
                var pl = new Polyline
                {
                    StrokeLineJoin = PenLineJoin.Round,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    Tag = el,
                    Cursor = Cursors.SizeAll
                };
                HookDrag(pl);
                v = pl;
            }

            var im = el as ImageElement;
            if (im != null)
            {
                var img = new Image { Stretch = Stretch.Fill, Tag = el, Cursor = Cursors.SizeAll };
                try { img.Source = BitmapFromBytes(Convert.FromBase64String(im.Base64)); } catch { }
                HookDrag(img);
                v = img;
            }

            var tr = el as TextRegionElement;
            if (tr != null)
            {
                var bd = new Border
                {
                    BorderBrush = BrushOf("#993556"),
                    BorderThickness = new Thickness(1.4),
                    Background = new SolidColorBrush(Color.FromArgb(20, 212, 83, 126)),
                    Tag = el,
                    Cursor = Cursors.SizeAll
                };
                bd.BorderBrush = new SolidColorBrush(Color.FromRgb(0x99, 0x35, 0x56)) { Opacity = 0.9 };
                var tbk = new TextBlock { Tag = "txt", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 2, 4, 2), IsHitTestVisible = false };
                bd.Child = tbk;
                HookDrag(bd);
                v = bd;
            }

            if (v == null) return;
            _views[el] = v;
            Stage.Children.Add(v);
        }

        private Canvas BuildComponentView(ComponentElement ce)
        {
            var g = ComponentVisuals.Build(ce);
            foreach (var hit in g.Children.OfType<Rectangle>().Where(r => ReferenceEquals(r.Tag, ce)).ToList())
                hit.MouseLeftButtonDown += (s, e) => { SelectElement(ce); e.Handled = true; };
            return g;
        }

        internal void RebuildComponentView(ComponentElement ce)
        {
            UIElement old;
            if (_views.TryGetValue(ce, out old)) Stage.Children.Remove(old);
            var g = BuildComponentView(ce);
            g.Visibility = ce.Visible ? Visibility.Visible : Visibility.Collapsed;
            _views[ce] = g;
            Stage.Children.Add(g);
            RebuildZ();
            ApplyMasks();
            ApplyComponentOpacity();
            if (ReferenceEquals(_sel, ce)) ShowSelectionVisual();
        }

        internal void UpdateView(CanvasElement el)
        {
            UIElement v;
            if (!_views.TryGetValue(el, out v)) return;
            v.Visibility = el.Visible ? Visibility.Visible : Visibility.Collapsed;

            var s = el as ShapeElement;
            if (s != null)
            {
                var sh = (Shape)v;
                sh.Opacity = s.IsMask ? 1.0 : s.Opacity;
                if (s.Kind == "line")
                {
                    var ln = (Line)sh;
                    ln.X1 = s.X; ln.Y1 = s.Y; ln.X2 = s.X2; ln.Y2 = s.Y2;
                    ln.Stroke = BrushOf(s.Stroke);
                    ln.StrokeThickness = Math.Max(1, s.StrokeW);
                    var hit = _lineHits[s];
                    hit.X1 = s.X; hit.Y1 = s.Y; hit.X2 = s.X2; hit.Y2 = s.Y2;
                    hit.Visibility = v.Visibility;
                }
                else
                {
                    Canvas.SetLeft(sh, s.X);
                    Canvas.SetTop(sh, s.Y);
                    sh.Width = Math.Max(1, s.W);
                    sh.Height = Math.Max(1, s.H);
                    var rc = sh as Rectangle;
                    if (rc != null)
                    {
                        rc.RadiusX = s.Kind == "roundrect" ? s.Radius : 0;
                        rc.RadiusY = rc.RadiusX;
                    }
                    if (s.IsMask)
                    {
                        // 遮罩画成灰虚线半透明块，不参与配色
                        sh.Fill = new SolidColorBrush(Color.FromArgb(36, 110, 115, 130));
                        sh.Stroke = BrushOf("#8a90a0");
                        sh.StrokeThickness = 1.5;
                        sh.StrokeDashArray = new DoubleCollection { 4, 3 };
                    }
                    else
                    {
                        sh.StrokeDashArray = null;
                        sh.Fill = s.NoFill ? Brushes.Transparent : (Brush)BrushOf(s.Fill);
                        sh.Stroke = s.StrokeW > 0 ? BrushOf(s.Stroke) : null;
                        sh.StrokeThickness = s.StrokeW;
                    }
                }
            }

            var k = el as InkElement;
            if (k != null)
            {
                var pl = (Polyline)v;
                pl.Points = new PointCollection(k.Points);
                pl.Stroke = BrushOf(k.Color);
                pl.StrokeThickness = Math.Max(1, k.Width);
                pl.Opacity = k.Opacity;
            }

            var im = el as ImageElement;
            if (im != null)
            {
                Canvas.SetLeft(v, im.X);
                Canvas.SetTop(v, im.Y);
                var img = (Image)v;
                img.Width = Math.Max(4, im.W);
                img.Height = Math.Max(4, im.H);
                img.Opacity = im.Opacity;
            }

            var tr = el as TextRegionElement;
            if (tr != null)
            {
                var bd = (Border)v;
                Canvas.SetLeft(bd, tr.X);
                Canvas.SetTop(bd, tr.Y);
                bd.Width = Math.Max(8, tr.W);
                bd.Height = Math.Max(8, tr.H);
                var tbk = bd.Child as TextBlock;
                if (tbk != null)
                {
                    tbk.Text = string.IsNullOrEmpty(tr.Text) ? "文字" : tr.Text;
                    tbk.FontSize = tr.FontSize;
                    tbk.FontWeight = tr.Bold ? FontWeights.Bold : FontWeights.Normal;
                    tbk.Foreground = BrushOf(tr.Color);
                    tbk.TextAlignment = tr.AlignH == "center" ? TextAlignment.Center : tr.AlignH == "right" ? TextAlignment.Right : TextAlignment.Left;
                    tbk.VerticalAlignment = tr.AlignV == "top" ? VerticalAlignment.Top : tr.AlignV == "bottom" ? VerticalAlignment.Bottom : VerticalAlignment.Center;
                    tbk.HorizontalAlignment = HorizontalAlignment.Stretch;
                }
            }

            if (ReferenceEquals(el, _sel)) ShowSelectionVisual();
        }

        internal void RebuildZ()
        {
            for (int i = 0; i < _elements.Count; i++)
            {
                UIElement v;
                if (_views.TryGetValue(_elements[i], out v)) Panel.SetZIndex(v, 10 + i * 2);
                var s = _elements[i] as ShapeElement;
                Line hit;
                if (s != null && _lineHits.TryGetValue(s, out hit)) Panel.SetZIndex(hit, 11 + i * 2);
            }
        }

        // ================= 遮罩 =================
        private static bool IsMaskTarget(CanvasElement el)
        {
            var s = el as ShapeElement;
            if (s != null) return !s.IsMask;
            return el is InkElement || el is ImageElement;
        }

        internal void ApplyMasks()
        {
            for (int i = 0; i < _elements.Count; i++)
            {
                var el = _elements[i];
                UIElement v;
                if (!_views.TryGetValue(el, out v)) continue;
                Geometry clip = null;
                if (i + 1 < _elements.Count && IsMaskTarget(el))
                {
                    var m = _elements[i + 1] as ShapeElement;
                    if (m != null && m.IsMask && m.Visible && m.Kind != "line")
                    {
                        // Clip 用元素自身坐标系：Canvas.Left/Top 定位的要平移
                        double tx = 0, ty = 0;
                        var s = el as ShapeElement;
                        if (s != null && s.Kind != "line") { tx = -s.X; ty = -s.Y; }
                        var im = el as ImageElement;
                        if (im != null) { tx = -im.X; ty = -im.Y; }
                        if (m.Kind == "ellipse")
                            clip = new EllipseGeometry(new Point(m.X + m.W / 2 + tx, m.Y + m.H / 2 + ty), m.W / 2, m.H / 2);
                        else
                            clip = new RectangleGeometry(new Rect(m.X + tx, m.Y + ty, Math.Max(1, m.W), Math.Max(1, m.H)),
                                m.Kind == "roundrect" ? m.Radius : 0,
                                m.Kind == "roundrect" ? m.Radius : 0);
                    }
                }
                var fe = v as FrameworkElement;
                if (fe != null) fe.Clip = clip;
            }
        }

        // ================= 图层面板 =================
        internal void RefreshLayers()
        {
            _syncingLayers = true;
            LayerList.Items.Clear();
            for (int i = _elements.Count - 1; i >= 0; i--)
            {
                var el = _elements[i];
                var elRef = el;
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                var chk = new CheckBox { IsChecked = el.Visible, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
                chk.Click += (s, e) =>
                {
                    PushUndo();
                    elRef.Visible = chk.IsChecked == true;
                    UpdateView(elRef);
                    ApplyMasks();
                    MarkDirty();
                };
                var se = el as ShapeElement;
                string suffix = se != null && se.IsMask ? " · 遮罩" : "";
                row.Children.Add(chk);
                row.Children.Add(new TextBlock { Text = ElementIcon(el) + " " + el.Name + suffix, VerticalAlignment = VerticalAlignment.Center });
                LayerList.Items.Add(new ListBoxItem { Content = row, Tag = el });
            }
            TxtEmptyHint.Visibility = (_elements.Count == 0 && !Prefs.GhostUnadded) ? Visibility.Visible : Visibility.Collapsed;
            foreach (ListBoxItem item in LayerList.Items)
                if (ReferenceEquals(item.Tag, _sel)) { LayerList.SelectedItem = item; break; }
            _syncingLayers = false;
            RefreshGhost();            // 添加/删除部件后，参考层要跟着增减
            ApplyComponentOpacity();
        }

        internal void MoveSelectedLayer(int delta)
        {
            if (_sel == null) return;
            int i = _elements.IndexOf(_sel);
            int j = i + delta;
            if (i < 0 || j < 0 || j >= _elements.Count) return;
            PushUndo();
            _elements.RemoveAt(i);
            _elements.Insert(j, _sel);
            RebuildZ();
            ApplyMasks();
            RefreshLayers();
            MarkDirty();
        }

        internal void MoveSelectedLayerTo(bool top)
        {
            if (_sel == null) return;
            PushUndo();
            if (!_elements.Remove(_sel)) return;
            if (top) _elements.Add(_sel);
            else _elements.Insert(0, _sel);
            RebuildZ();
            ApplyMasks();
            RefreshLayers();
            MarkDirty();
        }

        internal void ClearElements()
        {
            foreach (var el in _elements.ToList()) RemoveElement(el);
            _sel = null;
            HideShapeAdorner();
            HideRegionHighlight();
        }

        private void RemoveElement(CanvasElement el)
        {
            UIElement v;
            if (_views.TryGetValue(el, out v)) Stage.Children.Remove(v);
            _views.Remove(el);
            var s = el as ShapeElement;
            Line hit;
            if (s != null && _lineHits.TryGetValue(s, out hit))
            {
                Stage.Children.Remove(hit);
                _lineHits.Remove(s);
            }
            _elements.Remove(el);
        }

        internal void DeleteSelected()
        {
            var el = _sel;
            if (el == null) return;
            PushUndo();
            RemoveElement(el);
            ClearSelection();
            RebuildZ();
            ApplyMasks();
            RefreshLayers();
            MarkDirty();
            SetStatus("已删除「" + el.Name + "」。");
        }

        internal void NudgeSelected(double dx, double dy)
        {
            if (_sel != null) PushUndo("nudge:" + _sel.GetHashCode());
            var s = _sel as ShapeElement;
            if (s != null)
            {
                s.X += dx; s.Y += dy;
                if (s.Kind == "line") { s.X2 += dx; s.Y2 += dy; }
                UpdateView(s); ApplyMasks(); MarkDirty();
                return;
            }
            Rect box;
            if (TryGetBox(_sel, out box)) { SetBox(_sel, box.X + dx, box.Y + dy, box.Width, box.Height); UpdateView(_sel); ApplyMasks(); MarkDirty(); return; }
            var k = _sel as InkElement;
            if (k != null)
            {
                for (int i = 0; i < k.Points.Count; i++) k.Points[i] = new Point(k.Points[i].X + dx, k.Points[i].Y + dy);
                UpdateView(k); ApplyMasks(); MarkDirty();
            }
        }

        // ================= 选中 =================
        internal void SelectElement(CanvasElement el, bool syncLayers = true)
        {
            _sel = el;
            ShowPanelFor(el);
            ShowSelectionVisual();
            ApplyComponentOpacity();   // 选中的部件提亮一点
            if (syncLayers)
            {
                _syncingLayers = true;
                LayerList.SelectedItem = null;
                foreach (ListBoxItem item in LayerList.Items)
                    if (ReferenceEquals(item.Tag, el)) { LayerList.SelectedItem = item; break; }
                _syncingLayers = false;
            }
        }

        internal void ClearSelection()
        {
            _sel = null;
            HideShapeAdorner();
            HideRegionHighlight();
            ApplyComponentOpacity();   // 取消选中：部件回落到底图浓度
            _syncingLayers = true;
            LayerList.SelectedItem = null;
            _syncingLayers = false;
            PanelComp.Visibility = Visibility.Collapsed;
            PanelShape.Visibility = Visibility.Collapsed;
            PanelInk.Visibility = Visibility.Collapsed;
            PanelImage.Visibility = Visibility.Collapsed;
            PanelText.Visibility = Visibility.Collapsed;
            PanelTips.Visibility = Visibility.Visible;
        }

        private void ShowPanelFor(CanvasElement el)
        {
            PanelComp.Visibility = Visibility.Collapsed;
            PanelShape.Visibility = Visibility.Collapsed;
            PanelInk.Visibility = Visibility.Collapsed;
            PanelImage.Visibility = Visibility.Collapsed;
            PanelText.Visibility = Visibility.Collapsed;
            PanelTips.Visibility = Visibility.Collapsed;

            var ce = el as ComponentElement;
            if (ce != null)
            {
                BuildCompPanel(ce);
                PanelComp.Visibility = Visibility.Visible;
                SetStatus("正在调整「" + ce.Name + "」——改颜色立即生效；「恢复原版色」=和原版一样（不写进导出）。");
                return;
            }
            var s = el as ShapeElement;
            if (s != null) { SyncShapePanel(); PanelShape.Visibility = Visibility.Visible; return; }
            var k = el as InkElement;
            if (k != null) { SyncInkPanel(); PanelInk.Visibility = Visibility.Visible; return; }
            var im = el as ImageElement;
            if (im != null) { SyncImagePanel(); PanelImage.Visibility = Visibility.Visible; return; }
            var tr = el as TextRegionElement;
            if (tr != null) { SyncTextPanel(); PanelText.Visibility = Visibility.Visible; return; }
            PanelTips.Visibility = Visibility.Visible;
        }

        private void ShowSelectionVisual()
        {
            HideShapeAdorner();
            HideRegionHighlight();
            if (_sel == null) return;

            var ce = _sel as ComponentElement;
            if (ce != null)
            {
                var def = ComponentLib.Find(ce.CompId);
                if (def != null) ShowBoxHighlights(def.Boxes);
                return;
            }
            var s = _sel as ShapeElement;
            if (s != null && s.Kind == "line")
            {
                SetCenter(_ends[0], s.X, s.Y);
                SetCenter(_ends[1], s.X2, s.Y2);
                return;
            }
            var k = _sel as InkElement;
            if (k != null)
            {
                ShowOutline(CssBuilder.InkBBox(k), false);
                return;
            }
            Rect r;
            if (s != null) r = new Rect(s.X, s.Y, Math.Max(1, s.W), Math.Max(1, s.H));
            else if (TryGetBox(_sel, out r)) { }
            else return;
            ShowOutline(r, true);
        }

        // 盒状元素（图片 / 文本区）统一的取/设边框
        private static bool TryGetBox(CanvasElement el, out Rect r)
        {
            var im = el as ImageElement;
            if (im != null) { r = new Rect(im.X, im.Y, Math.Max(1, im.W), Math.Max(1, im.H)); return true; }
            var tr = el as TextRegionElement;
            if (tr != null) { r = new Rect(tr.X, tr.Y, Math.Max(1, tr.W), Math.Max(1, tr.H)); return true; }
            r = default(Rect); return false;
        }

        private static void SetBox(CanvasElement el, double x, double y, double w, double h)
        {
            var im = el as ImageElement;
            if (im != null) { im.X = x; im.Y = y; im.W = w; im.H = h; return; }
            var tr = el as TextRegionElement;
            if (tr != null) { tr.X = x; tr.Y = y; tr.W = w; tr.H = h; }
        }

        private void ShowOutline(Rect r, bool withHandles)
        {
            r.Inflate(3, 3);
            Canvas.SetLeft(_outline, r.X);
            Canvas.SetTop(_outline, r.Y);
            _outline.Width = r.Width;
            _outline.Height = r.Height;
            _outline.Visibility = Visibility.Visible;
            if (!withHandles) return;
            double cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
            SetCenter(_handles[0], r.Left, r.Top);
            SetCenter(_handles[1], cx, r.Top);
            SetCenter(_handles[2], r.Right, r.Top);
            SetCenter(_handles[3], r.Right, cy);
            SetCenter(_handles[4], r.Right, r.Bottom);
            SetCenter(_handles[5], cx, r.Bottom);
            SetCenter(_handles[6], r.Left, r.Bottom);
            SetCenter(_handles[7], r.Left, cy);
        }

        private static void SetCenter(Rectangle h, double cx, double cy)
        {
            Canvas.SetLeft(h, cx - h.Width / 2);
            Canvas.SetTop(h, cy - h.Height / 2);
            h.Visibility = Visibility.Visible;
        }

        internal void HideShapeAdorner()
        {
            if (_outline == null) return;
            _outline.Visibility = Visibility.Collapsed;
            foreach (var h in _handles) h.Visibility = Visibility.Collapsed;
            foreach (var h in _ends) h.Visibility = Visibility.Collapsed;
            HideGuides();
        }

        private void ShowBoxHighlights(Rect[] boxes)
        {
            foreach (var box in boxes)
            {
                var rc = new Rectangle
                {
                    Stroke = BrushOf("#7b6cf0"),
                    StrokeThickness = 1.6,
                    StrokeDashArray = new DoubleCollection { 5, 3 },
                    IsHitTestVisible = false,
                    Width = box.Width + 6,
                    Height = box.Height + 6
                };
                Canvas.SetLeft(rc, box.X - 3);
                Canvas.SetTop(rc, box.Y - 3);
                Overlay.Children.Add(rc);
                Panel.SetZIndex(rc, 998);
                _regionRects.Add(rc);
            }
        }

        internal void HideRegionHighlight()
        {
            foreach (var rc in _regionRects) Overlay.Children.Remove(rc);
            _regionRects.Clear();
        }

        // ================= 拖动 / 缩放 =================
        private void HookDrag(UIElement el)
        {
            el.MouseLeftButtonDown += ElDown;
            el.MouseMove += DragMove;
            el.MouseLeftButtonUp += DragUp;
        }

        private void StoreOrig(CanvasElement el)
        {
            var s = el as ShapeElement;
            if (s != null) { _oX = s.X; _oY = s.Y; _oW = s.W; _oH = s.H; _oX2 = s.X2; _oY2 = s.Y2; return; }
            Rect bx;
            if (TryGetBox(el, out bx)) { _oX = bx.X; _oY = bx.Y; _oW = bx.Width; _oH = bx.Height; return; }
            var k = el as InkElement;
            if (k != null)
            {
                _inkOrig = new List<Point>(k.Points);
                var r = CssBuilder.InkBBox(k);
                _oX = r.X; _oY = r.Y; _oW = r.Width; _oH = r.Height;
            }
        }

        private void ElDown(object sender, MouseButtonEventArgs e)
        {
            var el = ((FrameworkElement)sender).Tag as CanvasElement;
            if (el == null) return;
            SelectElement(el);
            _moving = true;
            _handle = -1;
            _down = e.GetPosition(Stage);
            StoreOrig(el);
            _dragUndoPending = true;
            ((UIElement)sender).CaptureMouse();
            Focus();   // 让 Delete / 方向键立即可用
            e.Handled = true;
        }

        private void HandleDown(object sender, MouseButtonEventArgs e)
        {
            if (_sel == null) return;
            _handle = (int)((FrameworkElement)sender).Tag;
            _moving = false;
            _down = e.GetPosition(Stage);
            StoreOrig(_sel);
            _dragUndoPending = true;
            ((UIElement)sender).CaptureMouse();
            e.Handled = true;
        }

        private void DragMove(object sender, MouseEventArgs e)
        {
            if (!_moving && _handle < 0) return;
            if (e.LeftButton != MouseButtonState.Pressed) return;
            if (_sel == null) return;

            if (_dragUndoPending) { PushUndo(); _dragUndoPending = false; }   // 真动了才记一步撤销
            var p = e.GetPosition(Stage);
            double dx = p.X - _down.X, dy = p.Y - _down.Y;
            var cands = SnapCands(_sel);
            double gx = double.NaN, gy = double.NaN;

            var s = _sel as ShapeElement;
            bool box = TryGetBox(_sel, out _);   // 图片 / 文本区
            var k = _sel as InkElement;

            if (_moving)
            {
                if (s != null && s.Kind == "line")
                {
                    double nx = _oX + dx, ny = _oY + dy, nx2 = _oX2 + dx, ny2 = _oY2 + dy;
                    double ddx = SnapDelta(new[] { nx, nx2, (nx + nx2) / 2 }, cands.xs, ref gx);
                    double ddy = SnapDelta(new[] { ny, ny2, (ny + ny2) / 2 }, cands.ys, ref gy);
                    s.X = nx + ddx; s.Y = ny + ddy; s.X2 = nx2 + ddx; s.Y2 = ny2 + ddy;
                }
                else if (s != null || box)
                {
                    double w = s != null ? s.W : _oW, h = s != null ? s.H : _oH;
                    double nx = _oX + dx, ny = _oY + dy;
                    nx += SnapDelta(new[] { nx, nx + w / 2, nx + w }, cands.xs, ref gx);
                    ny += SnapDelta(new[] { ny, ny + h / 2, ny + h }, cands.ys, ref gy);
                    if (s != null) { s.X = nx; s.Y = ny; }
                    else SetBox(_sel, nx, ny, w, h);
                }
                else if (k != null && _inkOrig != null)
                {
                    double nx = _oX + dx, ny = _oY + dy;
                    double ddx = SnapDelta(new[] { nx, nx + _oW / 2, nx + _oW }, cands.xs, ref gx);
                    double ddy = SnapDelta(new[] { ny, ny + _oH / 2, ny + _oH }, cands.ys, ref gy);
                    double tx = dx + ddx, ty = dy + ddy;
                    for (int i = 0; i < k.Points.Count && i < _inkOrig.Count; i++)
                        k.Points[i] = new Point(_inkOrig[i].X + tx, _inkOrig[i].Y + ty);
                }
            }
            else if (_handle >= 100 && s != null)
            {
                double nx = (_handle == 100 ? _oX : _oX2) + dx;
                double ny = (_handle == 100 ? _oY : _oY2) + dy;
                nx += SnapDelta(new[] { nx }, cands.xs, ref gx);
                ny += SnapDelta(new[] { ny }, cands.ys, ref gy);
                if (_handle == 100) { s.X = nx; s.Y = ny; }
                else { s.X2 = nx; s.Y2 = ny; }
            }
            else if (s != null || box)
            {
                bool left = _handle == 0 || _handle == 6 || _handle == 7;
                bool right = _handle == 2 || _handle == 3 || _handle == 4;
                bool top = _handle == 0 || _handle == 1 || _handle == 2;
                bool bottom = _handle == 4 || _handle == 5 || _handle == 6;

                double l = _oX, t = _oY, r = _oX + _oW, b2 = _oY + _oH;
                if (left) { l = _oX + dx; l += SnapDelta(new[] { l }, cands.xs, ref gx); if (r - l < 10) l = r - 10; }
                if (right) { r = _oX + _oW + dx; r += SnapDelta(new[] { r }, cands.xs, ref gx); if (r - l < 10) r = l + 10; }
                if (top) { t = _oY + dy; t += SnapDelta(new[] { t }, cands.ys, ref gy); if (b2 - t < 10) t = b2 - 10; }
                if (bottom) { b2 = _oY + _oH + dy; b2 += SnapDelta(new[] { b2 }, cands.ys, ref gy); if (b2 - t < 10) b2 = t + 10; }
                if (s != null) { s.X = l; s.Y = t; s.W = r - l; s.H = b2 - t; }
                else SetBox(_sel, l, t, r - l, b2 - t);
                if (_lockSquare && s != null && s.Kind != "line") { double mm = Math.Max(s.W, s.H); s.W = mm; s.H = mm; }
            }

            ShowGuides(gx, gy);
            UpdateView(_sel);
            ApplyMasks();   // 遮罩或被裁层移动时实时更新裁剪
        }

        private void DragUp(object sender, MouseButtonEventArgs e)
        {
            if (!_moving && _handle < 0) return;
            ((UIElement)sender).ReleaseMouseCapture();
            _moving = false;
            _handle = -1;
            HideGuides();
            MarkDirty();
            if (_sel is ShapeElement && PanelShape.Visibility == Visibility.Visible) SyncShapePanel();   // 拖完更新尺寸数值
            e.Handled = true;
        }

        // ================= 吸附 =================
        private (List<double> xs, List<double> ys) SnapCands(CanvasElement except)
        {
            var xs = new List<double> { 0, 600, 1200 };
            var ys = new List<double> { 0, 350, 700 };
            // 设计框的左/中/右、上/中/下（水平和垂直都能吸附）
            var f = ActiveTarget.FrameRect;
            xs.Add(f.X); xs.Add(f.X + f.Width / 2); xs.Add(f.Right);
            ys.Add(f.Y); ys.Add(f.Y + f.Height / 2); ys.Add(f.Bottom);
            foreach (var el in _elements)
            {
                if (ReferenceEquals(el, except) || !el.Visible) continue;
                var ce = el as ComponentElement;
                if (ce != null)
                {
                    var def = ComponentLib.Find(ce.CompId);
                    if (def != null)
                        foreach (var b in def.Boxes)
                        {
                            xs.Add(b.X); xs.Add(b.X + b.Width / 2); xs.Add(b.Right);
                            ys.Add(b.Y); ys.Add(b.Y + b.Height / 2); ys.Add(b.Bottom);
                        }
                    continue;
                }
                var r = CssBuilder.BBoxOfElement(el);
                xs.Add(r.X); xs.Add(r.X + r.Width / 2); xs.Add(r.Right);
                ys.Add(r.Y); ys.Add(r.Y + r.Height / 2); ys.Add(r.Bottom);
            }
            return (xs, ys);
        }

        private static double SnapDelta(IEnumerable<double> edges, List<double> cands, ref double guide)
        {
            double best = double.NaN;
            double g = guide;
            foreach (var e in edges)
            {
                foreach (var c in cands)
                {
                    double d = c - e;
                    if (Math.Abs(d) <= SnapTol && (double.IsNaN(best) || Math.Abs(d) < Math.Abs(best)))
                    {
                        best = d; g = c;
                    }
                }
            }
            if (double.IsNaN(best)) return 0;
            guide = g;
            return best;
        }

        private void ShowGuides(double gx, double gy)
        {
            if (!double.IsNaN(gx)) { _vGuide.X1 = _vGuide.X2 = gx; _vGuide.Visibility = Visibility.Visible; }
            else _vGuide.Visibility = Visibility.Collapsed;
            if (!double.IsNaN(gy)) { _hGuide.Y1 = _hGuide.Y2 = gy; _hGuide.Visibility = Visibility.Visible; }
            else _hGuide.Visibility = Visibility.Collapsed;
        }

        private void HideGuides()
        {
            _vGuide.Visibility = Visibility.Collapsed;
            _hGuide.Visibility = Visibility.Collapsed;
        }

        // ================= 画笔 =================
        private void WirePen()
        {
            BtnPen.Checked += (s, e) =>
            {
                RbViewCanvas.IsChecked = true;
                Pen.Visibility = Visibility.Visible;
                PanelPen.Visibility = Visibility.Visible;
                UpdatePenAttrs();
                SetStatus("画笔模式：直接在画布上画，松手一笔=一个图层。再点一次「画笔」退出。");
            };
            BtnPen.Unchecked += (s, e) =>
            {
                Pen.Visibility = Visibility.Collapsed;
                PanelPen.Visibility = Visibility.Collapsed;
            };
            TxtPen.TextChanged += (s, e) =>
            {
                string n = ColorUtil.NormalizeHex(TxtPen.Text);
                if (n != null) SetSwatch(SwPen, n);
                UpdatePenAttrs();
            };
            BtnPenPick.Click += (s, e) => PickColor(TxtPen);
            SldPenW.ValueChanged += (s, e) =>
            {
                if (LblPenW != null) LblPenW.Text = "画笔粗细：" + Math.Round(SldPenW.Value, 1).ToString("0.#");
                UpdatePenAttrs();
            };
            Pen.StrokeCollected += OnStrokeCollected;
            SetSwatch(SwPen, "#5b8def");
        }

        private void UpdatePenAttrs()
        {
            if (Pen == null) return;
            string hex = ColorUtil.NormalizeHex(TxtPen.Text) ?? "#5b8def";
            var c = ColorUtil.ParseHex(hex).Value;
            Pen.DefaultDrawingAttributes = new DrawingAttributes
            {
                Color = Color.FromRgb(c.R, c.G, c.B),
                Width = SldPenW.Value,
                Height = SldPenW.Value,
                FitToCurve = true
            };
        }

        private void OnStrokeCollected(object sender, InkCanvasStrokeCollectedEventArgs e)
        {
            var pts = new List<Point>();
            var last = new Point(double.MinValue, double.MinValue);
            foreach (var sp in e.Stroke.StylusPoints)
            {
                var p = new Point(sp.X, sp.Y);
                if (pts.Count == 0 || Math.Abs(p.X - last.X) + Math.Abs(p.Y - last.Y) >= 1.5)
                {
                    pts.Add(p);
                    last = p;
                }
            }
            Pen.Strokes.Remove(e.Stroke);
            if (pts.Count < 2) return;
            var k = new InkElement
            {
                Name = "手绘 " + (++_inkCounter),
                Points = pts,
                Color = ColorUtil.NormalizeHex(TxtPen.Text) ?? "#5b8def",
                Width = Math.Round(SldPenW.Value, 1)
            };
            AddElement(k);
        }

        // ================= 图形属性面板 =================
        private void WireShapePanel()
        {
            LayerList.SelectionChanged += (s, e) =>
            {
                if (_syncingLayers) return;
                var it = LayerList.SelectedItem as ListBoxItem;
                var el = it != null ? it.Tag as CanvasElement : null;
                if (el != null) SelectElement(el, false);
            };

            TxtFill.TextChanged += (s, e) =>
            {
                var sh = _sel as ShapeElement;
                if (_syncingShape || sh == null) return;
                string n = ColorUtil.NormalizeHex(TxtFill.Text);
                if (n == null) return;
                PushUndo("fill:" + sh.GetHashCode());
                sh.Fill = n;
                SetSwatch(SwFill, n);
                UpdateView(sh);
                MarkDirty();
            };
            TxtStroke.TextChanged += (s, e) =>
            {
                var sh = _sel as ShapeElement;
                if (_syncingShape || sh == null) return;
                string n = ColorUtil.NormalizeHex(TxtStroke.Text);
                if (n == null) return;
                PushUndo("stroke:" + sh.GetHashCode());
                sh.Stroke = n;
                SetSwatch(SwStroke, n);
                UpdateView(sh);
                MarkDirty();
            };
            ChkNoFill.Click += (s, e) =>
            {
                var sh = _sel as ShapeElement;
                if (_syncingShape || sh == null) return;
                PushUndo();
                sh.NoFill = ChkNoFill.IsChecked == true;
                if (sh.NoFill && sh.StrokeW <= 0)
                {
                    sh.StrokeW = 2;   // 全透明会看不见，自动补个边框
                    SyncShapePanel();
                }
                UpdateView(sh);
                MarkDirty();
            };
            SldStrokeW.ValueChanged += (s, e) =>
            {
                var sh = _sel as ShapeElement;
                if (_syncingShape || sh == null) return;
                PushUndo("strokew:" + sh.GetHashCode());
                sh.StrokeW = Math.Round(SldStrokeW.Value, 1);
                LblStrokeW.Text = (sh.Kind == "line" ? "线条粗细：" : "边框粗细：") + sh.StrokeW.ToString("0.#");
                UpdateView(sh);
                MarkDirty();
            };
            SldRadius.ValueChanged += (s, e) =>
            {
                var sh = _sel as ShapeElement;
                if (_syncingShape || sh == null) return;
                PushUndo("radius:" + sh.GetHashCode());
                sh.Radius = Math.Round(SldRadius.Value);
                LblRadius.Text = "圆角：" + sh.Radius.ToString("0");
                UpdateView(sh);
                ApplyMasks();
                MarkDirty();
            };
            SldOpacity.ValueChanged += (s, e) =>
            {
                var sh = _sel as ShapeElement;
                if (_syncingShape || sh == null) return;
                PushUndo("opacity:" + sh.GetHashCode());
                sh.Opacity = Math.Round(SldOpacity.Value) / 100.0;
                LblOpacity.Text = "透明度：" + Math.Round(SldOpacity.Value) + "%";
                UpdateView(sh);
                MarkDirty();
            };
            ChkMask.Click += (s, e) =>
            {
                var sh = _sel as ShapeElement;
                if (_syncingShape || sh == null || sh.Kind == "line") return;
                PushUndo();
                sh.IsMask = ChkMask.IsChecked == true;
                UpdateView(sh);
                ApplyMasks();
                RefreshLayers();
                MarkDirty();
                SetStatus(sh.IsMask
                    ? "已设为遮罩：它把图层里在它正下方的那一层裁剪成自己的形状（拖动遮罩试试）。"
                    : "已取消遮罩，恢复为普通图形。");
            };
            BtnFillPick.Click += (s, e) => PickColor(TxtFill);
            BtnStrokePick.Click += (s, e) => PickColor(TxtStroke);
            BtnDelShape.Click += (s, e) => DeleteSelected();
            BtnMaskShape.Click += (s, e) => AddMaskOverSelected();

            // 参数化尺寸（相对设计框的百分比）——回车或失焦生效
            System.Windows.Input.KeyEventHandler onEnter = (s, e) => { if (e.Key == Key.Enter) ApplySizeFields(); };
            TxtSizeW.KeyDown += onEnter; TxtSizeH.KeyDown += onEnter; TxtSizeX.KeyDown += onEnter; TxtSizeY.KeyDown += onEnter;
            TxtSizeW.LostFocus += (s, e) => ApplySizeFields();
            TxtSizeH.LostFocus += (s, e) => ApplySizeFields();
            TxtSizeX.LostFocus += (s, e) => ApplySizeFields();
            TxtSizeY.LostFocus += (s, e) => ApplySizeFields();
            ChkSquare.Click += (s, e) =>
            {
                _lockSquare = ChkSquare.IsChecked == true;
                var sh = _sel as ShapeElement;
                if (_lockSquare && sh != null && sh.Kind != "line")
                {
                    PushUndo();
                    double m = Math.Max(sh.W, sh.H);
                    sh.W = m; sh.H = m;
                    UpdateView(sh); ApplyMasks(); SyncShapePanel(); MarkDirty();
                }
            };
        }

        private void ApplySizeFields()
        {
            var sh = _sel as ShapeElement;
            if (_syncingShape || sh == null || sh.Kind == "line") return;
            var f = ActiveTarget.FrameRect;
            double pw, ph, px, py;
            if (!double.TryParse(TxtSizeW.Text, out pw)) pw = sh.W / f.Width * 100;
            if (!double.TryParse(TxtSizeH.Text, out ph)) ph = sh.H / f.Height * 100;
            if (!double.TryParse(TxtSizeX.Text, out px)) px = (sh.X - f.X) / f.Width * 100;
            if (!double.TryParse(TxtSizeY.Text, out py)) py = (sh.Y - f.Y) / f.Height * 100;
            PushUndo();
            sh.W = Math.Max(1, pw / 100.0 * f.Width);
            sh.H = Math.Max(1, ph / 100.0 * f.Height);
            if (_lockSquare) { double m = Math.Max(sh.W, sh.H); sh.W = m; sh.H = m; }
            sh.X = f.X + px / 100.0 * f.Width;
            sh.Y = f.Y + py / 100.0 * f.Height;
            UpdateView(sh); ApplyMasks(); SyncShapePanel(); MarkDirty();
        }

        private void SyncShapePanel()
        {
            var s = _sel as ShapeElement;
            if (s == null) return;
            _syncingShape = true;
            ShapeTitle.Text = "图形：" + s.Name;
            RowFill.Visibility = s.Kind == "line" ? Visibility.Collapsed : Visibility.Visible;
            RowRadius.Visibility = s.Kind == "roundrect" ? Visibility.Visible : Visibility.Collapsed;
            RowSize.Visibility = s.Kind == "line" ? Visibility.Collapsed : Visibility.Visible;
            ChkMask.Visibility = s.Kind == "line" ? Visibility.Collapsed : Visibility.Visible;
            if (s.Kind != "line")
            {
                var f = ActiveTarget.FrameRect;
                TxtSizeW.Text = Math.Round(s.W / f.Width * 100, 1).ToString("0.#");
                TxtSizeH.Text = Math.Round(s.H / f.Height * 100, 1).ToString("0.#");
                TxtSizeX.Text = Math.Round((s.X - f.X) / f.Width * 100, 1).ToString("0.#");
                TxtSizeY.Text = Math.Round((s.Y - f.Y) / f.Height * 100, 1).ToString("0.#");
                ChkSquare.IsChecked = _lockSquare;
            }
            TxtFill.Text = s.Fill;
            SetSwatch(SwFill, s.Fill);
            ChkNoFill.IsChecked = s.NoFill;
            TxtStroke.Text = s.Stroke;
            SetSwatch(SwStroke, s.Stroke);
            SldStrokeW.Value = s.StrokeW;
            LblStrokeW.Text = (s.Kind == "line" ? "线条粗细：" : "边框粗细：") + s.StrokeW.ToString("0.#");
            SldRadius.Value = s.Radius;
            LblRadius.Text = "圆角：" + s.Radius.ToString("0");
            SldOpacity.Value = Math.Round(s.Opacity * 100);
            LblOpacity.Text = "透明度：" + Math.Round(s.Opacity * 100) + "%";
            ChkMask.IsChecked = s.IsMask;
            _syncingShape = false;
        }

        // ================= 手绘 / 图片属性面板 =================
        private void WireInkImagePanels()
        {
            TxtInk.TextChanged += (s, e) =>
            {
                var k = _sel as InkElement;
                if (_syncingShape || k == null) return;
                string n = ColorUtil.NormalizeHex(TxtInk.Text);
                if (n == null) return;
                PushUndo("inkcolor:" + k.GetHashCode());
                k.Color = n;
                SetSwatch(SwInk, n);
                UpdateView(k);
                MarkDirty();
            };
            BtnInkPick.Click += (s, e) => PickColor(TxtInk);
            SldInkW.ValueChanged += (s, e) =>
            {
                var k = _sel as InkElement;
                if (_syncingShape || k == null) return;
                PushUndo("inkw:" + k.GetHashCode());
                k.Width = Math.Round(SldInkW.Value, 1);
                LblInkW.Text = "粗细：" + k.Width.ToString("0.#");
                UpdateView(k);
                MarkDirty();
            };
            SldInkOpacity.ValueChanged += (s, e) =>
            {
                var k = _sel as InkElement;
                if (_syncingShape || k == null) return;
                PushUndo("inkop:" + k.GetHashCode());
                k.Opacity = Math.Round(SldInkOpacity.Value) / 100.0;
                LblInkOpacity.Text = "透明度：" + Math.Round(SldInkOpacity.Value) + "%";
                UpdateView(k);
                MarkDirty();
            };
            BtnDelInk.Click += (s, e) => DeleteSelected();
            BtnMaskInk.Click += (s, e) => AddMaskOverSelected();

            SldImgOpacity.ValueChanged += (s, e) =>
            {
                var im = _sel as ImageElement;
                if (_syncingShape || im == null) return;
                PushUndo("imgop:" + im.GetHashCode());
                im.Opacity = Math.Round(SldImgOpacity.Value) / 100.0;
                LblImgOpacity.Text = "透明度：" + Math.Round(SldImgOpacity.Value) + "%";
                UpdateView(im);
                MarkDirty();
            };
            BtnDelImg.Click += (s, e) => DeleteSelected();
            BtnMaskImg.Click += (s, e) => AddMaskOverSelected();
        }

        private void SyncInkPanel()
        {
            var k = _sel as InkElement;
            if (k == null) return;
            _syncingShape = true;
            InkTitle.Text = "手绘：" + k.Name;
            TxtInk.Text = k.Color;
            SetSwatch(SwInk, k.Color);
            SldInkW.Value = k.Width;
            LblInkW.Text = "粗细：" + k.Width.ToString("0.#");
            SldInkOpacity.Value = Math.Round(k.Opacity * 100);
            LblInkOpacity.Text = "透明度：" + Math.Round(k.Opacity * 100) + "%";
            _syncingShape = false;
        }

        private void SyncImagePanel()
        {
            var im = _sel as ImageElement;
            if (im == null) return;
            _syncingShape = true;
            ImgTitle.Text = "图片：" + im.Name;
            SldImgOpacity.Value = Math.Round(im.Opacity * 100);
            LblImgOpacity.Text = "透明度：" + Math.Round(im.Opacity * 100) + "%";
            _syncingShape = false;
        }
    }
}
