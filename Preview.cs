namespace BmapUiEditor
{
    /// <summary>
    /// 真实预览：BaseCss 逐字复刻 ThoughtCanvas index.html 的 PREVIEW_BASE_CSS；
    /// TestCss/TestMarkup 是 V0.0.3 加的"可试玩预览"——大括号/连线/菜单/浮条全都在，
    /// 卡片文字可直接输入，变长变高时括号和连线实时跟着动。
    /// </summary>
    public static class Preview
    {
        public const string BaseCss = """
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

        public const string TestCss = """
#stage{display:block;}
.node{position:absolute;}
.card-inner{outline:none;min-width:40px;white-space:pre-wrap;}
#wire{position:absolute;left:0;top:0;width:100%;height:100%;pointer-events:none;}
.brace-path{stroke:var(--brace);fill:none;}
.link-line{stroke:var(--brace);}
.anchor{fill:var(--card);stroke:var(--accent);stroke-width:1.6;}
.popmenu{position:absolute;background:#fff;border:1px solid var(--card-border);border-radius:10px;box-shadow:0 8px 24px rgba(0,0,0,.12);padding:6px;min-width:150px;font-size:13px;}
.mi{padding:7px 12px;border-radius:7px;color:var(--ink);cursor:pointer;}
.mi:hover,.mi.active{background:var(--accent-soft);color:var(--accent);}
.msep{height:1px;background:var(--card-border);margin:5px 6px;}
#floatBar{position:absolute;left:50%;bottom:18px;transform:translateX(-50%);display:flex;gap:8px;background:#fff;border:1px solid var(--card-border);border-radius:12px;padding:6px 10px;box-shadow:0 4px 16px rgba(0,0,0,.10);}
.act{padding:4px 10px;border-radius:8px;color:var(--ink);cursor:pointer;font-size:15px;}
.act:hover{background:var(--accent-soft);color:var(--accent);}
.pv-hint{position:absolute;left:24px;bottom:16px;font-size:12px;color:var(--muted);pointer-events:none;}
""";

        public const string TestMarkup = """
<div id="toolbar"><span class="title"><span class="dot"></span>ThoughtCanvas</span>
<span class="vsep"></span><button class="btn">📁 文件 <span class="caret">▾</span></button>
<button class="btn primary">＋ 文本框</button><button class="btn ghost">✨ 整理</button>
<span class="fname">未命名.bmap</span></div>
<div class="stage" id="stage">
  <svg id="wire">
    <path id="bracePath" class="brace-path" stroke-width="2.5"/>
    <line id="l1" class="link-line" stroke-width="2"/>
    <line id="l2" class="link-line" stroke-width="2"/>
    <circle id="a0" class="anchor" r="5"/>
    <circle id="a1" class="anchor" r="5"/>
    <circle id="a2" class="anchor" r="5"/>
  </svg>
  <div class="node sel" id="n0" style="left:70px;top:110px;"><div class="card"><div class="card-inner" contenteditable="true" spellcheck="false">中心主题</div></div></div>
  <div class="node" id="n1" style="left:360px;top:64px;"><div class="card"><div class="card-inner" contenteditable="true" spellcheck="false">分支节点</div></div></div>
  <div class="node" id="n2" style="left:360px;top:186px;"><div class="card"><div class="card-inner" contenteditable="true" spellcheck="false">分支节点二</div></div></div>
  <div class="node" id="n3" style="left:70px;top:330px;"><div class="card"><div class="card-inner" contenteditable="true" spellcheck="false">蜘蛛网节点</div></div></div>
  <div class="node" id="n4" style="left:380px;top:396px;"><div class="card"><div class="card-inner" contenteditable="true" spellcheck="false">连线目标</div></div></div>
  <div class="popmenu" style="right:36px;top:40px;">
    <div class="mi">✏ 编辑文字</div>
    <div class="mi active">🎨 调整颜色</div>
    <div class="msep"></div>
    <div class="mi">🗑 删除节点</div>
  </div>
  <div id="floatBar"><span class="act">↺</span><span class="act">↻</span><span class="act">🗑</span></div>
  <span class="pv-hint">💡 点卡片文字直接打字（可换行），变长变高时大括号和连线会跟着动</span>
</div>
<script>
(function(){
  function box(id){
    var st = document.getElementById('stage').getBoundingClientRect();
    var b = document.getElementById(id).getBoundingClientRect();
    return { l:b.left-st.left, t:b.top-st.top, r:b.right-st.left, b:b.bottom-st.top,
             cx:(b.left+b.right)/2-st.left, cy:(b.top+b.bottom)/2-st.top };
  }
  function bracePath(x,yT,yB,yM){
    if(yM < yT+16) yM = yT+16;
    if(yM > yB-16) yM = yB-16;
    return 'M '+(x+14)+','+yT+' C '+(x+3)+','+yT+' '+x+','+(yT+8)+' '+x+','+(yT+20)
      +' L '+x+','+(yM-14)+' C '+x+','+(yM-5)+' '+(x-3)+','+(yM-2)+' '+(x-9)+','+yM
      +' C '+(x-3)+','+(yM+2)+' '+x+','+(yM+5)+' '+x+','+(yM+14)
      +' L '+x+','+(yB-20)+' C '+x+','+(yB-8)+' '+(x+3)+','+yB+' '+(x+14)+','+yB;
  }
  function seta(id,attrs){ var e=document.getElementById(id); for(var k in attrs) e.setAttribute(k, attrs[k]); }
  function upd(){
    var p=box('n0'), c1=box('n1'), c2=box('n2');
    var x = Math.min(c1.l, c2.l) - 22;
    seta('bracePath', { d: bracePath(x, c1.cy, c2.cy, p.cy) });
    var s=box('n3'), t=box('n4');
    seta('l1', { x1:s.r, y1:s.cy, x2:t.l, y2:t.cy });
    seta('l2', { x1:s.r, y1:s.cy, x2:c2.l, y2:c2.b });
    seta('a0', { cx:s.r, cy:s.cy });
    seta('a1', { cx:t.l, cy:t.cy });
    seta('a2', { cx:c2.l, cy:c2.b });
  }
  var cards = document.querySelectorAll('.card-inner');
  for (var i=0;i<cards.length;i++) cards[i].addEventListener('input', upd);
  if (window.ResizeObserver){
    var ro = new ResizeObserver(upd);
    var cs = document.querySelectorAll('.card');
    for (var j=0;j<cs.length;j++) ro.observe(cs[j]);
    ro.observe(document.getElementById('stage'));
  }
  window.addEventListener('resize', upd);
  upd();
})();
</script>
""";
    }
}
