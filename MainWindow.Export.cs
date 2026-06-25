using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using IOPath3 = System.IO.Path;

namespace ThoughtCanvas
{
    // 导出 PNG / JPG / PDF（把画布内容渲染成位图；PDF 用极简内嵌 JPEG 的方式，无第三方依赖）。
    public partial class MainWindow
    {
        const double ExportScale = 2.0;   // 2 倍超采样，更清晰

        // 自测：构造一张有富节点+叠加层的图，离屏渲染成 PNG（开发期用 --selftest 触发）
        public void SelfTest(string outPng)
        {
            Measure(new Size(1200, 800)); Arrange(new Rect(0, 0, 1200, 800)); UpdateLayout();
            var root = new Topic { Text = "中心主题", X = 400, Y = 360 };
            var a = new Topic { Text = "重要分支" }; a.Markers.Add("p1"); a.Markers.Add("t2"); a.Tags.Add("标签"); a.Note = "备注"; a.Todo = true; a.Done = true;
            var a1 = new Topic { Text = "子项一" }; var a2 = new Topic { Text = "子项二" };
            a.Children.Add(a1); a.Children.Add(a2);
            var b = new Topic { Text = "另一分支", Bg = "#e3f0ff" }; b.Markers.Add("star"); b.Link = "http://x";
            root.Children.Add(a); root.Children.Add(b);
            doc = new Document(); doc.Roots.Add(root); doc.Numbering = "num";
            doc.Boundaries.Add(new Boundary { Id = "bd1", Members = { a1.Id, a2.Id }, Label = "外框", Color = "#5ab867" });
            doc.Summaries.Add(new Summary { Id = "sm1", Members = { a1.Id, a2.Id }, Text = "概要" });
            doc.Callouts.Add(new Callout { Id = "co1", Tb = b.Id, Text = "标注气泡" });
            doc.Relations.Add(new Relation { Id = "rl1", A = a.Id, B = b.Id, Text = "联系" });
            HideStart();
            Rebuild();
            // 离屏时没有 PresentationSource，需手动量测/排布 world 才有内容边界
            world.Measure(new Size(6000, 4000));
            world.Arrange(new Rect(0, 0, 6000, 4000));
            world.UpdateLayout();
            var rtb = RenderContent();
            if (rtb == null) { File.WriteAllText(outPng + ".txt", "RENDER NULL"); return; }
            var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = File.Create(outPng)) enc.Save(fs);
            // 顺带验证 PDF 生成
            byte[] jpeg;
            using (var ms = new MemoryStream())
            {
                var je = new JpegBitmapEncoder { QualityLevel = 90 };
                je.Frames.Add(BitmapFrame.Create(rtb)); je.Save(ms); jpeg = ms.ToArray();
            }
            File.WriteAllBytes(IOPath3.ChangeExtension(outPng, ".pdf"), BuildPdf(jpeg, rtb.PixelWidth, rtb.PixelHeight));

            // 再渲染一张蜘蛛网图，验证锚点 + 连线
            var n1 = new Topic { Text = "想法 A", X = 300, Y = 320 };
            var n2 = new Topic { Text = "想法 B", X = 560, Y = 220 };
            var n3 = new Topic { Text = "想法 C", X = 560, Y = 430 };
            n1.Anchors.Add(-Math.PI / 4);   // 一个自定义锚点
            doc = new Document { DocType = "spider" };
            doc.Roots.Add(n1); doc.Roots.Add(n2); doc.Roots.Add(n3);
            doc.Links.Add(new Link { Id = "l1", A = n1.Id, AAng = 0, B = n2.Id, BAng = Math.PI, Text = "关联" });
            doc.Links.Add(new Link { Id = "l2", A = n1.Id, AAng = Math.PI / 2, B = n3.Id, BAng = Math.PI });
            Rebuild();
            world.Measure(new Size(6000, 4000)); world.Arrange(new Rect(0, 0, 6000, 4000)); world.UpdateLayout();
            var rtb2 = RenderContent();
            if (rtb2 != null)
            {
                var e2 = new PngBitmapEncoder(); e2.Frames.Add(BitmapFrame.Create(rtb2));
                using var fs2 = File.Create(IOPath3.Combine(IOPath3.GetDirectoryName(outPng), IOPath3.GetFileNameWithoutExtension(outPng) + "_spider.png"));
                e2.Save(fs2);
            }
        }

        // 渲染当前内容到位图（白底，含所有卡片/大括号/叠加层/连线）
        // 内容包围盒：逐个可见子元素求并集（比 GetDescendantBounds(world) 更可靠，
        // 不会把 6000×4000 的画布尺寸算进去）
        Rect ContentBounds()
        {
            double x1 = 1e9, y1 = 1e9, x2 = -1e9, y2 = -1e9; bool any = false;
            foreach (UIElement ch in world.Children)
            {
                if (ch.Visibility != Visibility.Visible) continue;
                var fe = ch as FrameworkElement;
                Size sz = fe != null ? fe.RenderSize : new Size();
                if (sz.Width < 0.5 || sz.Height < 0.5) sz = VisualTreeHelper.GetDescendantBounds(ch).Size;
                if (sz.Width < 0.5 || sz.Height < 0.5) continue;
                try
                {
                    var r = ch.TransformToAncestor(world).TransformBounds(new Rect(sz));
                    if (r.Width >= 5500 && r.Height >= 3500) continue;   // 跳过铺满画布的元素
                    any = true;
                    x1 = Math.Min(x1, r.Left); y1 = Math.Min(y1, r.Top);
                    x2 = Math.Max(x2, r.Right); y2 = Math.Max(y2, r.Bottom);
                }
                catch { }
            }
            return any ? new Rect(x1, y1, x2 - x1, y2 - y1) : Rect.Empty;
        }

        RenderTargetBitmap RenderContent()
        {
            world.UpdateLayout();
            Rect b = ContentBounds();
            if (b.IsEmpty || b.Width < 1 || b.Height < 1) return null;
            const double pad = 36;
            double w = b.Width + pad * 2, h = b.Height + pad * 2;

            var oldTransform = world.RenderTransform;
            var oldBg = world.Background;
            var tg = new TransformGroup();
            tg.Children.Add(new TranslateTransform(-b.X + pad, -b.Y + pad));
            tg.Children.Add(new ScaleTransform(ExportScale, ExportScale));
            world.RenderTransform = tg;
            world.Background = Brushes.White;
            world.UpdateLayout();

            var rtb = new RenderTargetBitmap((int)Math.Ceiling(w * ExportScale), (int)Math.Ceiling(h * ExportScale), 96, 96, PixelFormats.Pbgra32);
            rtb.Render(world);

            world.RenderTransform = oldTransform;
            world.Background = oldBg;
            world.UpdateLayout();
            return rtb;
        }

        void ExportImage(bool jpeg)
        {
            if (editing != null) CommitEdit();
            var rtb = RenderContent();
            if (rtb == null) { MessageBox.Show(T("画布是空的，没什么可导出。", "Nothing to export."), "ThoughtCanvas"); return; }
            string ext = jpeg ? "jpg" : "png";
            var dlg = new SaveFileDialog { Filter = jpeg ? "JPEG 图片 (*.jpg)|*.jpg" : "PNG 图片 (*.png)|*.png", FileName = curName + "." + ext };
            if (dlg.ShowDialog() != true) return;
            try
            {
                BitmapEncoder enc = jpeg ? new JpegBitmapEncoder { QualityLevel = 92 } : (BitmapEncoder)new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(rtb));
                using var fs = File.Create(dlg.FileName);
                enc.Save(fs);
            }
            catch (Exception ex) { MessageBox.Show(T("导出失败：", "Export failed: ") + ex.Message); }
        }

        void ExportPdf()
        {
            if (editing != null) CommitEdit();
            var rtb = RenderContent();
            if (rtb == null) { MessageBox.Show(T("画布是空的，没什么可导出。", "Nothing to export."), "ThoughtCanvas"); return; }
            var dlg = new SaveFileDialog { Filter = "PDF (*.pdf)|*.pdf", FileName = curName + ".pdf" };
            if (dlg.ShowDialog() != true) return;
            try
            {
                // 先编码为 JPEG
                byte[] jpeg;
                using (var ms = new MemoryStream())
                {
                    var enc = new JpegBitmapEncoder { QualityLevel = 90 };
                    enc.Frames.Add(BitmapFrame.Create(rtb));
                    enc.Save(ms);
                    jpeg = ms.ToArray();
                }
                File.WriteAllBytes(dlg.FileName, BuildPdf(jpeg, rtb.PixelWidth, rtb.PixelHeight));
            }
            catch (Exception ex) { MessageBox.Show(T("导出失败：", "Export failed: ") + ex.Message); }
        }

        // 极简单页 PDF：内嵌一张 JPEG（DCTDecode）。逐字节记录 xref 偏移，保证有效。
        static byte[] BuildPdf(byte[] jpeg, int pxW, int pxH)
        {
            double ptW = pxW * 72.0 / 96.0, ptH = pxH * 72.0 / 96.0;   // 像素 → 点
            var enc = Encoding.GetEncoding("ISO-8859-1");
            using var outMs = new MemoryStream();
            var offsets = new List<long>();
            void W(string s) { var by = enc.GetBytes(s); outMs.Write(by, 0, by.Length); }
            void Obj(string body) { offsets.Add(outMs.Position); W(body); }

            string F2(double d) => d.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

            W("%PDF-1.4\n%âãÏÓ\n");
            Obj("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
            Obj("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");
            Obj($"3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {F2(ptW)} {F2(ptH)}] " +
                "/Resources << /XObject << /Im0 4 0 R >> >> /Contents 5 0 R >>\nendobj\n");
            // 图像对象（头 + 原始 jpeg 字节 + 尾）
            offsets.Add(outMs.Position);
            W($"4 0 obj\n<< /Type /XObject /Subtype /Image /Width {pxW} /Height {pxH} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {jpeg.Length} >>\nstream\n");
            outMs.Write(jpeg, 0, jpeg.Length);
            W("\nendstream\nendobj\n");
            // 内容流：铺满整页
            string content = $"q\n{F2(ptW)} 0 0 {F2(ptH)} 0 0 cm\n/Im0 Do\nQ\n";
            Obj($"5 0 obj\n<< /Length {enc.GetByteCount(content)} >>\nstream\n{content}endstream\nendobj\n");

            long xref = outMs.Position;
            W($"xref\n0 6\n0000000000 65535 f \n");
            foreach (var off in offsets) W(off.ToString("0000000000") + " 00000 n \n");
            W($"trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF");
            return outMs.ToArray();
        }
    }
}
