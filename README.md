# ThoughtCanvas V0.0.8

ThoughtCanvas 是一款本地优先的 Windows 思维导图与思维画布工具。文档使用开放的 `.bmap` JSON 格式，不强制登录，也不依赖云端保存。

## 本版本功能

- 大括号图、蜘蛛网图、逻辑图、组织结构图、树状图、时间轴、鱼骨图、矩阵和树形表格，并支持混合分支结构。
- 多画布、自由拖动、磁吸、智能整理、大纲视图、任务、甘特图、图片、标签、备注、链接与导出。
- 文本框样式库：内置样式、自定义样式、单框字体与输入规则、批量应用、默认样式和删除自定义样式。
- 自定义样式使用用户画出的文字区域定位；装饰图层、图片、剪切、蒙版和文字区域在同一坐标系中缩放。
- 两种尺寸策略：固定长宽比、自由拉伸。短文字先使用现有文字区域，溢出后才扩大。
- 可在结构节点上放置文本框，也支持 AI 生成带节点文本框的导图。
- 可连接配套的 BMAP 文本框样式编辑器，保存后自动刷新样式库。
- AI 助手为可选功能：模型地址和 API Key 由用户自行配置，软件不内置模型或账号。

## 安装与运行

面向普通用户的文件是 `ThoughtCanvas-Setup-0.0.8.exe`。安装包为 Windows x64 自包含版本，目标电脑不需要预装 Node.js 或 Electron。

安装包未做商业代码签名，Windows SmartScreen 可能显示未知发布者提示。发布者应同时提供本仓库源码、SHA-256 校验值和许可证文件，供用户核对。

## 从源代码运行

需要 Node.js LTS 和 npm：

```powershell
npm ci
npm start
```

构建 Windows 安装包：

```powershell
npm run dist:win:installer
```

也可使用 `npm run dist:win:portable` 构建便携版。生成目录 `dist/`、依赖目录 `node_modules/` 以及本地打包缓存不应提交到 GitHub。

## 自动测试

```powershell
npm run test:text-styles
npm run test:text-input
npm run test:user-style
npm run test:node-textboxes
npm run test:sizing-modes
npm run test:wpf-style-parity
npm run test:text-region-roundtrip
npm run test:visual-unified
npm run test:real-style-sizing
npm run test:performance
```

涉及窗口渲染的测试会启动 Electron；测试期间不要同时修改样式库或关闭测试窗口。

## 文件与隐私

- `.bmap` 文件和自定义样式默认保存在用户选择的位置或程序数据目录。
- 源码中的 `text-styles/custom-text-styles.json` 是空白发布基线，不包含开发电脑上的个人样式。
- API Key 不应写进源码、截图、发行 ZIP 或问题报告。若使用第三方 AI 服务，请同时遵守该服务的隐私政策与计费规则。
- 主程序只打包开放许可的 Press Start 2P 字体；其他字体从 Windows 已安装字体中选择或使用系统回退。

## English summary

ThoughtCanvas is a local-first Windows mind-mapping application using an open JSON-based `.bmap` format. V0.0.8 adds custom text-box styles, per-box typography and input rules, node-attached text boxes, and live style-library synchronization with the companion WPF editor. The Windows installer is self-contained and unsigned.

## 许可证

项目许可证与第三方许可已单独保存，请分别阅读 [LICENSE](LICENSE) 和 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。
