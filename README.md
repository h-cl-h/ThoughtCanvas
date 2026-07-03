# BMAP Color Editor · BMAP 配色编辑器 (V0.0.3)

> English first, 中文在下。

---

## English

A lightweight desktop tool to **visually create and edit the two custom file
types used by ThoughtCanvas**:

- `.bmaptheme` — theme colors (color scheme)
- `.bmapui` — UI scheme (Mode A: base skin + accent / Mode B: raw CSS)

### Tech stack
C# WPF (.NET 8) + WebView2. The shell, color picking and file I/O use native
C# (lightweight); the preview pane embeds WebView2 (the Edge engine bundled
with Windows 11 — the browser itself is **not** packaged). The preview uses the
same Chromium engine as ThoughtCanvas, so what you see matches 100%.

### Features
- 🏠 **Start page**: opens to a home screen with **New / Open** entries and a
  **Recent files** list.
- 🎨 Create/edit **theme colors**: pick the accent visually; every other color
  is auto-derived by default and can be overridden manually. 6 built-in schemes
  as starting templates. Smart color input (`#` optional, 3-digit shorthand,
  any case).
- 🖼 Create/edit **UI schemes**: Mode A (pick one of 12 base skins + change
  accent), Mode B (full CSS editor with a sample snippet and common selectors).
- 📂 **Open an existing .bmaptheme/.bmapui to edit** — type is auto-detected and
  fields are populated.
- 👁 **Live preview** on the right, switchable between **Start page** and **Main
  UI**: the start page shows the sidebar/card gradients, the main UI shows the
  button accent and selected-item background.
- 💾 Save as `.bmaptheme` / `.bmapui`, then import in ThoughtCanvas via
  **Settings → Appearance → Import**.
- Color derivation (accentSoft = ×1.7, dark = ×0.8, light = ×1.18) is bit-for-bit
  identical to ThoughtCanvas's `hexDark`, so exports never fail to load.

### Run (release build)
Double-click `BMAP配色编辑器.exe` in the release folder (self-contained .NET
runtime, no install needed). Requires the Edge WebView2 runtime (bundled with
Windows 11).

### Build from source
Requires the .NET 8 SDK:
```
dotnet build -c Release
```
Publish a self-contained portable build:
```
dotnet publish "BMAP配色编辑器.csproj" -c Release -r win-x64 --self-contained true -o "<release-dir>"
```
(SDK on this machine lives at `%USERPROFILE%\.dotnet\dotnet.exe`)

### Folders
- `assets/skins/` — the 12 skin CSS sets copied from ThoughtCanvas V0.0.5, used
  for Mode A previews.

---

## 中文

专门用来**可视化编辑和创建 ThoughtCanvas 的两种自定义文件**的桌面小工具：

- `.bmaptheme` —— 主题色（配色方案）
- `.bmapui` —— UI 方案（写法 A 基底皮肤+主色 / 写法 B 整段 CSS）

### 技术栈
C# WPF (.NET 8) + WebView2。外壳/取色/文件读写用 C# 原生（轻量），
预览区嵌 WebView2（Win11 系统自带 Edge 内核，不打包浏览器），
预览用的正是和 ThoughtCanvas 同款 Chromium，所以效果 100% 一致。

### 功能
- 🏠 **开始页**：打开软件先进主页，有「新建 / 打开」两个入口，下面是「最近使用」文件列表。
- 🎨 新建/编辑 **主题色**：可视化取色选主色，其余颜色默认自动推导，
  每一项都能取消「自动」手动填；内置 6 套配色可作起手模板。
  颜色输入智能识别（`#` 可省略、支持 3 位简写、大小写皆可）。
- 🖼 新建/编辑 **UI 方案**：写法 A（12 套基底皮肤下拉 + 换主色）、
  写法 B（整段 CSS 编辑，带示例片段和常用选择器提示）。
- 📂 **打开现有 .bmaptheme/.bmapui 再改**，自动识别类型并回填。
- 👁 右侧 **实时预览**，可切「开始页 / 主界面」两种视图：
  开始页体现侧栏渐变与三张卡片渐变，主界面体现按钮主色与选中底色。
- 💾 保存为 `.bmaptheme` / `.bmapui`，直接丢回 ThoughtCanvas 的
  「设置 → 外观 → 导入」即可使用。
- 颜色推导逻辑（accentSoft=×1.7、暗×0.8、亮×1.18）与 ThoughtCanvas
  的 `hexDark` 逐位一致，导出后不会「无法读取」。

### 运行（成品）
双击「发行版」里的 `BMAP配色编辑器.exe`（自带 .NET 运行时，免安装）。
需要系统装有 Edge WebView2 运行时（Win11 默认自带）。

### 从源码构建
需要 .NET 8 SDK：
```
dotnet build -c Release
```
发布自带运行时的绿色版：
```
dotnet publish "BMAP配色编辑器.csproj" -c Release -r win-x64 --self-contained true -o "<发行版目录>"
```
（本机 SDK 装在用户目录 `%USERPROFILE%\.dotnet\dotnet.exe`）

### 目录
- `assets/skins/` —— 从 ThoughtCanvas V0.0.5 复制的 12 套皮肤 CSS，供写法 A 预览。
