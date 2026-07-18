#The new version will be uploaded to the new repository：https://github.com/h-cl-h/KnotJot
#新版本将上传到新仓库：https://github.com/h-cl-h/KnotJot

<div align="center">
  <img src="assets/icon-256.png" width="120" alt="KnotJot">
  <h1>KnotJot</h1>
  <p><b>A flexible, local-first mind-mapping tool for Windows</b></p>
  <p>Organize ideas visually · Keep files on your computer · Make the workspace your own</p>
</div>

---

## English

### What is KnotJot?

**KnotJot (ThoughtCanvas V0.0.9)** is a free, open-source Windows desktop app for mind mapping, planning, and visual note-taking. It works without a required account, keeps core editing local, and saves maps as readable `.bmap` JSON files on your own computer.

The project is now hosted as **KnotJot**. To protect existing settings, file associations, and upgrade compatibility, the V0.0.9 installer and executable still use the **ThoughtCanvas** name.

### What you can do

- **Build the map that fits the idea** — use brace maps, spider-web maps, logic charts, org charts, trees, timelines, fishbones, matrices, and tree tables. A whole map or a single branch can use a different structure.
- **Keep several canvases in one file** — add, rename, delete, and switch sheets from the bottom tabs, or open a branch in its own sheet.
- **Arrange freely or tidy automatically** — drag nodes anywhere, adjust spacing, use alignment guides and magnetic snapping, or restore a clean layout with Smart Tidy.
- **Work quickly from the keyboard** — add children and siblings, rename and move topics, and remap shortcuts in Settings.
- **Edit in the outline** — reorganize the hierarchy, change parents, collapse sections, and update task progress in a linear view that stays synchronized with the canvas.
- **Add useful details** — attach markers, tags, notes, hyperlinks, inline images, assignees, dates, progress, and a movable marker legend.
- **Plan with tasks and Gantt view** — turn topic dates and progress into a visual schedule, then jump from a Gantt bar back to its topic.
- **Customize text boxes** — use built-in and user-created styles, control fonts and input rules, resize freely or keep a fixed aspect ratio, and apply styles to one or many boxes.
- **Change the workspace appearance** — choose built-in UI skins, import custom skins, and edit the original UI colors and canvas background from Settings.
- **Use AI only if you want it** — connect your own compatible model endpoint and API key. KnotJot does not include a model account or require AI for normal editing.

### New in V0.0.9

#### Original UI colors and canvas backgrounds

Open **Settings → Appearance** while using the original/default UI to adjust colors for the canvas, grid, cards, text, toolbar, menus, connectors, and start page. These controls are intentionally limited to the original UI so imported skins keep their own design.

You can also import PNG, JPEG, or WebP backgrounds without an app-defined file-size or image-dimension limit. Every image opens in a preview before you save it:

- **Unlimited canvas** repeats the image across the workspace at its natural size and your chosen scale.
- **Limited canvas** creates a bounded workspace, starting at **6000 × 3600**, with adjustable canvas size, image scale, and position.
- Imported images are shown without a grid over them. In the preview, the background zooms while the sample text box stays at a stable on-screen size, making the scale easier to judge.

Saved background settings become the default for new maps and are also embedded in the current `.bmap` file. Older `.bmap` files remain supported.

#### Live UI-skin updates

**BMAP UI Editor V1.0.0** can save a skin directly to KnotJot's shared UI-skin library. When connected, the current skin normally refreshes in place within about one second without reloading the map or losing the current selection and text input.

### Download and run

V0.0.9 is provided for **Windows x64**.

1. Open the repository's **Releases** page.
2. Download `ThoughtCanvas-Setup-0.0.9.exe`.
3. Run the installer and choose an installation folder.
4. Create a new map or open an existing `.bmap` file.

The current installer is not commercially code-signed, so Windows SmartScreen may show **Unknown publisher**. Verify that the file came from this repository before continuing.

### Companion editors

- **BMAP UI Editor V1.0.0** creates and live-updates complete UI skins.
- **BMAP Text Style Editor V0.0.1** creates reusable text-box styles without mixing them into UI skins.

The former color editor's original-UI color controls are now built into **Settings → Appearance**.

### Files, privacy, and compatibility

- No account or cloud storage is required for normal use.
- `.bmap` files are readable JSON and stay on the storage location you choose.
- Imported background images are embedded in map data and can make `.bmap` files much larger.
- UI skins, original-UI colors, text-box styles, and map backgrounds are stored separately so one type of customization does not overwrite another.
- AI is optional and uses the model settings you provide.

### Run from source

Install Node.js LTS and npm, then run:

```powershell
npm ci
npm start
```

Build the Windows installer with:

```powershell
npm run dist:win:installer
```

### License

KnotJot is licensed under **GPL-3.0-only**. Commercial use is allowed, but distributed derivative versions must follow the GPL requirements and provide corresponding source code. Bundled libraries and fonts keep their own compatible licenses; see [LICENSE](LICENSE) and [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

---

## 中文

### KnotJot 是什么？

**KnotJot（ThoughtCanvas V0.0.9）** 是一款免费、开源的 Windows 桌面思维导图工具，也可以用来做计划和可视化笔记。正常使用不强制登录，核心编辑功能在本地运行，导图以可读的 `.bmap` JSON 文件保存在你选择的位置。

项目现在以 **KnotJot** 为新名称和新仓库。为了保护已有设置、文件关联和升级兼容性，V0.0.9 的安装包与程序文件暂时仍沿用 **ThoughtCanvas** 名称。

### 你可以用它做什么

- **按想法选择结构** —— 支持大括号图、蜘蛛网图、逻辑图、组织结构图、树状图、时间轴、鱼骨图、矩阵和树形表格；可以切换整张导图，也可以只改变一个分支，组合成混合布局。
- **一个文件放多张画布** —— 通过底部标签新增、改名、删除和切换画布，也可以把某个分支单独放进新画布。
- **自由摆放，也能自动整理** —— 节点可以随意拖动，可调整间距、使用对齐参考线和磁吸；需要规整时，用“智能整理”恢复清晰布局。
- **用键盘快速建图** —— 快速添加子级和同级、重命名和移动主题，并可在设置中重新录制快捷键。
- **在大纲中编辑** —— 用线性方式调整层级、改变父级、折叠内容和更新任务进度，画布会保持双向同步。
- **给主题补充信息** —— 添加标记、标签、备注、超链接、节点图片、负责人、日期、进度和可拖动的标记图例。
- **用任务和甘特图做计划** —— 把主题日期与进度变成时间轴，点击甘特条即可回到对应主题。
- **自定义文本框** —— 使用内置或自制样式，调整字体和输入规则，选择自由拉伸或固定长宽比，并批量应用样式。
- **改变工作区外观** —— 切换内置 UI、导入自定义 UI，并在设置中修改原版 UI 的颜色和画布背景。
- **AI 完全可选** —— 可以连接你自己的兼容模型地址和 API Key；KnotJot 不附带模型账号，普通编辑也不依赖 AI。

### V0.0.9 新功能

#### 原版 UI 配色与画布背景

使用原版/默认 UI 时，打开 **设置 → 外观**，可以调整画布、网格、卡片、文字、工具栏、菜单、连接线和开始页等颜色。这些选项只作用于原版 UI，避免覆盖导入皮肤自己的设计。

画布背景支持 PNG、JPEG 和 WebP，程序本身不设置文件体积或图片尺寸上限。导入后会先进入预览，再决定是否保存：

- **不限制画布大小**：按图片天然尺寸和你设置的比例，在工作区中重复平铺。
- **限制画布大小**：建立有边界的工作区，默认从 **6000 × 3600** 开始，可调整画布大小、图片比例和位置。
- 导入图片后，网格不会覆盖在照片表面。预览缩放只改变背景，参考文本框保持稳定的屏幕大小，更容易判断实际比例。

保存后的背景会成为新建导图的默认背景，也会写入当前 `.bmap` 文件；旧版 `.bmap` 仍可继续打开。

#### UI 皮肤实时更新

**BMAP UI Editor V1.0.0** 可以把皮肤直接保存到 KnotJot 共用的 UI 皮肤库。连接后，当前皮肤通常会在约一秒内原位刷新，不需要重载导图，也不会丢失当前选择和正在输入的文字。

### 下载与运行

V0.0.9 面向 **Windows x64**。

1. 打开仓库的 **Releases** 页面。
2. 下载 `ThoughtCanvas-Setup-0.0.9.exe`。
3. 运行安装包并选择安装位置。
4. 新建导图，或打开已有 `.bmap` 文件。

当前安装包还没有商业代码签名，因此 Windows SmartScreen 可能显示“未知发布者”。继续之前，请确认文件确实来自本仓库。

### 配套编辑器

- **BMAP UI Editor V1.0.0**：创建完整 UI 皮肤，并实时同步到主程序。
- **BMAP Text Style Editor V0.0.1**：创建可重复使用的文本框样式，不会与 UI 皮肤混在一起。

原配色编辑器中针对原版 UI 的颜色功能，现在已经整合到主程序的 **设置 → 外观**。

### 文件、隐私与兼容性

- 正常使用不要求账号，也不强制使用云存储。
- `.bmap` 是可读的 JSON 文件，保存在你自己选择的位置。
- 导入的背景图片会嵌入导图数据，因此可能明显增大 `.bmap` 文件。
- UI 皮肤、原版 UI 配色、文本框样式和导图背景分开保存，不会互相覆盖。
- AI 是可选功能，只使用你自己提供的模型设置。

### 从源代码运行

安装 Node.js LTS 和 npm，然后执行：

```powershell
npm ci
npm start
```

构建 Windows 安装包：

```powershell
npm run dist:win:installer
```

### 许可证

KnotJot 采用 **GPL-3.0-only**。允许商用，但对外发布衍生版本时必须遵守 GPL，并提供对应源代码。打包的第三方库和字体继续使用各自兼容许可证，详见 [LICENSE](LICENSE) 和 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。
