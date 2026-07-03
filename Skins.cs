using System.Collections.Generic;

namespace BmapEditor
{
    public class BaseSkin
    {
        public string Id;
        public string Name;
        public string[] Files;
        public BaseSkin(string id, string name, string[] files) { Id = id; Name = name; Files = files; }
        public override string ToString() { return Name; }
    }

    public class ThemePreset
    {
        public string Name, Accent, AccentSoft, SideGrad, ThumbBlank, ThumbSample, ThumbOpen;
    }

    /// <summary>
    /// 直接照搬 ThoughtCanvas index.html 的 BUILTIN_UI / BUILTIN_SCHEMES 与预览模板，
    /// 保证本编辑器的预览与真实软件里的效果一致。
    /// </summary>
    public static class Skins
    {
        // === 12 套内置基底皮肤（写法 A 的 base 取值）；files 相对 assets/skins/ ===
        public static readonly List<BaseSkin> BuiltinUI = new List<BaseSkin>
        {
            new BaseSkin("default",  "默认（浅蓝）",        new string[0]),
            new BaseSkin("skeleton", "极简扁平·深色",       new[]{"skeleton/ui-theme.css"}),
            new BaseSkin("tailwind", "Tailwind 风格",       new[]{"tailwind/ui-theme.css","tailwind/tailwind-skin.css"}),
            new BaseSkin("pico",     "Pico.css 极简",        new[]{"pico/pico.css","pico/ui-theme.css","pico/pico-skin.css"}),
            new BaseSkin("water",    "Water.css 极简",       new[]{"water/water.css","water/ui-theme.css","water/water-skin.css"}),
            new BaseSkin("photon",   "Photon（类 macOS）",   new[]{"photon/photon.css","photon/ui-theme.css","photon/photon-skin.css"}),
            new BaseSkin("coreui",   "CoreUI 后台框架",      new[]{"coreui/coreui.min.css","coreui/coreui-skin.css"}),
            new BaseSkin("shoelace", "Shoelace 组件库",      new[]{"shoelace/shoelace-light.css","shoelace/shoelace-skin.css"}),
            new BaseSkin("win98",    "Windows 98 复古",      new[]{"win98/98.css","win98/98-skin.css"}),
            new BaseSkin("xp",       "Windows XP Luna",      new[]{"xp/XP.css","xp/xp-skin.css"}),
            new BaseSkin("win7",     "Windows 7 Aero",       new[]{"win7/7.css","win7/7-skin.css"}),
            new BaseSkin("nes",      "NES 8 位像素",         new[]{"nes/nes.css","nes/nes-skin.css"}),
        };

        public static BaseSkin FindBase(string id)
        {
            return BuiltinUI.Find(x => x.Id == id) ?? BuiltinUI[0];
        }

        // === 6 套内置配色，作为「主题色」新建时的起手模板 ===
        public static readonly List<ThemePreset> ThemePresets = new List<ThemePreset>
        {
            new ThemePreset{Name="经典蓝",     Accent="#5b8def", AccentSoft="#e8f0ff", SideGrad="160deg,#5b8def,#7b6cf0", ThumbBlank="135deg,#5b8def,#6f9bf2", ThumbSample="135deg,#7b6cf0,#9a7af2", ThumbOpen="135deg,#39b59a,#46c4a8"},
            new ThemePreset{Name="靛紫",       Accent="#5856d6", AccentSoft="#e7e7fb", SideGrad="160deg,#5856d6,#4a48c4", ThumbBlank="135deg,#5856d6,#7a78e6", ThumbSample="135deg,#4a48c4,#5856d6", ThumbOpen="135deg,#1b9e3e,#39b54a"},
            new ThemePreset{Name="翡翠绿",     Accent="#0a8a6f", AccentSoft="#e2f3ee", SideGrad="160deg,#0a8a6f,#0c6f5a", ThumbBlank="135deg,#0a8a6f,#16a085", ThumbSample="135deg,#0c6f5a,#0a8a6f", ThumbOpen="135deg,#1f9e8a,#0a8a6f"},
            new ThemePreset{Name="暖橙",       Accent="#c0560f", AccentSoft="#fbe9dd", SideGrad="160deg,#c0560f,#9c3f08", ThumbBlank="135deg,#c0560f,#e0701f", ThumbSample="135deg,#9c3f08,#c0560f", ThumbOpen="135deg,#d9893a,#c0560f"},
            new ThemePreset{Name="Windows蓝", Accent="#0078d4", AccentSoft="#e5f1fb", SideGrad="160deg,#0078d4,#005a9e", ThumbBlank="135deg,#0078d4,#2b95e0", ThumbSample="135deg,#005a9e,#0078d4", ThumbOpen="135deg,#107c10,#0b6a0b"},
            new ThemePreset{Name="玫红",       Accent="#d6336c", AccentSoft="#fde3ec", SideGrad="160deg,#d6336c,#a61e4d", ThumbBlank="135deg,#d6336c,#e64980", ThumbSample="135deg,#a61e4d,#d6336c", ThumbOpen="135deg,#7048e8,#5f3dc4"},
        };

        // === 预览基础样式与 DOM（逐字复刻 index.html 的 PREVIEW_BASE_CSS / PREVIEW_MARKUP） ===
        public const string PreviewBaseCss = """
*{box-sizing:border-box;margin:0;font-family:-apple-system,"Microsoft YaHei",sans-serif;}
:root{--bg:#f5f6f8;--grid:#e7e9ee;--card:#fff;--card-border:#e2e5ec;--ink:#23262e;--muted:#8a90a0;--accent:#5b8def;--accent-soft:#e8f0ff;--brace:#9aa3b8;--danger:#e0533d;--side-grad:linear-gradient(160deg,#5b8def,#7b6cf0);}
html,body{height:100%;width:100%;}body{background:var(--bg);color:var(--ink);font-size:14px;overflow:hidden;}
#toolbar{display:flex;align-items:center;gap:10px;height:54px;padding:0 18px;background:rgba(255,255,255,.85);border-bottom:1px solid var(--card-border);white-space:nowrap;}
.title{font-weight:700;font-size:16px;display:flex;align-items:center;gap:8px;color:var(--ink);flex-shrink:0;}
.dot{width:14px;height:14px;border-radius:50%;background:var(--accent);flex-shrink:0;}
.vsep{width:1px;height:22px;background:var(--card-border);flex-shrink:0;}
.btn{height:34px;padding:0 14px;border-radius:9px;border:1px solid var(--card-border);background:#fff;color:var(--ink);font-size:14px;cursor:pointer;display:inline-flex;align-items:center;gap:5px;white-space:nowrap;flex-shrink:0;}
.btn.primary{background:var(--accent);color:#fff;border-color:var(--accent);}
.btn.ghost{background:transparent;border-color:transparent;}
.fname{margin-left:auto;color:var(--muted);font-size:13px;flex-shrink:0;white-space:nowrap;}
.stage{position:relative;height:calc(100% - 54px);display:flex;align-items:center;gap:24px;padding:0 32px;
 background:linear-gradient(var(--grid) 1px,transparent 1px),linear-gradient(90deg,var(--grid) 1px,transparent 1px);background-size:22px 22px;}
.card{background:var(--card);border:1px solid var(--card-border);border-radius:11px;box-shadow:0 1px 4px rgba(0,0,0,.07);}
.card-inner{padding:11px 18px;font-size:14px;color:var(--ink);white-space:nowrap;}
.node.sel .card{border-color:var(--accent);box-shadow:0 0 0 2px var(--accent-soft);}
""";

        public const string PreviewMarkup = """
<div id="toolbar"><span class="title"><span class="dot"></span>ThoughtCanvas</span>
<span class="vsep"></span><button class="btn">📁 文件 <span class="caret">▾</span></button>
<button class="btn primary">＋ 文本框</button><button class="btn ghost">✨ 整理</button>
<span class="fname">未命名.bmap</span></div>
<div class="stage"><div class="node sel"><div class="card"><div class="card-inner">中心主题</div></div></div>
<div class="node"><div class="card"><div class="card-inner">分支节点</div></div></div></div>
""";

        // === 开始页预览（复刻 index.html 的 .start* / .newcard / .nc-thumb，Word 风格） ===
        // sideGrad 用在大侧栏、thumbBlank/Sample/Open 用在三张卡片，accent 用在卡片高亮，
        // accentSoft 用在下方「选中项」chip——一屏内每个主题参数都在大面积上可见。
        public const string StartCss = """
:root{--shadow:0 1px 4px rgba(0,0,0,.07);--shadow-hi:0 8px 24px rgba(0,0,0,.12);--side-grad:linear-gradient(160deg,#5b8def,#7b6cf0);--thumb-blank:linear-gradient(135deg,#5b8def,#6f9bf2);--thumb-sample:linear-gradient(135deg,#7b6cf0,#9a7af2);--thumb-open:linear-gradient(135deg,#39b59a,#46c4a8);}
body{overflow:auto;}
.start{position:absolute;inset:0;display:flex;background:var(--bg);}
.start-side{width:260px;flex-shrink:0;background:var(--side-grad);color:#fff;padding:34px 28px;display:flex;flex-direction:column;}
.start-side .brand{display:flex;align-items:center;gap:12px;}
.start-side .logo{font-size:30px;line-height:1;background:rgba(255,255,255,.18);width:52px;height:52px;border-radius:14px;display:flex;align-items:center;justify-content:center;}
.start-side .bt{font-size:18px;font-weight:600;}
.start-side .bs{font-size:12px;opacity:.8;letter-spacing:1px;}
.start-side .side-sub{margin-top:22px;font-size:13px;line-height:1.9;opacity:.92;}
.start-side .side-btn{margin-top:auto;align-self:flex-start;background:rgba(255,255,255,.16);color:#fff;border:1px solid rgba(255,255,255,.32);border-radius:9px;padding:8px 16px;font-size:13px;cursor:pointer;}
.start-side .ver{margin-top:14px;font-size:12px;opacity:.7;}
.start-main{flex:1;padding:34px 40px;overflow:auto;}
.start-main .sh{font-size:15px;font-weight:600;color:#3a4050;margin:0 0 15px;}
.start-main .sh+.sh,.newrow+.sh{margin-top:32px;}
.newrow{display:flex;gap:16px;flex-wrap:wrap;}
.newcard{width:158px;border:1px solid var(--card-border);background:#fff;border-radius:14px;padding:16px;cursor:pointer;transition:all .16s ease;}
.newcard.hi{border-color:var(--accent);box-shadow:var(--shadow-hi);transform:translateY(-2px);}
.nc-thumb{height:90px;border-radius:10px;display:flex;align-items:center;justify-content:center;font-size:38px;color:#fff;margin-bottom:12px;}
.nc-thumb.blank{background:var(--thumb-blank);}
.nc-thumb.sample{background:var(--thumb-sample);}
.nc-thumb.open{background:var(--thumb-open);font-size:32px;}
.nc-t{font-size:14px;font-weight:600;color:var(--ink);}
.nc-s{font-size:12px;color:var(--muted);margin-top:3px;}
.struct-row{gap:12px;}
.struct-row .newcard{width:120px;padding-bottom:2px;}
.struct-row .nc-thumb{height:54px;font-size:26px;}
.recent{display:flex;flex-direction:column;gap:2px;max-width:560px;}
.recent .ri{display:flex;align-items:center;gap:12px;padding:10px 12px;border-radius:9px;background:#fff;box-shadow:var(--shadow);}
.recent .ri .ic{font-size:20px;}
.recent .ri .rn{font-size:14px;font-weight:500;color:var(--ink);}
.recent .ri .rp{font-size:11.5px;color:var(--muted);margin-top:1px;}
.recent .ri .chip{margin-left:auto;background:var(--accent-soft);color:var(--accent);padding:5px 12px;border-radius:8px;font-size:12px;font-weight:500;}
""";

        public const string StartMarkup = """
<div class="start">
  <div class="start-side">
    <div class="brand"><span class="logo">✦</span><div><div class="bt">ThoughtCanvas</div><div class="bs">思维画布</div></div></div>
    <div class="side-sub">大括号 · 蜘蛛网 · 多种结构图<br>自由梳理你的思路结构</div>
    <button class="side-btn">⚙ 设置</button>
    <div class="ver">配色预览</div>
  </div>
  <div class="start-main">
    <h2 class="sh">开始</h2>
    <div class="newrow">
      <div class="newcard hi"><div class="nc-thumb blank" style="font-size:30px">｛｝</div><div class="nc-t">新建大括号思维导图</div><div class="nc-s">空白画布</div></div>
      <div class="newcard"><div class="nc-thumb sample">✳</div><div class="nc-t">新建蜘蛛网思维导图</div><div class="nc-s">锚点连线·自由发散</div></div>
      <div class="newcard"><div class="nc-thumb open">📂</div><div class="nc-t">打开…</div><div class="nc-s">浏览 .bmap 文件</div></div>
    </div>
    <h2 class="sh">更多结构</h2>
    <div class="newrow struct-row">
      <div class="newcard"><div class="nc-thumb blank">⊏</div><div class="nc-t">逻辑图</div><div class="nc-s">向右·折线</div></div>
      <div class="newcard"><div class="nc-thumb blank">品</div><div class="nc-t">组织结构图</div><div class="nc-s">自上而下</div></div>
      <div class="newcard"><div class="nc-thumb blank">⫩</div><div class="nc-t">树状图</div><div class="nc-s">逐级缩进</div></div>
    </div>
    <h2 class="sh">最近使用</h2>
    <div class="recent"><div class="ri"><span class="ic">📄</span><div><div class="rn">示例.bmap</div><div class="rp">选中/悬停时用主色与选中底色</div></div><span class="chip">选中底色</span></div></div>
  </div>
</div>
""";
    }
}
