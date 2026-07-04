using System;
using System.IO;
using System.Text.Json;

namespace BmapUiEditor
{
    /// <summary>
    /// 偏好设置（持久化到 %AppData%\BMAP界面编辑器\prefs.json）。
    /// 控制"原版 UI 参考底图"的浓度、以及真实预览里未修改原版的显示方式。
    /// </summary>
    public static class Prefs
    {
        // 画布：是否显示"你还没添加的原版部件"作为半透明参考
        public static bool GhostUnadded = true;
        // 画布：原版 UI（参考底图 + 已添加部件）的显示浓度 0.05~1.0
        public static double OriginalOpacity = 0.20;

        // 真实预览：未修改的原版底图显示方式 0=完整 1=淡化 2=隐藏
        public static int PreviewOriginalMode = 1;
        // 真实预览：淡化程度（PreviewOriginalMode==1 时用）0.05~0.8
        public static double PreviewDim = 0.30;

        // 真实预览来源：0=内置模拟；1=真实软件（加载 ThoughtCanvas 的 index.html）
        public static int PreviewSource = 0;
        public static string TcPath = "";   // ThoughtCanvas index.html 路径

        // 保存时提示还有部件没绘制
        public static bool WarnUndrawn = true;

        // 高级模式：解锁"自由编辑（不限定设计框）"与"自己画文本区/固定文字"
        public static bool Advanced = false;
        // 高级模式下：是否仍限定到设计框（false=自由，超出框也照样导出）
        public static bool ConstrainFrame = true;
        // 高级模式下：文本区是否用自己打的固定文字（false=用部件本来的文字）
        public static bool CustomTextRegion = false;

        /// <summary>是否把设计裁剪到框内（普通模式恒 true；高级模式看 ConstrainFrame）。</summary>
        public static bool ClipToFrame { get { return !Advanced || ConstrainFrame; } }
        /// <summary>文本区是否允许固定文字（普通模式恒 false）。</summary>
        public static bool AllowCustomText { get { return Advanced && CustomTextRegion; } }

        /// <summary>选中/正在编辑的那块，浓度往上提但不到正常。</summary>
        public static double SelectedOpacity
        {
            get { return Math.Min(1.0, OriginalOpacity + 0.35); }
        }

        /// <summary>鼠标悬停参考底图时的提亮。</summary>
        public static double HoverOpacity
        {
            get { return Math.Min(1.0, OriginalOpacity + 0.30); }
        }

        private static readonly string PathFile = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BMAP界面编辑器", "prefs.json");

        private class Dto
        {
            public bool ghostUnadded { get; set; }
            public double originalOpacity { get; set; }
            public int previewOriginalMode { get; set; }
            public double previewDim { get; set; }
            public bool advanced { get; set; }
            public bool constrainFrame { get; set; } = true;
            public bool customTextRegion { get; set; }
            public int previewSource { get; set; }
            public string tcPath { get; set; } = "";
            public bool warnUndrawn { get; set; } = true;
        }

        public static void Load()
        {
            try
            {
                if (!File.Exists(PathFile)) return;
                var d = JsonSerializer.Deserialize<Dto>(File.ReadAllText(PathFile));
                if (d == null) return;
                GhostUnadded = d.ghostUnadded;
                OriginalOpacity = Clamp(d.originalOpacity, 0.05, 1.0, 0.20);
                PreviewOriginalMode = d.previewOriginalMode < 0 || d.previewOriginalMode > 2 ? 1 : d.previewOriginalMode;
                PreviewDim = Clamp(d.previewDim, 0.05, 0.8, 0.30);
                Advanced = d.advanced;
                ConstrainFrame = d.constrainFrame;
                CustomTextRegion = d.customTextRegion;
                PreviewSource = d.previewSource == 1 ? 1 : 0;
                TcPath = d.tcPath ?? "";
                WarnUndrawn = d.warnUndrawn;
            }
            catch { }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathFile));
                var d = new Dto
                {
                    ghostUnadded = GhostUnadded,
                    originalOpacity = OriginalOpacity,
                    previewOriginalMode = PreviewOriginalMode,
                    previewDim = PreviewDim,
                    advanced = Advanced,
                    constrainFrame = ConstrainFrame,
                    customTextRegion = CustomTextRegion,
                    previewSource = PreviewSource,
                    tcPath = TcPath,
                    warnUndrawn = WarnUndrawn
                };
                File.WriteAllText(PathFile, JsonSerializer.Serialize(d, new JsonSerializerOptions { WriteIndented = true }),
                    new System.Text.UTF8Encoding(false));
            }
            catch { }
        }

        private static double Clamp(double v, double lo, double hi, double fb)
        {
            if (double.IsNaN(v) || v < lo || v > hi) return v < lo && v > 0 ? lo : (v > hi ? hi : fb);
            return v;
        }
    }
}
