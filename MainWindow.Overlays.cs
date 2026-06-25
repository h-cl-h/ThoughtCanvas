using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ShapePath = System.Windows.Shapes.Path;

namespace ThoughtCanvas
{
    // 第二梯队叠加层：外框 / 概要 / 标注 / 联系 / 聚焦（与网页版 dumpExtras 同结构，进 .bmap）
    public partial class MainWindow
    {
        (string type, string id)? selOverlay;
        readonly List<UIElement> overlayVisuals = new List<UIElement>();
        static long _oseq;
        static string NewOid(string p) => p + System.Threading.Interlocked.Increment(ref _oseq);

        static readonly Brush BoundaryFill = new BrushConverter().ConvertFromString("#225B6CF0") as Brush;

        // 成员包围盒（仅算已渲染的卡片）
        bool MemberBox(IEnumerable<string> ids, out double x1, out double y1, out double x2, out double y2)
        {
            x1 = 1e9; y1 = 1e9; x2 = -1e9; y2 = -1e9; bool any = false;
            foreach (var id in ids)
                if (byId.TryGetValue(id, out var t) && cardOf.ContainsKey(t))
                {
                    any = true;
                    x1 = Math.Min(x1, t.LX); y1 = Math.Min(y1, t.CY - t.H / 2);
                    x2 = Math.Max(x2, t.LX + t.W); y2 = Math.Max(y2, t.CY + t.H / 2);
                }
            return any;
        }

        void AddVisual(UIElement e, int z) { Panel.SetZIndex(e, z); world.Children.Add(e); overlayVisuals.Add(e); }

        void DrawOverlays()
        {
            foreach (var v in overlayVisuals) world.Children.Remove(v);
            overlayVisuals.Clear();
            foreach (var b in doc.Boundaries) DrawBoundary(b);
            foreach (var s in doc.Summaries)  DrawSummary(s);
            foreach (var c in doc.Callouts)   DrawCallout(c);
            foreach (var r in doc.Relations)  DrawRelation(r);
            DrawLinks();   // 蜘蛛网连线
        }

        // ---------- 外框 ----------
        void DrawBoundary(Boundary b)
        {
            if (!MemberBox(b.Members, out var x1, out var y1, out var x2, out var y2)) return;
            const double pad = 14;
            bool sel = selOverlay?.type == "bd" && selOverlay?.id == b.Id;
            Brush stroke = string.IsNullOrEmpty(b.Color) ? Accent : (Brush)new BrushConverter().ConvertFromString(b.Color);
            var rect = new Border
            {
                Width = (x2 - x1) + pad * 2, Height = (y2 - y1) + pad * 2,
                CornerRadius = new CornerRadius(16),
                Background = BoundaryFill,
                BorderBrush = stroke, BorderThickness = new Thickness(sel ? 2.4 : 1.6),
                Cursor = Cursors.Hand, Tag = b
            };
            rect.MouseLeftButtonDown += (s, e) => { e.Handled = true; selOverlay = ("bd", b.Id); Relayout(); };
            rect.ContextMenu = OverlayMenu("bd", b.Id);
            Canvas.SetLeft(rect, x1 - pad); Canvas.SetTop(rect, y1 - pad);
            AddVisual(rect, -10);
            if (!string.IsNullOrWhiteSpace(b.Label))
            {
                var lab = new Border
                {
                    Background = stroke, CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 2, 8, 2),
                    Child = new TextBlock { Text = b.Label, Foreground = Brushes.White, FontSize = 12 }
                };
                Canvas.SetLeft(lab, x1 - pad + 6); Canvas.SetTop(lab, y1 - pad - 12);
                AddVisual(lab, -9);
            }
        }

        // ---------- 概要：成员右侧一个大括号 + 概要文本节点 ----------
        void DrawSummary(Summary sm)
        {
            if (!MemberBox(sm.Members, out var x1, out var y1, out var x2, out var y2)) return;
            double bx = x2 + 16, tip = bx + 10, mid = (y1 + y2) / 2;
            double r = Math.Min(12, (y2 - y1) / 4);
            if (!Fin(bx) || !Fin(y1) || !Fin(y2)) return;
            string d = $"M {F(bx)} {F(y1)} Q {F(tip)} {F(y1)} {F(tip)} {F(y1 + r)} L {F(tip)} {F(mid - r)} " +
                       $"Q {F(tip)} {F(mid)} {F(tip + 8)} {F(mid)} Q {F(tip)} {F(mid)} {F(tip)} {F(mid + r)} " +
                       $"L {F(tip)} {F(y2 - r)} Q {F(tip)} {F(y2)} {F(bx)} {F(y2)}";
            try
            {
                var path = new ShapePath { Data = Geometry.Parse(d), Stroke = Accent, StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round };
                AddVisual(path, 0);
            }
            catch { }

            bool sel = selOverlay?.type == "sm" && selOverlay?.id == sm.Id;
            var node = new Border
            {
                Background = AccentSoft, CornerRadius = new CornerRadius(10),
                BorderBrush = sel ? Accent : Accent, BorderThickness = new Thickness(sel ? 2 : 1),
                Padding = new Thickness(12, 7, 12, 7), MaxWidth = 200, Cursor = Cursors.Hand, Tag = sm,
                Child = new TextBlock { Text = string.IsNullOrWhiteSpace(sm.Text) ? "概要" : sm.Text, Foreground = Ink, FontSize = 13, TextWrapping = TextWrapping.Wrap }
            };
            node.MouseLeftButtonDown += (s, e) => { e.Handled = true; selOverlay = ("sm", sm.Id); Relayout(); };
            node.MouseRightButtonUp += (s, e) => { };
            node.ContextMenu = OverlayMenu("sm", sm.Id);
            node.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(node, tip + 16); Canvas.SetTop(node, mid - node.DesiredSize.Height / 2);
            AddVisual(node, 1);
        }

        // ---------- 标注：附在某节点旁的小气泡 ----------
        void DrawCallout(Callout c)
        {
            if (!byId.TryGetValue(c.Tb, out var t) || !cardOf.ContainsKey(t)) return;
            double nx = t.LX + t.W, ny = t.CY;
            double bx = nx + 26, by = ny - 46;
            bool sel = selOverlay?.type == "co" && selOverlay?.id == c.Id;
            Brush fill = string.IsNullOrEmpty(c.Color) ? (Brush)new BrushConverter().ConvertFromString("#FFF3D6") : (Brush)new BrushConverter().ConvertFromString(c.Color);
            // 连接线
            try
            {
                var tail = new ShapePath { Data = Geometry.Parse($"M {F(nx)} {F(ny)} L {F(bx + 8)} {F(by + 18)}"), Stroke = new BrushConverter().ConvertFromString("#D9B85A") as Brush, StrokeThickness = 1.6 };
                AddVisual(tail, 0);
            }
            catch { }
            var bubble = new Border
            {
                Background = fill, CornerRadius = new CornerRadius(10),
                BorderBrush = sel ? Accent : (Brush)new BrushConverter().ConvertFromString("#E6C766"),
                BorderThickness = new Thickness(sel ? 2 : 1),
                Padding = new Thickness(10, 6, 10, 6), MaxWidth = 200, Cursor = Cursors.Hand, Tag = c,
                Child = new TextBlock { Text = string.IsNullOrWhiteSpace(c.Text) ? "标注" : c.Text, Foreground = Ink, FontSize = 12.5, TextWrapping = TextWrapping.Wrap }
            };
            bubble.MouseLeftButtonDown += (s, e) => { e.Handled = true; selOverlay = ("co", c.Id); Relayout(); };
            bubble.ContextMenu = OverlayMenu("co", c.Id);
            Canvas.SetLeft(bubble, bx); Canvas.SetTop(bubble, by);
            AddVisual(bubble, 2);
        }

        // ---------- 联系：两节点间的曲线箭头 ----------
        void DrawRelation(Relation rl)
        {
            if (!byId.TryGetValue(rl.A, out var a) || !byId.TryGetValue(rl.B, out var b)) return;
            if (!cardOf.ContainsKey(a) || !cardOf.ContainsKey(b)) return;
            double ax = a.LX + a.W / 2, ay = a.CY, bx = b.LX + b.W / 2, by = b.CY;
            // 从各自最近的左右边缘出发
            double ax2 = bx >= ax ? a.LX + a.W : a.LX;
            double bx2 = bx >= ax ? b.LX : b.LX + b.W;
            double mx = (ax2 + bx2) / 2, my = (ay + by) / 2 - 50;   // 上凸
            bool sel = selOverlay?.type == "rl" && selOverlay?.id == rl.Id;
            try
            {
                var geo = Geometry.Parse($"M {F(ax2)} {F(ay)} Q {F(mx)} {F(my)} {F(bx2)} {F(by)}");
                var path = new ShapePath
                {
                    Data = geo, Stroke = sel ? Accent : (Brush)new BrushConverter().ConvertFromString("#E0719A"),
                    StrokeThickness = sel ? 2.6 : 2, StrokeDashArray = new DoubleCollection { 5, 3 },
                    Cursor = Cursors.Hand, Tag = rl
                };
                path.MouseLeftButtonDown += (s, e) => { e.Handled = true; selOverlay = ("rl", rl.Id); Relayout(); };
                path.ContextMenu = OverlayMenu("rl", rl.Id);
                AddVisual(path, 0);
                // 箭头
                double dx = bx2 - mx, dy = by - my, len = Math.Sqrt(dx * dx + dy * dy);
                if (len > 0.1 && Fin(len))
                {
                    dx /= len; dy /= len;
                    double s1x = bx2 - dx * 11 - dy * 6, s1y = by - dy * 11 + dx * 6;
                    double s2x = bx2 - dx * 11 + dy * 6, s2y = by - dy * 11 - dx * 6;
                    var head = new ShapePath { Data = Geometry.Parse($"M {F(bx2)} {F(by)} L {F(s1x)} {F(s1y)} M {F(bx2)} {F(by)} L {F(s2x)} {F(s2y)}"), Stroke = path.Stroke, StrokeThickness = 2 };
                    AddVisual(head, 0);
                }
                if (!string.IsNullOrWhiteSpace(rl.Text))
                {
                    var lab = new Border
                    {
                        Background = Brushes.White, CornerRadius = new CornerRadius(7), Padding = new Thickness(6, 1, 6, 1),
                        BorderBrush = (Brush)new BrushConverter().ConvertFromString("#E0719A"), BorderThickness = new Thickness(1),
                        Child = new TextBlock { Text = rl.Text, FontSize = 11, Foreground = Ink }
                    };
                    lab.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    Canvas.SetLeft(lab, mx - lab.DesiredSize.Width / 2); Canvas.SetTop(lab, my + 8);
                    AddVisual(lab, 1);
                }
            }
            catch { }
        }

        ContextMenu OverlayMenu(string type, string id)
        {
            var m = new ContextMenu();
            MenuItem MI(string h, Action a) { var mi = new MenuItem { Header = h }; mi.Click += (s, e) => a(); return mi; }
            if (type == "bd")
            {
                m.Items.Add(MI("编辑标签…", () => { var b = doc.Boundaries.FirstOrDefault(x => x.Id == id); if (b == null) return; var r = PromptText("外框标签", "外框标签：", b.Label, false); if (r != null) { PushUndo(); b.Label = r.Trim(); Relayout(); } }));
                m.Items.Add(MI("删除外框", () => { PushUndo(); doc.Boundaries.RemoveAll(x => x.Id == id); selOverlay = null; Relayout(); }));
            }
            else if (type == "sm")
            {
                m.Items.Add(MI("编辑概要文字…", () => { var s = doc.Summaries.FirstOrDefault(x => x.Id == id); if (s == null) return; var r = PromptText("概要", "概要文字：", s.Text, false); if (r != null) { PushUndo(); s.Text = r.Trim(); Relayout(); } }));
                m.Items.Add(MI("删除概要", () => { PushUndo(); doc.Summaries.RemoveAll(x => x.Id == id); selOverlay = null; Relayout(); }));
            }
            else if (type == "co")
            {
                m.Items.Add(MI("编辑标注…", () => { var c = doc.Callouts.FirstOrDefault(x => x.Id == id); if (c == null) return; var r = PromptText("标注", "标注文字：", c.Text, true); if (r != null) { PushUndo(); c.Text = r.Trim(); Relayout(); } }));
                m.Items.Add(MI("删除标注", () => { PushUndo(); doc.Callouts.RemoveAll(x => x.Id == id); selOverlay = null; Relayout(); }));
            }
            else if (type == "rl")
            {
                m.Items.Add(MI("编辑联系文字…", () => { var rl = doc.Relations.FirstOrDefault(x => x.Id == id); if (rl == null) return; var r = PromptText("联系", "联系文字：", rl.Text, false); if (r != null) { PushUndo(); rl.Text = r.Trim(); Relayout(); } }));
                m.Items.Add(MI("删除联系", () => { PushUndo(); doc.Relations.RemoveAll(x => x.Id == id); selOverlay = null; Relayout(); }));
            }
            return m;
        }

        // ========== 创建叠加层（从右键菜单/快捷调用） ==========
        void AddBoundary()
        {
            var ids = selSet.Select(t => t.Id).ToList();
            if (ids.Count == 0) { MessageBox.Show("先选中一个或多个主题（Ctrl+点击可多选），再加外框。", "ThoughtCanvas"); return; }
            PushUndo();
            doc.Boundaries.Add(new Boundary { Id = NewOid("bd"), Members = ids, Color = "" });
            Relayout();
        }
        void AddSummary()
        {
            var ids = selSet.Select(t => t.Id).ToList();
            if (ids.Count == 0) { MessageBox.Show("先选中要概括的主题（Ctrl+点击可多选）。", "ThoughtCanvas"); return; }
            PushUndo();
            doc.Summaries.Add(new Summary { Id = NewOid("sm"), Members = ids, Text = "概要" });
            Relayout();
        }
        void AddCallout(Topic t)
        {
            if (t == null) return;
            string r = PromptText("标注", "标注文字：", "", true);
            if (r == null) return;
            PushUndo();
            doc.Callouts.Add(new Callout { Id = NewOid("co"), Tb = t.Id, Text = r.Trim() });
            Relayout();
        }
        void AddRelation()
        {
            var two = selSet.ToList();
            if (two.Count != 2) { MessageBox.Show("请按住 Ctrl 正好选中两个主题，再添加联系。", "ThoughtCanvas"); return; }
            PushUndo();
            doc.Relations.Add(new Relation { Id = NewOid("rl"), A = two[0].Id, B = two[1].Id, Text = "" });
            Relayout();
        }

        // ========== 聚焦 / 下钻 ==========
        void SetFocus(Topic t)
        {
            if (t == null) return;
            doc.FocusId = t.Id;
            Select(null);
            Rebuild(); Fit();
        }
        void ExitFocus()
        {
            doc.FocusId = null;
            Rebuild(); Fit();
        }
        void DeleteSelectedOverlay()
        {
            if (selOverlay == null) return;
            var (type, id) = selOverlay.Value;
            PushUndo();
            if (type == "bd") doc.Boundaries.RemoveAll(x => x.Id == id);
            else if (type == "sm") doc.Summaries.RemoveAll(x => x.Id == id);
            else if (type == "co") doc.Callouts.RemoveAll(x => x.Id == id);
            else if (type == "rl") doc.Relations.RemoveAll(x => x.Id == id);
            else if (type == "lk") doc.Links.RemoveAll(x => x.Id == id);
            selOverlay = null;
            Relayout();
        }

        void UpdateFocusBar()
        {
            bool on = focusTopic != null;
            focusBar.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (on) focusName.Text = string.IsNullOrWhiteSpace(focusTopic.Text) ? "(空白主题)" : focusTopic.Text;
        }
    }
}
