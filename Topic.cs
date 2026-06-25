using System.Collections.Generic;
using System.Threading;

namespace ThoughtCanvas
{
    // 一个主题（文本框）。子节点形成大括号树；根节点用 X/Y（X=左缘，Y=纵向中心）。
    // 蜘蛛网模式下，所有文本框都是自由节点（用 X/Y），靠 Link 连线。
    public class Topic
    {
        public string Id;                   // 稳定 id（叠加层/联系/蜘蛛网连线都按 id 引用）
        public string Text = "";
        public string Bg = "";              // 卡片背景色（如 #fff7d6），空=默认白
        public double X, Y;                 // 根节点 / 蜘蛛网节点使用

        public List<Topic> Children = new List<Topic>();

        // —— 富节点（持久，与网页版同名字段，进 .bmap）——
        public List<string> Markers = new List<string>();   // MARKER_DEFS 的 key
        public List<string> Tags    = new List<string>();
        public string Note = "";
        public string Link = "";
        public bool Todo;                   // 是否代办项
        public bool Done;                   // 代办是否已完成

        // —— 蜘蛛网锚点（角度，弧度）：除 4 个默认边锚点外，用户自定义的锚点 ——
        public List<double> Anchors = new List<double>();

        // 布局缓存（每次排版时填充，不存盘）
        public double W, H, Sub, LX, CY;
        public string Num = "";             // 主题编号（计算得到）

        static long _seq;
        public Topic() { Id = "k" + Interlocked.Increment(ref _seq); }
    }

    // —— 第二梯队叠加层（与网页版 dumpExtras 同结构）——
    public class Boundary { public string Id; public List<string> Members = new List<string>(); public string Label = ""; public string Color = ""; }
    public class Summary  { public string Id; public List<string> Members = new List<string>(); public string Text = ""; }
    public class Callout  { public string Id; public string Tb = "";  public string Text = ""; public string Color = ""; }
    public class Relation { public string Id; public string A = "";   public string B = "";   public string Text = ""; }

    // —— 蜘蛛网连线（LK）——
    public class Link
    {
        public string Id;
        public string A, B;             // 两端文本框 id
        public double AAng, BAng;       // 锚点角度
        public string Mode = "curve";   // 连线样式
        public string Text = "";
        public double Tx, Ty, Dist;
    }

    // 整份文档：BmapIO 整体读写它（保证叠加层/连线/聚焦/编号/视图都进 .bmap 且能被撤销快照）。
    public class Document
    {
        public string DocType = "brace";    // brace | spider
        public string Name = "未命名思维导图";
        public List<Topic> Roots = new List<Topic>();
        public List<Boundary> Boundaries = new List<Boundary>();
        public List<Summary>  Summaries  = new List<Summary>();
        public List<Callout>  Callouts   = new List<Callout>();
        public List<Relation> Relations  = new List<Relation>();
        public List<Link>     Links      = new List<Link>();
        public string Numbering = "";       // "" | num | alpha | lalpha | roman
        public int CompactMode = 0;         // 0 正常 | 1 纵向压缩 | 2 横向错位
        public string FocusId = null;
        public double Scale = 1, PanX = 0, PanY = 0;
    }
}
