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
    // 蜘蛛网思维导图：自由文本框 + 锚点连线（docType=spider）。
    public partial class MainWindow
    {
        bool IsSpider => doc.DocType == "spider";

        // 根据 docType 调整界面（标题、连线按钮、整理按钮文案）
        void ApplyDocType()
        {
            docTitle.Text = IsSpider ? "蜘蛛网思维导图" : "大括号思维导图";
            btnLink.Visibility = IsSpider ? Visibility.Visible : Visibility.Collapsed;
            UpdateTidyBtn();
        }

        // ✨整理：大括号=切换紧凑模式；蜘蛛网=智能整理（弹簧布局）
        void Tidy()
        {
            if (IsSpider) { SpiderTidy(); return; }
            doc.CompactMode = (doc.CompactMode + 1) % 3;
            Rebuild(); Fit(); UpdateTidyBtn();
        }
        void UpdateTidyBtn()
        {
            if (IsSpider) { btnTidy.Content = "✨ 智能整理"; return; }
            string[] names = { "✨ 整理", "✨ 紧凑", "✨ 错位" };
            btnTidy.Content = names[Math.Max(0, Math.Min(2, doc.CompactMode))];
        }

        // 蜘蛛网智能整理：简易力导向（连线相吸、节点相斥），把缠在一起的图摊开
        void SpiderTidy()
        {
            if (readOnly) return;
            var nodes = doc.Roots.Where(t => byId.ContainsKey(t.Id)).ToList();
            if (nodes.Count < 2) return;
            PushUndo();
            // 度数高的更靠中心：初始不动，迭代松弛
            double cx = nodes.Average(n => n.X), cy = nodes.Average(n => n.Y);
            for (int iter = 0; iter < 120; iter++)
            {
                var fx = new Dictionary<Topic, double>();
                var fy = new Dictionary<Topic, double>();
                foreach (var n in nodes) { fx[n] = 0; fy[n] = 0; }
                // 斥力（所有节点对）
                for (int i = 0; i < nodes.Count; i++)
                    for (int j = i + 1; j < nodes.Count; j++)
                    {
                        var a = nodes[i]; var b = nodes[j];
                        double dx = a.X - b.X, dy = a.Y - b.Y;
                        double d2 = dx * dx + dy * dy + 0.01;
                        double rep = 90000 / d2;
                        double d = Math.Sqrt(d2);
                        double ux = dx / d, uy = dy / d;
                        fx[a] += ux * rep; fy[a] += uy * rep;
                        fx[b] -= ux * rep; fy[b] -= uy * rep;
                    }
                // 引力（有连线的相吸）
                foreach (var l in doc.Links)
                {
                    if (!byId.TryGetValue(l.A, out var a) || !byId.TryGetValue(l.B, out var b)) continue;
                    if (!fx.ContainsKey(a) || !fx.ContainsKey(b)) continue;
                    double dx = b.X - a.X, dy = b.Y - a.Y;
                    double d = Math.Sqrt(dx * dx + dy * dy) + 0.01;
                    double att = (d - 220) * 0.015;
                    double ux = dx / d, uy = dy / d;
                    fx[a] += ux * att * d; fy[a] += uy * att * d;
                    fx[b] -= ux * att * d; fy[b] -= uy * att * d;
                }
                // 轻微回中
                foreach (var n in nodes) { fx[n] += (cx - n.X) * 0.002; fy[n] += (cy - n.Y) * 0.002; }
                double step = 0.08;
                foreach (var n in nodes)
                {
                    n.X += Math.Max(-30, Math.Min(30, fx[n] * step));
                    n.Y += Math.Max(-30, Math.Min(30, fy[n] * step));
                }
            }
            ReorientLinks();   // 让每条连线从朝向对方的锚点出发
            Rebuild(); Fit();
        }

        // 智能整理后：把每条连线的两端锚点转到最朝向对方的那个
        void ReorientLinks()
        {
            foreach (var l in doc.Links)
            {
                if (!byId.TryGetValue(l.A, out var A) || !byId.TryGetValue(l.B, out var B)) continue;
                double acx = A.X + A.W / 2, bcx = B.X + B.W / 2;
                double ang = Math.Atan2(B.Y - A.Y, bcx - acx);
                l.AAng = NearestAnchorAngle(A, ang);
                l.BAng = NearestAnchorAngle(B, ang + Math.PI);
            }
        }
        double NearestAnchorAngle(Topic t, double target)
        {
            double best = 0, bd = double.MaxValue;
            foreach (var a in AnchorAngles(t))
            {
                double d = Math.Abs(Math.Atan2(Math.Sin(a - target), Math.Cos(a - target)));
                if (d < bd) { bd = d; best = a; }
            }
            return best;
        }

        void DoNewSpider()
        {
            undoStack.Clear(); redoStack.Clear();
            doc = new Document { DocType = "spider" };
            doc.Roots.Add(new Topic { Text = "主题 1", X = 2300, Y = 1400 });
            doc.Roots.Add(new Topic { Text = "主题 2", X = 2620, Y = 1520 });
            readOnly = false; btnRead.Content = T("👁 阅读模式", "👁 Read");
            ApplyDocType();
            RefreshLabels(); HideStart();
            Rebuild();
            Select(doc.Roots[0]);
            Fit();
        }

        // ===== 锚点几何（与网页版一致）=====
        const double ANCHOR_GAP = 10;                                  // 锚点离边缘的距离
        static readonly double[] CardAngs = { -Math.PI / 2, 0, Math.PI / 2, Math.PI };   // 上 右 下 左
        IEnumerable<double> AnchorAngles(Topic t)
        {
            foreach (var a in CardAngs) yield return a;
            foreach (var a in t.Anchors) yield return a;
        }
        // 沿 ang 方向，从节点中心到矩形边缘再外推 GAP 的世界坐标
        void AnchorPos(Topic t, double ang, out double x, out double y)
        {
            double hw = Math.Max(8, t.W / 2), hh = Math.Max(8, t.H / 2);
            double ca = Math.Cos(ang), sa = Math.Sin(ang);
            double k = 1.0 / Math.Max(Math.Abs(ca) / hw, Math.Abs(sa) / hh);
            double cx = t.LX + t.W / 2, cy = t.CY;
            x = cx + ca * (k + ANCHOR_GAP); y = cy + sa * (k + ANCHOR_GAP);
        }

        // 「🔗连线」按钮：选中两个节点，朝彼此方向各取一个锚点连起来
        void AddLinkFromSelection()
        {
            if (!IsSpider) return;
            var two = selSet.ToList();
            if (two.Count != 2) { MessageBox.Show(T("请按住 Ctrl 选中正好两个节点，或直接从锚点拖到另一个节点。", "Ctrl-select exactly two nodes, or drag from an anchor."), "ThoughtCanvas"); return; }
            var A = two[0]; var B = two[1];
            double acx = A.LX + A.W / 2, bcx = B.LX + B.W / 2;
            double angA = Math.Atan2(B.CY - A.CY, bcx - acx);
            CreateLink(A.Id, angA, B.Id, angA + Math.PI);
        }

        void CreateLink(string a, double aAng, string b, double bAng)
        {
            if (a == b) return;
            if (doc.Links.Any(l => (l.A == a && l.B == b) || (l.A == b && l.B == a)))
            { MessageBox.Show(T("这两个节点已经连过了。", "Already connected."), "ThoughtCanvas"); return; }
            PushUndo();
            doc.Links.Add(new Link { Id = NewOid("lk"), A = a, AAng = aAng, B = b, BAng = bAng, Mode = "line" });
            Relayout();
        }

        void AddSpiderNodeNear(Topic t)
        {
            if (readOnly) return;
            PushUndo();
            var c = new Topic { Text = "", X = t.X + t.W + 60, Y = t.Y + 40 };
            doc.Roots.Add(c);
            Rebuild(); Select(c); EnsureVisible(c); BeginEdit(c);
        }

        // ===== 连线渲染：锚点到锚点的直线 + 箭头 =====
        void DrawLinks()
        {
            foreach (var l in doc.Links)
            {
                if (!byId.TryGetValue(l.A, out var a) || !byId.TryGetValue(l.B, out var b)) continue;
                if (!cardOf.ContainsKey(a) || !cardOf.ContainsKey(b)) continue;
                AnchorPos(a, l.AAng, out double sx, out double sy);
                AnchorPos(b, l.BAng, out double ex, out double ey);
                if (!Fin(sx) || !Fin(sy) || !Fin(ex) || !Fin(ey)) continue;
                bool sel = selOverlay?.type == "lk" && selOverlay?.id == l.Id;
                Brush col = sel ? Accent : (Brush)new BrushConverter().ConvertFromString("#7B6CF0");
                try
                {
                    var path = new ShapePath
                    {
                        Data = Geometry.Parse($"M {F(sx)} {F(sy)} L {F(ex)} {F(ey)}"),
                        Stroke = col, StrokeThickness = sel ? 3 : 2.2, StrokeLineJoin = PenLineJoin.Round,
                        Cursor = Cursors.Hand, Tag = l
                    };
                    path.MouseLeftButtonDown += (s, e) => { e.Handled = true; selOverlay = ("lk", l.Id); Relayout(); };
                    path.ContextMenu = LinkMenu(l.Id);
                    AddVisual(path, 0);
                    // 箭头（指向 B）
                    double dx = ex - sx, dy = ey - sy, len = Math.Sqrt(dx * dx + dy * dy);
                    if (len > 0.1)
                    {
                        dx /= len; dy /= len;
                        double s1x = ex - dx * 11 - dy * 6, s1y = ey - dy * 11 + dx * 6;
                        double s2x = ex - dx * 11 + dy * 6, s2y = ey - dy * 11 - dx * 6;
                        AddVisual(new ShapePath { Data = Geometry.Parse($"M {F(ex)} {F(ey)} L {F(s1x)} {F(s1y)} M {F(ex)} {F(ey)} L {F(s2x)} {F(s2y)}"), Stroke = col, StrokeThickness = 2 }, 0);
                    }
                    if (!string.IsNullOrWhiteSpace(l.Text))
                    {
                        double mx = (sx + ex) / 2, my = (sy + ey) / 2;
                        var lab = new Border
                        {
                            Background = Brushes.White, CornerRadius = new CornerRadius(7), Padding = new Thickness(6, 1, 6, 1),
                            BorderBrush = col, BorderThickness = new Thickness(1),
                            Child = new TextBlock { Text = l.Text, FontSize = 11, Foreground = Ink }
                        };
                        lab.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                        Canvas.SetLeft(lab, mx - lab.DesiredSize.Width / 2); Canvas.SetTop(lab, my - lab.DesiredSize.Height / 2);
                        AddVisual(lab, 1);
                    }
                }
                catch { }
            }
            if (IsSpider) DrawAnchors();
        }

        // ===== 锚点小圆点 + 从锚点拖拽连线 =====
        void DrawAnchors()
        {
            if (readOnly) return;
            foreach (var t in roots)
            {
                if (!cardOf.ContainsKey(t)) continue;
                foreach (var ang in AnchorAngles(t))
                {
                    AnchorPos(t, ang, out double x, out double y);
                    if (!Fin(x) || !Fin(y)) continue;
                    double aLocal = ang;
                    var dot = new Ellipse
                    {
                        Width = 9, Height = 9, Fill = Brushes.White, Stroke = Accent, StrokeThickness = 1.6,
                        Opacity = 0.72, Cursor = Cursors.Cross, ToolTip = T("从这里拖到另一个节点即可连线", "Drag to another node to connect")
                    };
                    Canvas.SetLeft(dot, x - 4.5); Canvas.SetTop(dot, y - 4.5);
                    var topic = t;
                    dot.MouseEnter += (s, e) => { dot.Opacity = 1; dot.Width = dot.Height = 12; Canvas.SetLeft(dot, x - 6); Canvas.SetTop(dot, y - 6); };
                    dot.MouseLeave += (s, e) => { if (!linkDragging) { dot.Opacity = 0.72; dot.Width = dot.Height = 9; Canvas.SetLeft(dot, x - 4.5); Canvas.SetTop(dot, y - 4.5); } };
                    dot.MouseLeftButtonDown += (s, e) => { e.Handled = true; BeginLinkDrag(topic, aLocal, dot); };
                    AddVisual(dot, 4);
                }
            }
        }

        bool linkDragging;
        ShapePath linkPreview;
        (string tb, double ang)? linkCand;

        void BeginLinkDrag(Topic src, double srcAng, Ellipse dot)
        {
            if (readOnly) return;
            linkDragging = true; linkCand = null;
            AnchorPos(src, srcAng, out double fx, out double fy);
            linkPreview = new ShapePath { Stroke = Accent, StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 4, 3 }, IsHitTestVisible = false };
            Panel.SetZIndex(linkPreview, 6); world.Children.Add(linkPreview);

            MouseEventHandler move = null; MouseButtonEventHandler up = null;
            move = (s, e) =>
            {
                var w = ToWorld(e.GetPosition(viewport));
                linkCand = NearestAnchor(w.X, w.Y, src.Id);
                double tx = w.X, ty = w.Y;
                if (linkCand != null) AnchorPos(byId[linkCand.Value.tb], linkCand.Value.ang, out tx, out ty);
                if (Fin(fx) && Fin(fy) && Fin(tx) && Fin(ty))
                    try { linkPreview.Data = Geometry.Parse($"M {F(fx)} {F(fy)} L {F(tx)} {F(ty)}"); } catch { }
            };
            up = (s, e) =>
            {
                dot.ReleaseMouseCapture();
                dot.MouseMove -= move; dot.MouseLeftButtonUp -= up;
                if (linkPreview != null) { world.Children.Remove(linkPreview); linkPreview = null; }
                linkDragging = false;
                var c = linkCand; linkCand = null;
                if (c != null) CreateLink(src.Id, srcAng, c.Value.tb, c.Value.ang);
            };
            dot.MouseMove += move; dot.MouseLeftButtonUp += up;
            dot.CaptureMouse();
        }

        (string tb, double ang)? NearestAnchor(double x, double y, string exceptId)
        {
            (string, double)? best = null; double bd = 24;
            foreach (var t in roots)
            {
                if (t.Id == exceptId || !cardOf.ContainsKey(t)) continue;
                foreach (var ang in AnchorAngles(t))
                {
                    AnchorPos(t, ang, out double px, out double py);
                    double d = Math.Sqrt((px - x) * (px - x) + (py - y) * (py - y));
                    if (d < bd) { bd = d; best = (t.Id, ang); }
                }
            }
            return best;
        }

        // 添加自定义锚点：进入模式，移动鼠标在节点边缘预览，点击落点，Esc 取消
        void StartAddAnchor(Topic t)
        {
            if (readOnly || !IsSpider) return;
            Select(t);
            MouseEventHandler mm = null; MouseButtonEventHandler clk = null; KeyEventHandler key = null;
            double curAng = 0; ShapePath preview = null;
            void Cleanup()
            {
                viewport.MouseMove -= mm; viewport.PreviewMouseLeftButtonDown -= clk; PreviewKeyDown -= key;
                if (preview != null) { world.Children.Remove(preview); preview = null; }
            }
            mm = (s, e) =>
            {
                var w = ToWorld(e.GetPosition(viewport));
                curAng = Math.Atan2(w.Y - t.CY, w.X - (t.LX + t.W / 2));
                AnchorPos(t, curAng, out double x, out double y);
                if (preview == null) { preview = new ShapePath { Fill = Accent, IsHitTestVisible = false }; Panel.SetZIndex(preview, 7); world.Children.Add(preview); }
                preview.Data = Geometry.Parse($"M {F(x - 5)} {F(y)} A 5 5 0 1 0 {F(x + 5)} {F(y)} A 5 5 0 1 0 {F(x - 5)} {F(y)} Z");
            };
            clk = (s, e) =>
            {
                e.Handled = true;
                PushUndo(); t.Anchors.Add(curAng); Cleanup(); Relayout();
            };
            key = (s, e) => { if (e.Key == Key.Escape) { e.Handled = true; Cleanup(); } };
            viewport.MouseMove += mm; viewport.PreviewMouseLeftButtonDown += clk; PreviewKeyDown += key;
        }

        ContextMenu LinkMenu(string id)
        {
            var m = new ContextMenu();
            MenuItem MI(string h, Action a) { var mi = new MenuItem { Header = h }; mi.Click += (s, e) => a(); return mi; }
            m.Items.Add(MI("编辑连线文字…", () =>
            {
                var l = doc.Links.FirstOrDefault(x => x.Id == id); if (l == null) return;
                var r = PromptText("连线", "连线上的文字：", l.Text, false);
                if (r != null) { PushUndo(); l.Text = r.Trim(); Relayout(); }
            }));
            m.Items.Add(MI("删除连线", () => { PushUndo(); doc.Links.RemoveAll(x => x.Id == id); selOverlay = null; Relayout(); }));
            return m;
        }
    }
}
