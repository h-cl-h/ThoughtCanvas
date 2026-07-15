# BMAP 文本框样式编辑器 V0.0.1

这是 ThoughtCanvas V0.0.8 的配套样式设计工具，也可以不连接主程序独立使用。应用基于 C#、WPF 和 .NET 8，界面支持中文与英文。

## 主要能力

- 矩形、圆角矩形、椭圆、直线、图片和唯一文字输入区域。
- 多图层、拖拽排序、显示、锁定、删除、框选和整体移动。
- 图层剪切与蒙版；编辑辅助线不会作为最终样式边框输出。
- 网格、图层边缘、中心、常用比例和 1:1 磁吸，也可手动输入长宽比。
- `X / Y / 宽度 / 高度` 属性编辑，画布撤销与重做快捷键。
- 字体列表读取 Windows 已安装字体，不在编辑器中打包商业字体。
- 独立保存/打开 `.bmaptextstyle`，并在独立预览中测试文字、输入规则和两种尺寸策略。
- 可选择 ThoughtCanvas 的 EXE、`main.js` 或桌面快捷方式；保存并同步后，运行中的主程序自动刷新。
- 同一主程序可保存多个自定义样式；没有有效文字区域时禁止保存与同步。

## 普通用户

运行 `BMAP-Text-Style-Editor-Setup-0.0.1.exe` 安装。该安装包为 Windows x64 自包含版本，目标电脑不需要预装 .NET。

安装包未做商业代码签名，Windows SmartScreen 可能显示未知发布者提示。请从项目正式发布页获取，并用发布页提供的 SHA-256 校验值核对。

## 开发与构建

需要 .NET 8 SDK：

```powershell
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

安装包脚本是 `installer.nsi`。它直接读取上述 `publish` 目录，不依赖已删除的实时预览目录。构建安装包还需要 NSIS 3。

## 自动测试

```powershell
dotnet run -c Release --project tests/ModelSafetySmoke/ModelSafetySmoke.csproj
dotnet run -c Release --project tests/EditorHistorySmoke/EditorHistorySmoke.csproj
dotnet run -c Release --project tests/MultiStyleSyncSmoke/MultiStyleSyncSmoke.csproj
dotnet run -c Release --project tests/PreviewRulesSizingSmoke/PreviewRulesSizingSmoke.csproj
```

## 数据与连接

- 连接配置位于 `%APPDATA%\BMAPTextStyleEditor\connection.json`。
- 异常日志位于 `%LOCALAPPDATA%\BMAPTextStyleEditor\error.log`。
- 同步到主程序的样式库位于目标程序的 `text-styles/custom-text-styles.json`。
- 打开他人提供的 `.bmaptextstyle` 前应确认来源；图片会以内嵌数据保存，文件可能较大。

## English summary

BMAP Text Style Editor is a standalone Windows x64 WPF companion for ThoughtCanvas V0.0.8. It creates layered text-box styles with one editable text region, clipping, masks, snapping, system-font selection, offline preview, undo/redo, and optional live synchronization with ThoughtCanvas.

## 许可证

项目许可证与第三方许可已单独保存，请分别阅读 [LICENSE](LICENSE) 和 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。
