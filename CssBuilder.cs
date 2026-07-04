using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;

namespace BmapUiEditor
{
    /// <summary>
    /// 把「部件配色 + 手搓图形/手绘/图片」拼成可直接注入 ThoughtCanvas 的 CSS（.bmapui 写法 B）。
    /// 原则：**只输出改过的东西**——没添加的部件、没改动的颜色一概不写，软件里自动保持原版。
    /// 图形按落点分宿主：顶栏 → #toolbar；节点卡片 → .card（软件里每张卡片都带上）；其余 → body（垫底）。
    /// 位置按「就近的角」锚定，窗口大小变化不跑偏。遮罩图形转成 SVG clipPath 裁剪它下面那一层。
    /// </summary>
    public static class CssBuilder
    {
        public const double DesignW = 1200, DesignH = 700;

        public static readonly Rect ToolbarBox = new Rect(0, 0, 1200, 54);
        public static readonly Rect[] CardBoxes =
        {
            new Rect(137, 326, 156, 52),
            new Rect(380, 279, 150, 46),
            new Rect(380, 379, 150, 46),
        };
        public static readonly Rect BodyBox = new Rect(0, 0, DesignW, DesignH);

        private static string F(double v)
        {
            return Math.Round(v, 2).ToString("0.##", CultureInfo.InvariantCulture);
        }

        public static string Build(List<CanvasElement> elements, string name)
        {
            var sb = new StringBuilder();
            sb.AppendLine("/* 由 BMAP 界面编辑器生成 · " + (name ?? "") + " · 未修改的部分自动保持原版 */");

            foreach (var el in elements)
            {
                var ce = el as ComponentElement;
                if (ce == null || !ce.Visible) continue;
                var def = ComponentLib.Find(ce.CompId);
                if (def == null) continue;
                foreach (var p in ce.Props)
                {
                    if (!p.Changed) continue;
                    string tpl;
                    if (def.CssTemplates.TryGetValue(p.Key, out tpl))
                        sb.AppendLine(tpl.Replace("@V", p.Value));
                }
            }

            AppendDecorations(sb, elements);
            return sb.ToString();
        }

        // ===== 图形 / 手绘 / 图片 → 各宿主的多层 background =====

        private class Layer { public string Img, Pos, Size; }

        private static void AppendDecorations(StringBuilder sb, List<CanvasElement> elements)
        {
            var byHost = new Dictionary<string, List<Layer>>();

            // 索引越大越在上层；CSS 背景第一层在最上 → 逆序遍历
            for (int i = elements.Count - 1; i >= 0; i--)
            {
                var el = elements[i];
                if (!el.Visible) continue;
                var sh = el as ShapeElement;
                if (sh != null && sh.IsMask) continue;      // 遮罩本身不输出

                ShapeElement mask = null;
                if (i + 1 < elements.Count)
                {
                    var above = elements[i + 1] as ShapeElement;
                    if (above != null && above.IsMask && above.Visible && above.Kind != "line" && IsDecoratable(el))
                        mask = above;
                }
                AddElementLayer(byHost, el, mask);
            }

            foreach (var kv in byHost)
            {
                string imgs = string.Join(",", kv.Value.Select(l => l.Img));
                string poss = string.Join(",", kv.Value.Select(l => l.Pos));
                string sizes = string.Join(",", kv.Value.Select(l => l.Size));
                string extra = kv.Key == "body" ? "background-attachment:fixed!important;" : "";
                sb.AppendLine(kv.Key + "{background-image:" + imgs + "!important;background-position:" + poss
                    + "!important;background-size:" + sizes + "!important;background-repeat:no-repeat!important;" + extra + "}");
            }
        }

        private static bool IsDecoratable(CanvasElement el)
        {
            return el is ShapeElement || el is InkElement || el is ImageElement;
        }

        private static void AddElementLayer(Dictionary<string, List<Layer>> byHost, CanvasElement el, ShapeElement mask)
        {
            Rect bbox;
            double pad, opacity;
            string inner;

            var s = el as ShapeElement;
            var k = el as InkElement;
            var im = el as ImageElement;

            if (s != null)
            {
                bbox = BBoxOf(s);
                pad = Math.Ceiling(s.StrokeW) + 1;
                inner = ShapeSvg(s, bbox, pad);
                opacity = s.Opacity;
            }
            else if (k != null)
            {
                if (k.Points.Count < 2) return;
                bbox = InkBBox(k);
                pad = Math.Ceiling(k.Width) + 1;
                inner = InkSvg(k, bbox, pad);
                opacity = k.Opacity;
            }
            else if (im != null)
            {
                if (string.IsNullOrEmpty(im.Base64)) return;
                bbox = new Rect(im.X, im.Y, im.W, im.H);
                pad = 0;
                opacity = im.Opacity;
                if (mask == null && opacity >= 0.999)
                {
                    // 无遮罩不透明的图片直接作为一层背景，不裹 SVG
                    AddToHost(byHost, bbox, MakePositioned(bbox, 0, "url(\"data:" + im.Mime + ";base64," + im.Base64 + "\")"));
                    return;
                }
                inner = "<image x='0' y='0' width='" + F(im.W) + "' height='" + F(im.H)
                    + "' href='data:" + im.Mime + ";base64," + im.Base64 + "' preserveAspectRatio='none'/>";
            }
            else return;

            double svgW = Math.Max(1, bbox.Width + pad * 2), svgH = Math.Max(1, bbox.Height + pad * 2);
            string defs = "";
            if (mask != null)
            {
                string clip = ClipSvg(mask, bbox, pad);
                if (clip != null)
                {
                    defs = "<defs><clipPath id='m'>" + clip + "</clipPath></defs>";
                    inner = "<g clip-path='url(#m)'>" + inner + "</g>";
                }
            }
            if (opacity < 0.999) inner = "<g opacity='" + F(opacity) + "'>" + inner + "</g>";

            string svg = "<svg xmlns='http://www.w3.org/2000/svg' width='" + F(svgW) + "' height='" + F(svgH) + "'>" + defs + inner + "</svg>";
            string img = "url(\"data:image/svg+xml," + Uri.EscapeDataString(svg) + "\")";
            AddToHost(byHost, bbox, MakePositioned(bbox, pad, img));
        }

        private static Layer MakePositioned(Rect bbox, double pad, string img)
        {
            Rect box = HostBox(bbox);
            double svgW = Math.Max(1, bbox.Width + pad * 2), svgH = Math.Max(1, bbox.Height + pad * 2);
            double dx = bbox.X - pad - box.X, dy = bbox.Y - pad - box.Y;
            string posX = (bbox.X + bbox.Width / 2) < box.X + box.Width / 2
                ? "left " + F(dx) + "px"
                : "right " + F(box.Width - (dx + svgW)) + "px";
            string posY = (bbox.Y + bbox.Height / 2) < box.Y + box.Height / 2
                ? "top " + F(dy) + "px"
                : "bottom " + F(box.Height - (dy + svgH)) + "px";
            return new Layer { Img = img, Pos = posX + " " + posY, Size = F(svgW) + "px " + F(svgH) + "px" };
        }

        private static Rect HostBox(Rect bbox)
        {
            var c = new Point(bbox.X + bbox.Width / 2, bbox.Y + bbox.Height / 2);
            if (ToolbarBox.Contains(c)) return ToolbarBox;
            foreach (var b in CardBoxes) if (b.Contains(c)) return b;
            return BodyBox;
        }

        private static string HostSelector(Rect bbox)
        {
            var c = new Point(bbox.X + bbox.Width / 2, bbox.Y + bbox.Height / 2);
            if (ToolbarBox.Contains(c)) return "#toolbar";
            foreach (var b in CardBoxes) if (b.Contains(c)) return ".card";
            return "body";
        }

        private static void AddToHost(Dictionary<string, List<Layer>> byHost, Rect bbox, Layer layer)
        {
            string host = HostSelector(bbox);
            List<Layer> list;
            if (!byHost.TryGetValue(host, out list)) { list = new List<Layer>(); byHost[host] = list; }
            list.Add(layer);
        }

        // ===== 各类元素的 SVG 片段 =====

        private static string ShapeSvg(ShapeElement s, Rect bbox, double pad)
        {
            string fill = s.NoFill ? "none" : s.Fill;
            string strokeAttr = s.StrokeW > 0
                ? " stroke='" + s.Stroke + "' stroke-width='" + F(s.StrokeW) + "'"
                : "";
            switch (s.Kind)
            {
                case "ellipse":
                    return "<ellipse cx='" + F(pad + bbox.Width / 2) + "' cy='" + F(pad + bbox.Height / 2)
                        + "' rx='" + F(bbox.Width / 2) + "' ry='" + F(bbox.Height / 2) + "' fill='" + fill + "'" + strokeAttr + "/>";
                case "line":
                    return "<line x1='" + F(s.X - bbox.X + pad) + "' y1='" + F(s.Y - bbox.Y + pad)
                        + "' x2='" + F(s.X2 - bbox.X + pad) + "' y2='" + F(s.Y2 - bbox.Y + pad)
                        + "' stroke='" + s.Stroke + "' stroke-width='" + F(Math.Max(1, s.StrokeW)) + "' stroke-linecap='round'/>";
                default:
                    string rx = s.Kind == "roundrect" ? " rx='" + F(s.Radius) + "'" : "";
                    return "<rect x='" + F(pad) + "' y='" + F(pad) + "' width='" + F(bbox.Width) + "' height='" + F(bbox.Height)
                        + "'" + rx + " fill='" + fill + "'" + strokeAttr + "/>";
            }
        }

        private static string InkSvg(InkElement k, Rect bbox, double pad)
        {
            var sb = new StringBuilder("M ");
            for (int i = 0; i < k.Points.Count; i++)
            {
                var p = k.Points[i];
                if (i > 0) sb.Append(" L ");
                sb.Append(F(p.X - bbox.X + pad)).Append(' ').Append(F(p.Y - bbox.Y + pad));
            }
            return "<path d='" + sb + "' fill='none' stroke='" + k.Color + "' stroke-width='" + F(k.Width)
                + "' stroke-linecap='round' stroke-linejoin='round'/>";
        }

        private static string ClipSvg(ShapeElement m, Rect bbox, double pad)
        {
            double ox = bbox.X - pad, oy = bbox.Y - pad;
            switch (m.Kind)
            {
                case "ellipse":
                    return "<ellipse cx='" + F(m.X + m.W / 2 - ox) + "' cy='" + F(m.Y + m.H / 2 - oy)
                        + "' rx='" + F(m.W / 2) + "' ry='" + F(m.H / 2) + "'/>";
                case "rect":
                case "roundrect":
                    string rx = m.Kind == "roundrect" ? " rx='" + F(m.Radius) + "'" : "";
                    return "<rect x='" + F(m.X - ox) + "' y='" + F(m.Y - oy) + "' width='" + F(m.W) + "' height='" + F(m.H) + "'" + rx + "/>";
                default:
                    return null;
            }
        }

        // ================= 部件皮肤（V0.0.3 重做核心）=================
        // 每个"设计目标"的元素 → 裁到设计框内 → 一张 SVG 背景贴到该部件的选择器上，
        // 文本区 → 内边距/对齐/字色作用到部件真正的文字元素，从而保留功能。

        public static string BuildSkins(Dictionary<string, List<CanvasElement>> designs, string name)
        {
            var sb = new StringBuilder();
            sb.AppendLine("/* 由 BMAP 界面编辑器生成 · " + (name ?? "") + " · 部件皮肤（保留功能）·未设计的部件保持原版 */");
            if (designs == null) return sb.ToString();

            foreach (var kv in designs)
            {
                var t = DesignTargetLib.Find(kv.Key);
                if (t == null || kv.Value == null) continue;
                var els = kv.Value.Where(e => e.Visible).ToList();
                var drawable = els.Where(IsDrawable).ToList();
                var textRegion = els.OfType<TextRegionElement>().FirstOrDefault();
                if (drawable.Count == 0 && textRegion == null) continue;

                // 渲染框：普通/限定=设计框；自由=框∪所有元素
                Rect view = t.FrameRect;
                if (!Prefs.ClipToFrame)
                    foreach (var e in drawable) view = Rect.Union(view, BBoxOfElement(e));

                if (drawable.Count > 0)
                {
                    string svg = RenderDesignSvg(els, view, t, textRegion);
                    string img = "url(\"data:image/svg+xml," + Uri.EscapeDataString(svg) + "\")";
                    sb.AppendLine(t.SkinSelector + "{background-image:" + img
                        + "!important;background-size:100% 100%!important;background-repeat:no-repeat!important;"
                        + "background-color:transparent!important;border:none!important;box-shadow:none!important;}");
                }

                if (t.HasText && !string.IsNullOrEmpty(t.InnerSelector) && textRegion != null)
                {
                    var f = t.FrameRect;
                    double pl = Math.Max(0, textRegion.X - f.X), pt = Math.Max(0, textRegion.Y - f.Y);
                    double pr = Math.Max(0, f.Right - (textRegion.X + textRegion.W)), pb = Math.Max(0, f.Bottom - (textRegion.Y + textRegion.H));
                    var css = new StringBuilder();
                    css.Append("padding:" + F(pt) + "px " + F(pr) + "px " + F(pb) + "px " + F(pl) + "px!important;");
                    css.Append("text-align:" + textRegion.AlignH + "!important;");
                    css.Append("font-size:" + F(textRegion.FontSize) + "px!important;");
                    css.Append("font-weight:" + (textRegion.Bold ? "700" : "400") + "!important;");
                    bool custom = Prefs.AllowCustomText && textRegion.CustomText;
                    css.Append("color:" + (custom ? "transparent" : textRegion.Color) + "!important;");
                    sb.AppendLine(t.InnerSelector + "{" + css + "}");
                }
            }
            return sb.ToString();
        }

        private static bool IsDrawable(CanvasElement el)
        {
            var s = el as ShapeElement;
            if (s != null) return !s.IsMask;   // 遮罩不单独画
            return el is InkElement || el is ImageElement;
        }

        private static string RenderDesignSvg(List<CanvasElement> els, Rect view, DesignTarget t, TextRegionElement textRegion)
        {
            double ox = view.X, oy = view.Y;
            var body = new StringBuilder();
            int clipId = 0;

            for (int i = 0; i < els.Count; i++)
            {
                var el = els[i];
                if (!IsDrawable(el)) continue;
                string node = DesignElementSvg(el, ox, oy);
                if (node == null) continue;

                ShapeElement mask = null;
                if (i + 1 < els.Count)
                {
                    var above = els[i + 1] as ShapeElement;
                    if (above != null && above.IsMask && above.Visible && above.Kind != "line") mask = above;
                }
                double op = ElementOpacity(el);
                if (mask != null)
                {
                    string cid = "c" + (clipId++);
                    body.Append("<clipPath id='" + cid + "'>" + MaskClipSvg(mask, ox, oy) + "</clipPath>");
                    body.Append("<g clip-path='url(#" + cid + ")'" + (op < 0.999 ? " opacity='" + F(op) + "'" : "") + ">" + node + "</g>");
                }
                else if (op < 0.999)
                    body.Append("<g opacity='" + F(op) + "'>" + node + "</g>");
                else
                    body.Append(node);
            }

            if (textRegion != null && Prefs.AllowCustomText && textRegion.CustomText && !string.IsNullOrEmpty(textRegion.Text))
                body.Append(CustomTextSvg(textRegion, ox, oy));

            return "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 " + F(view.Width) + " " + F(view.Height) + "'>" + body + "</svg>";
        }

        private static double ElementOpacity(CanvasElement el)
        {
            var s = el as ShapeElement; if (s != null) return s.Opacity;
            var k = el as InkElement; if (k != null) return k.Opacity;
            var im = el as ImageElement; if (im != null) return im.Opacity;
            return 1.0;
        }

        private static string DesignElementSvg(CanvasElement el, double ox, double oy)
        {
            var s = el as ShapeElement;
            if (s != null)
            {
                string fill = s.NoFill ? "none" : s.Fill;
                string stroke = s.StrokeW > 0 ? " stroke='" + s.Stroke + "' stroke-width='" + F(s.StrokeW) + "'" : "";
                switch (s.Kind)
                {
                    case "ellipse":
                        return "<ellipse cx='" + F(s.X - ox + s.W / 2) + "' cy='" + F(s.Y - oy + s.H / 2)
                            + "' rx='" + F(s.W / 2) + "' ry='" + F(s.H / 2) + "' fill='" + fill + "'" + stroke + "/>";
                    case "line":
                        return "<line x1='" + F(s.X - ox) + "' y1='" + F(s.Y - oy) + "' x2='" + F(s.X2 - ox) + "' y2='" + F(s.Y2 - oy)
                            + "' stroke='" + s.Stroke + "' stroke-width='" + F(Math.Max(1, s.StrokeW)) + "' stroke-linecap='round'/>";
                    default:
                        string rx = s.Kind == "roundrect" ? " rx='" + F(s.Radius) + "'" : "";
                        return "<rect x='" + F(s.X - ox) + "' y='" + F(s.Y - oy) + "' width='" + F(s.W) + "' height='" + F(s.H) + "'" + rx + " fill='" + fill + "'" + stroke + "/>";
                }
            }
            var k = el as InkElement;
            if (k != null)
            {
                if (k.Points.Count < 2) return null;
                var d = new StringBuilder("M ");
                for (int i = 0; i < k.Points.Count; i++)
                {
                    if (i > 0) d.Append(" L ");
                    d.Append(F(k.Points[i].X - ox)).Append(' ').Append(F(k.Points[i].Y - oy));
                }
                return "<path d='" + d + "' fill='none' stroke='" + k.Color + "' stroke-width='" + F(k.Width) + "' stroke-linecap='round' stroke-linejoin='round'/>";
            }
            var im = el as ImageElement;
            if (im != null)
            {
                if (string.IsNullOrEmpty(im.Base64)) return null;
                return "<image x='" + F(im.X - ox) + "' y='" + F(im.Y - oy) + "' width='" + F(im.W) + "' height='" + F(im.H)
                    + "' href='data:" + im.Mime + ";base64," + im.Base64 + "' preserveAspectRatio='none'/>";
            }
            return null;
        }

        private static string MaskClipSvg(ShapeElement m, double ox, double oy)
        {
            switch (m.Kind)
            {
                case "ellipse":
                    return "<ellipse cx='" + F(m.X - ox + m.W / 2) + "' cy='" + F(m.Y - oy + m.H / 2) + "' rx='" + F(m.W / 2) + "' ry='" + F(m.H / 2) + "'/>";
                default:
                    string rx = m.Kind == "roundrect" ? " rx='" + F(m.Radius) + "'" : "";
                    return "<rect x='" + F(m.X - ox) + "' y='" + F(m.Y - oy) + "' width='" + F(m.W) + "' height='" + F(m.H) + "'" + rx + "/>";
            }
        }

        private static string CustomTextSvg(TextRegionElement tr, double ox, double oy)
        {
            double x; string a;
            if (tr.AlignH == "center") { x = tr.X - ox + tr.W / 2; a = "middle"; }
            else if (tr.AlignH == "right") { x = tr.X - ox + tr.W; a = "end"; }
            else { x = tr.X - ox; a = "start"; }
            double y = tr.Y - oy + tr.H / 2;
            string esc = tr.Text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
            return "<text x='" + F(x) + "' y='" + F(y) + "' fill='" + tr.Color + "' font-size='" + F(tr.FontSize)
                + "' font-family='sans-serif' font-weight='" + (tr.Bold ? "700" : "400")
                + "' text-anchor='" + a + "' dominant-baseline='middle'>" + esc + "</text>";
        }

        // ===== 包围盒 =====

        public static Rect BBoxOf(ShapeElement s)
        {
            if (s.Kind == "line") return new Rect(new Point(s.X, s.Y), new Point(s.X2, s.Y2));
            return new Rect(s.X, s.Y, s.W, s.H);
        }

        public static Rect InkBBox(InkElement k)
        {
            if (k.Points.Count == 0) return new Rect(0, 0, 1, 1);
            double x1 = double.MaxValue, y1 = double.MaxValue, x2 = double.MinValue, y2 = double.MinValue;
            foreach (var p in k.Points)
            {
                if (p.X < x1) x1 = p.X;
                if (p.Y < y1) y1 = p.Y;
                if (p.X > x2) x2 = p.X;
                if (p.Y > y2) y2 = p.Y;
            }
            return new Rect(x1, y1, Math.Max(1, x2 - x1), Math.Max(1, y2 - y1));
        }

        public static Rect BBoxOfElement(CanvasElement el)
        {
            var s = el as ShapeElement;
            if (s != null) return BBoxOf(s);
            var k = el as InkElement;
            if (k != null) return InkBBox(k);
            var im = el as ImageElement;
            if (im != null) return new Rect(im.X, im.Y, im.W, im.H);
            var tr = el as TextRegionElement;
            if (tr != null) return new Rect(tr.X, tr.Y, tr.W, tr.H);
            var ce = el as ComponentElement;
            if (ce != null)
            {
                var def = ComponentLib.Find(ce.CompId);
                if (def != null && def.Boxes.Length > 0)
                {
                    var r = def.Boxes[0];
                    for (int i = 1; i < def.Boxes.Length; i++) r.Union(def.Boxes[i]);
                    return r;
                }
            }
            return new Rect(0, 0, 1, 1);
        }
    }
}
