<div align="center">
  <img src="assets/icon-256.png" width="120" alt="ThoughtCanvas">
  <h1>ThoughtCanvas · 思维画布</h1>
  <p><b>A fully customizable, local-first, open-source thinking tool</b></p>
  <p>完全自定义 · 本地优先 · 免费开源的桌面思维工具</p>
</div>

---

## English

**ThoughtCanvas** is a free, open-source desktop mind-mapping tool. It offers two
structures — **brace maps** and **spider-web maps** — and focuses on **deep
customization** (swappable UI skins, custom colors and styles) and a
**local-first, open file format** (`.bmap`, plain JSON). No forced login, no cloud
lock-in — your data stays on your own machine.

- 🗂 **Brace maps** — dynamic braces, magnetic snapping, reverse braces
- 🕸 **Spider-web maps** — free anchor connections, smart tidy-up (radial / grid / timeline)
- 🧩 **Multiple structures** — brace, logic chart, org chart, tree, timeline, fishbone, matrix, tree-table; switch the whole map or just one branch (mixed layouts)
- 🗂 **Multiple sheets** — many canvases in one file with bottom tabs (add / rename / delete / switch); right-click a topic to open that branch in a new sheet
- ✨ **AI mind maps — read, create & edit (bring-your-own model)** — connect **your own** local model (Ollama / LM Studio / llama.cpp, any OpenAI-compatible endpoint) or a third-party **API key** (DeepSeek / OpenAI / SiliconFlow…). Chat to describe a topic: the AI first gives a **cheap rough plan + the best-fit structure**, you confirm (and can change the structure), then it generates the full map — as one page or **several pages at once**. It can also **read your current canvas** and either **edit it in place** or build a **new** map beside it (in *new* mode it only references your map and never changes or deletes it). Keeps running in the **background** if you close the panel, shows a live **token speed / usage** meter, and the whole **conversation is saved inside the `.bmap`**. Fully optional — nothing is bundled; your API key stays on your machine and is sent only to the endpoint you set (local models cost nothing). Two entry points: the **AI mind map** card on the start page, and the **✨ AI Assistant** item in the menu bar
- 📅 **Gantt view** — turns topics' task info (start/due dates + progress) into a timeline of progress bars; click a bar to jump to the topic
- 📏 **Adjustable spacing & free nudging** — a per-structure link-spacing slider, plus drag any structure node (or the small handle beside it, hidden in read mode) freely in any direction — its connector re-routes to the nearest edge and follows. Alignment guides with magnetic snapping appear when nodes line up; "Smart tidy" resets a structure back to its clean auto-layout in one click
- ⌨️ **Keyboard-first** — Tab = child, Enter = sibling, R = rename, arrows to move; every shortcut re-recordable in Settings
- 📋 **Outline view** — two-way live sync; collapse/expand (incl. expand-all / collapse-all), drag rows to reorder or re-parent, tick to-dos, inline marker/tag/progress badges, Shift+Tab to outdent
- 🏷 **Rich topics** — markers (priority 1–7 / progress pie in eighths / flags / symbols / your own custom markers), tags, notes, hyperlinks, **inline images** (pick, drag onto a node, or paste), **task info** (assignee / progress / start–due dates), and a draggable **marker legend** on the canvas. Tags and links are edited in small popovers
- 🧰 **Custom menus & toolbar** — PS-style text menu bar, SketchUp-style drag-to-customize toolbar, and an editable right-click menu (common / more / hidden tiers)
- 🎨 **Deep UI customization** — multiple built-in skins, import your own CSS skins & color schemes
- 🖼 **Free canvas** — drag nodes anywhere, one-click auto-arrange; pan with the middle mouse button or **hold Space and drag**; scroll to zoom
- 📂 **Drag & drop** — drop a `.bmap` file onto the window to open it
- 💾 **Open format** — `.bmap` is just JSON: readable, lossless
- 🖥 **Portable** — no install, no registry writes; delete to uninstall

**Run the release:** download the release folder and double-click `ThoughtCanvas.exe`
(Windows x64, no installation, no dependencies). **Build from source:** `npm install`
then `npm start`.

> Early-stage project, actively evolving.

## 中文

**ThoughtCanvas(思维画布)** 是一款免费、开源的桌面思维导图工具。提供 **大括号思维导图**
与 **蜘蛛网思维导图** 两种结构,主打 **极致自定义**(可换整套界面皮肤、自定义配色与样式)
与 **本地优先、开放的文件格式**(`.bmap`,就是 JSON)。不强制登录、不上云,数据始终在你自己电脑上。

- 🗂 **大括号思维导图** —— 动态大括号、磁吸吸附、反向括号
- 🕸 **蜘蛛网思维导图** —— 自由锚点连线、智能整理(放射 / 网格 / 时间线)
- 🧩 **多种结构** —— 大括号、逻辑图、组织结构图、树状图、时间轴、鱼骨图、矩阵、树形表格;可整图切换,也可单独给某个分支换结构(混合布局)
- 🗂 **多画布** —— 一个文件里多张画布,底部标签栏切换(新增/改名/删除);右键主题可"在新画布中展开此分支"
- ✨ **AI 生成 / 修改导图(接你自己的模型)** —— 接入 **你自己的** 本地模型(Ollama / LM Studio / llama.cpp 等任何 OpenAI 兼容接口)或第三方 **API Key**(DeepSeek / OpenAI / 硅基流动……)。对话里描述主题,AI 先给 **省算力的粗方案 + 最合适的结构**,你确认(可改结构)后再生成完整导图,可一次生成 **单页或多页**。还能 **读取你当前的画布**,选择 **就地修改** 这一页,或在旁边 **新建** 一张(新建模式只把原图当参考,绝不改动或删除它)。关掉面板它会 **在后台继续算**,底部实时显示 **token 速度与用量**,整段 **对话随 `.bmap` 一起保存**。纯可选——软件不打包任何模型,密钥只存本机、仅发往你自己填的地址(本地模型不花钱)。两个入口:开始页的 **AI 生成导图** 卡片,以及菜单栏的 **✨ AI 助手**
- 📅 **甘特图** —— 把主题的任务信息(起止日期 + 进度)变成时间轴进度条;点进度条跳到对应主题
- 📏 **间距可调 · 自由微调** —— 每种结构各有整图连线间距滑条;结构图里任意节点可**往任意方向自由拖动**(或拖它旁边的小圆手柄,阅读模式下隐藏),连线自动就近改边跟随;节点对齐时出现**对齐虚线 + 磁吸**;结构图的「智能整理」一键复位到干净自动排版
- ⌨️ **键盘流** —— Tab 加子级、Enter 加同级、R 改名、方向键移动;所有快捷键都能在设置里重新录制
- 📋 **大纲视图** —— 与导图实时双向同步;折叠/展开(含展开全部/折叠全部)、拖动行排序或换父级、代办打勾、行内显示标记/标签/进度徽章、Shift+Tab 升级
- 🏷 **富节点** —— 标记(优先级 1–7 / 八分进度饼 / 旗帜 / 符号 / 自定义标记)、标签、备注、超链接、**节点图片**(选择/拖入/粘贴)、**任务信息**(负责人 / 进度 / 起止日期),画布上还可放一张可拖动的**标记图例**;标签、链接改用小弹层编辑
- 🧰 **菜单工具栏全可定制** —— PS 式文字菜单栏、SketchUp 式拖拽自定义工具栏、右键菜单可编辑(常用 / 更多 / 隐藏 三档)
- 🎨 **极致自定义 UI** —— 内置多套皮肤一键切换,支持导入自定义 CSS 皮肤与配色方案
- 🖼 **自由画布** —— 节点随意拖放,一键智能整理回规整布局;鼠标中键或**按住空格拖动**平移画布,滚轮缩放
- 📂 **拖拽打开** —— 把 `.bmap` 文件拖进窗口即可打开
- 💾 **开放格式** —— `.bmap` 即 JSON,可读、可无损打开
- 🖥 **绿色便携** —— 免安装、不写注册表,删除即卸载干净

**运行发行版:** 下载发行版文件夹,双击 `ThoughtCanvas.exe` 即可(Windows 64 位,免安装、无环境依赖)。
**从源码运行:** `npm install` 后 `npm start`。

> 本项目处于早期阶段,仍在持续完善。

## License / 许可证

Copyright (C) 2026 happymore

自有代码采用 **[GPL-3.0](LICENSE)** 协议(copyleft:任何人可使用、修改、**商用**,但
**发布衍生版本时必须同样以 GPL 开源源码**)。打包的第三方框架与字体沿用各自许可证
(多为 MIT、字体为 OFL,均与 GPL 兼容),见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。

The author's own code is licensed under GPL-3.0 (copyleft — commercial use is allowed,
but distributed derivatives must also be released under GPL). Bundled third-party
components keep their own licenses (mostly MIT, OFL for fonts).
