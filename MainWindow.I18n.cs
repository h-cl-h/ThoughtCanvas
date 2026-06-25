using System.Windows;

namespace ThoughtCanvas
{
    // 中英文切换。T(zh,en) 取当前语言文案；ApplyLang 刷新固定界面。
    public partial class MainWindow
    {
        string T(string zh, string en) => lang == "en" ? en : zh;

        // 刷新顶栏等固定文案（动态生成的菜单各自用 T() 即可）
        void ApplyLang()
        {
            btnHome.Content    = T("🏠 开始页", "🏠 Home");
            btnFile.Content    = T("📁 文件 ▾", "📁 File ▾");
            btnAdd.Content     = T("＋ 文本框", "＋ Text");
            btnOutline.Content = OutlineOpen ? T("📋 大纲 ✓", "📋 Outline ✓") : T("📋 大纲", "📋 Outline");
            btnFit.Content     = T("归位", "Fit");
            btnRead.Content    = readOnly ? T("👁 阅读模式 ✓", "👁 Read ✓") : T("👁 阅读模式", "👁 Read");
            btnLink.Content    = T("🔗 连线", "🔗 Link");
            btnExitFocus.Content = T("退出聚焦", "Exit focus");
            // 开始页
            scSettings.Content = T("⚙ 设置", "⚙ Settings");
            startTitle.Text    = T("开始", "Start");
            recentTitle.Text   = T("最近使用", "Recent");
            scNewTitle.Text    = T("新建大括号思维导图", "New brace mind map");
            scNewSub.Text      = T("空白画布", "Blank canvas");
            scSpiderTitle.Text = T("新建蜘蛛网思维导图", "New spider mind map");
            scSpiderSub.Text   = T("锚点连线·自由发散", "Free anchored links");
            scOpenTitle.Text   = T("打开…", "Open…");
            scOpenSub.Text     = T("浏览 .bmap 文件", "Browse .bmap files");
            UpdateTidyBtn();
            ApplyDocType();
        }
    }
}
