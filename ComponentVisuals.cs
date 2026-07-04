using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace BmapUiEditor
{
    /// <summary>
    /// 把一个原版部件实例画成 WPF 元素组（位置=它在真实界面里的位置，颜色取实例当前值）。
    /// 颜色一改就整组重建，最上层盖透明命中块用于点选。
    /// </summary>
    public static class ComponentVisuals
    {
        public static Canvas Build(ComponentElement el)
        {
            var g = new Canvas { Width = 1200, Height = 700 };
            switch (el.CompId)
            {
                case "toolbar": BuildToolbar(g, el); break;
                case "title": BuildTitle(g, el); break;
                case "btn": BuildBtn(g, el); break;
                case "btnPrimary": BuildBtnPrimary(g, el); break;
                case "fname": BuildFname(g, el); break;
                case "stage": BuildStage(g, el); break;
                case "card": BuildCard(g, el); break;
                case "cardSel": BuildCardSel(g, el); break;
                case "brace": BuildBrace(g, el); break;
                case "link": BuildLink(g, el); break;
                case "menu": BuildMenu(g, el); break;
                case "floatbar": BuildFloatbar(g, el); break;
            }
            // 透明命中块（盖最上，点哪都能选中这个部件）
            var def = ComponentLib.Find(el.CompId);
            if (def != null)
            {
                foreach (var b in def.Boxes)
                {
                    var hit = new Rectangle { Width = b.Width, Height = b.Height, Fill = Brushes.Transparent, Tag = el };
                    Canvas.SetLeft(hit, b.X);
                    Canvas.SetTop(hit, b.Y);
                    g.Children.Add(hit);
                }
            }
            return g;
        }

        private static Brush B(ComponentElement el, string key) { return MainWindow.BrushOf(el.Get(key)); }

        private static void Put(Canvas g, UIElement e, double x, double y)
        {
            Canvas.SetLeft(e, x);
            Canvas.SetTop(e, y);
            g.Children.Add(e);
        }

        private static Border Box(double w, double h, double r, Brush bg, Brush bd, double bw, string text, Brush fg, double fs)
        {
            var b = new Border { Width = w, Height = h, CornerRadius = new CornerRadius(r), Background = bg, BorderBrush = bd, BorderThickness = new Thickness(bw) };
            if (text != null)
                b.Child = new TextBlock { Text = text, FontSize = fs, Foreground = fg, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            return b;
        }

        public static DrawingBrush MakeGridBrush(string gridHex)
        {
            var pen = new Pen(MainWindow.BrushOf(gridHex), 1);
            var group = new GeometryGroup();
            group.Children.Add(new LineGeometry(new Point(0, 0.5), new Point(22, 0.5)));
            group.Children.Add(new LineGeometry(new Point(0.5, 0), new Point(0.5, 22)));
            var gd = new GeometryDrawing(null, pen, group);
            var db = new DrawingBrush(gd)
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, 22, 22),
                ViewportUnits = BrushMappingMode.Absolute,
                Stretch = Stretch.None
            };
            db.Freeze();
            return db;
        }

        // ================= 各部件 =================
        private static void BuildToolbar(Canvas g, ComponentElement el)
        {
            var bar = new Border
            {
                Width = 1200, Height = 54,
                Background = B(el, "bg"),
                BorderBrush = B(el, "border"),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
            Put(g, bar, 0, 0);
            var vsep = new Rectangle { Width = 1, Height = 22, Fill = B(el, "border") };
            Put(g, vsep, 176, 16);
        }

        private static void BuildTitle(Canvas g, ComponentElement el)
        {
            var dot = new Ellipse { Width = 14, Height = 14, Fill = B(el, "dot") };
            Put(g, dot, 18, 20);
            var t = new TextBlock { Text = "ThoughtCanvas", FontSize = 16, FontWeight = FontWeights.Bold, Foreground = B(el, "text") };
            Put(g, t, 40, 15);
        }

        private static void BuildBtn(Canvas g, ComponentElement el)
        {
            Put(g, Box(92, 34, 9, B(el, "bg"), B(el, "border"), 1, "📁 文件 ▾", B(el, "text"), 13), 190, 10);
            Put(g, Box(80, 34, 9, Brushes.Transparent, Brushes.Transparent, 1, "✨ 整理", B(el, "text"), 13), 402, 10);
        }

        private static void BuildBtnPrimary(Canvas g, ComponentElement el)
        {
            Put(g, Box(100, 34, 9, B(el, "bg"), Brushes.Transparent, 0, "＋ 文本框", B(el, "text"), 13), 292, 10);
        }

        private static void BuildFname(Canvas g, ComponentElement el)
        {
            var t = new TextBlock { Text = "未命名.bmap", FontSize = 13, Foreground = B(el, "color") };
            Put(g, t, 1060, 19);
        }

        private static void BuildStage(Canvas g, ComponentElement el)
        {
            var bg = new Rectangle { Width = 1200, Height = 646, Fill = B(el, "bg") };
            Put(g, bg, 0, 54);
            var grid = new Rectangle { Width = 1200, Height = 646, Fill = MakeGridBrush(el.Get("grid")) };
            Put(g, grid, 0, 54);
        }

        private static void BuildCard(Canvas g, ComponentElement el)
        {
            Put(g, Box(150, 46, 11, B(el, "bg"), B(el, "border"), 1, "分支节点", B(el, "text"), 14), 380, 279);
            Put(g, Box(150, 46, 11, B(el, "bg"), B(el, "border"), 1, "分支节点二", B(el, "text"), 14), 380, 379);
        }

        private static void BuildCardSel(Canvas g, ComponentElement el)
        {
            var ring = new Border
            {
                Width = 160, Height = 56,
                CornerRadius = new CornerRadius(14),
                BorderThickness = new Thickness(2.5),
                BorderBrush = B(el, "ring"),
                Background = Brushes.Transparent
            };
            var card = new Border
            {
                CornerRadius = new CornerRadius(11),
                BorderThickness = new Thickness(1.5),
                BorderBrush = B(el, "accent"),
                Background = B(el, "bg")
            };
            card.Child = new TextBlock { Text = "中心主题", FontSize = 14, Foreground = B(el, "text"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            ring.Child = card;
            Put(g, ring, 135, 324);
        }

        private static void BuildBrace(Canvas g, ComponentElement el)
        {
            var p = new Path
            {
                Data = Geometry.Parse("M 362,281 C 350,281 347,288 347,300 L 347,338 C 347,348 344,352 337,354 C 344,356 347,360 347,370 L 347,406 C 347,418 350,425 362,425"),
                Stroke = B(el, "color"),
                StrokeThickness = 2.5,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
            g.Children.Add(p);
        }

        private static void BuildLink(Canvas g, ComponentElement el)
        {
            var c = B(el, "color");
            g.Children.Add(new Line { X1 = 620, Y1 = 470, X2 = 758, Y2 = 436, Stroke = c, StrokeThickness = 2 });
            g.Children.Add(new Line { X1 = 620, Y1 = 470, X2 = 758, Y2 = 504, Stroke = c, StrokeThickness = 2 });
            var a = B(el, "anchor");
            foreach (var pt in new[] { new Point(620, 470), new Point(758, 436), new Point(758, 504) })
            {
                var e = new Ellipse { Width = 10, Height = 10, Fill = Brushes.White, Stroke = a, StrokeThickness = 1.6 };
                Put(g, e, pt.X - 5, pt.Y - 5);
            }
        }

        private static void BuildMenu(Canvas g, ComponentElement el)
        {
            var box = new Border
            {
                Width = 190, Height = 150,
                CornerRadius = new CornerRadius(10),
                Background = B(el, "bg"),
                BorderBrush = B(el, "border"),
                BorderThickness = new Thickness(1)
            };
            Put(g, box, 840, 130);
            Put(g, new TextBlock { Text = "✏ 编辑文字", FontSize = 13, Foreground = B(el, "text") }, 856, 142);
            var hover = new Border { Width = 178, Height = 26, CornerRadius = new CornerRadius(6), Background = B(el, "hoverBg") };
            Put(g, hover, 846, 168);
            Put(g, new TextBlock { Text = "🎨 调整颜色", FontSize = 13, Foreground = B(el, "hoverText") }, 856, 172);
            var sep = new Rectangle { Width = 174, Height = 1, Fill = B(el, "border") };
            Put(g, sep, 848, 204);
            Put(g, new TextBlock { Text = "🗑 删除节点", FontSize = 13, Foreground = B(el, "text") }, 856, 214);
            Put(g, new TextBlock { Text = "⧉ 复制样式", FontSize = 13, Foreground = B(el, "text") }, 856, 244);
        }

        private static void BuildFloatbar(Canvas g, ComponentElement el)
        {
            var bar = new Border
            {
                Width = 200, Height = 40,
                CornerRadius = new CornerRadius(12),
                Background = B(el, "bg"),
                BorderBrush = B(el, "border"),
                BorderThickness = new Thickness(1)
            };
            Put(g, bar, 500, 628);
            var hover = new Border { Width = 40, Height = 28, CornerRadius = new CornerRadius(8), Background = B(el, "hoverBg") };
            Put(g, hover, 512, 634);
            Put(g, new TextBlock { Text = "↺", FontSize = 16, Foreground = B(el, "text") }, 526, 636);
            Put(g, new TextBlock { Text = "↻", FontSize = 16, Foreground = B(el, "text") }, 586, 636);
            Put(g, new TextBlock { Text = "🗑", FontSize = 14, Foreground = B(el, "text") }, 644, 638);
        }
    }
}
