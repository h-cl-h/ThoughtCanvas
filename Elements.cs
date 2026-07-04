using System.Collections.Generic;
using System.Windows;

namespace BmapUiEditor
{
    /// <summary>一个可调的颜色属性。</summary>
    public class PropVal
    {
        public string Key;
        public string Label;
        public string Value;
        public string Default;
        public PropVal(string key, string label, string def) { Key = key; Label = label; Value = def; Default = def; }
        public bool Changed { get { return Value != Default; } }
    }

    /// <summary>画布上的一个图层元素（组件 / 图形 / 手绘 / 图片）。索引越大越在上层。</summary>
    public abstract class CanvasElement
    {
        public string Name = "元素";
        public bool Visible = true;
        public abstract string TypeId { get; }
    }

    /// <summary>原版界面部件的一个实例（位置固定=它在真实界面里的位置，颜色可改）。</summary>
    public class ComponentElement : CanvasElement
    {
        public string CompId;
        public List<PropVal> Props = new List<PropVal>();
        public override string TypeId { get { return "component"; } }
        public PropVal Prop(string key) { return Props.Find(p => p.Key == key); }
        public string Get(string key) { var p = Prop(key); return p != null ? p.Value : "#000000"; }
    }

    /// <summary>手搓图形。line 用 X,Y=起点、X2,Y2=终点，其余用 X,Y,W,H。</summary>
    public class ShapeElement : CanvasElement
    {
        public string Kind;                 // rect / roundrect / ellipse / line
        public double X, Y, W, H;
        public double X2, Y2;
        public string Fill = "#5b8def";
        public bool NoFill;
        public string Stroke = "#3a6bd8";
        public double StrokeW;
        public double Radius = 12;
        public double Opacity = 1.0;
        public bool IsMask;                 // 作为遮罩：裁剪它下面那一层（仅 rect/roundrect/ellipse）
        public override string TypeId { get { return Kind; } }
    }

    /// <summary>画笔手绘的一笔。</summary>
    public class InkElement : CanvasElement
    {
        public List<Point> Points = new List<Point>();
        public string Color = "#5b8def";
        public double Width = 3;
        public double Opacity = 1.0;
        public override string TypeId { get { return "ink"; } }
    }

    /// <summary>导入的 JPG / PNG 图片。</summary>
    public class ImageElement : CanvasElement
    {
        public string Base64;
        public string Mime = "image/png";
        public double X, Y, W, H;
        public double Opacity = 1.0;
        public override string TypeId { get { return "image"; } }
    }

    /// <summary>
    /// 文本区（内容槽）：部件里"文字放这儿"的区域。
    /// 默认绑定成部件本来的文字（节点可编辑的字 / 按钮标签）；高级模式可改成固定文字。
    /// 导出时它决定内边距 / 对齐 / 字号 / 字色，让你画的外框里能正常显示并编辑文字。
    /// </summary>
    public class TextRegionElement : CanvasElement
    {
        public double X, Y, W, H;
        public string Text = "文字";          // 编辑器里显示的示例；固定模式下=真正导出的字
        public bool CustomText;               // true=用 Text 固定文字；false=用部件本来的文字
        public double FontSize = 14;
        public string Color = "#23262e";
        public string AlignH = "left";        // left / center / right
        public string AlignV = "middle";      // top / middle / bottom
        public bool Bold;
        public override string TypeId { get { return "textregion"; } }
    }
}
