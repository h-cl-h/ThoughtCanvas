# ThoughtCanvas · C# 原生版（源代码）

## 这是什么
ThoughtCanvas 思维导图的 **C#（WPF / .NET 8）原生 Windows 版**，版本 **V0.0.2**。
和网页版共用 **`.bmap` 文件格式**（同一个文件两边都能打开，互通）。

> **这个版本的定位 = 可行性验证（试水版）。**
> 目的是验证「不用网页技术、改用原生编译型语言把 ThoughtCanvas 重写一遍」这条路走不走得通、长得像不像、好不好用。
> 它已经把网页版的主要功能照着抄了过来（大括号图、蜘蛛网图+锚点连线、大纲、富节点、外框/概要/标注/联系/聚焦、整理、设置+自定义快捷键、导出 PNG/JPG/PDF、中英文），
> 但还**不是最终版**：换肤（网页版的招牌）在 WPF 里无法照搬，是转原生的固有取舍，留作后续。

## 用什么语言 / 需要什么环境
- **语言**：C#（界面用 WPF / XAML）。
- **框架**：.NET 8（`net8.0-windows`）。
- **编译需要**：**.NET 8 SDK**。本机已用 `dotnet-install.ps1` 装在用户目录（免管理员）：
  `%LOCALAPPDATA%\Microsoft\dotnet`，`dotnet.exe` 全路径
  `C:\Users\开心的外星人\AppData\Local\Microsoft\dotnet\dotnet.exe`。
- **运行成品不需要任何环境**：发布出来的 exe 是「自包含单文件」，自带运行时，**任何 Win10/11 双击即用，不用装 .NET**。

> ℹ️ **为什么源代码里不放一份运行环境**：源代码文件夹只放「源码」就够了。
> 编译用的 .NET SDK 是装在系统里的（上面那个路径），不属于本项目；
> 成品 exe 又会把运行时自己打包进去。所以这里**不需要、也不应该**再塞一份 .NET 运行环境进来，
> 那样只会让文件夹白白变大、还容易和系统里的版本打架。`bin/` 和 `obj/` 是编译时自动生成的临时产物，
> 已在 `.gitignore` 里排除——一编译就会重新出现，可以随时删，删了不影响源码。

## 怎么用 / 怎么编译
- **直接用**：双击同级目录 `ThoughtCanvas V0.0.2 C#版 发行版\ThoughtCanvas.exe`（单文件、约 155MB、免安装）。
- **改完代码重新生成**（在本文件夹里）：
  ```powershell
  $env:DOTNET_ROOT="$env:LOCALAPPDATA\Microsoft\dotnet"          # 让它找到运行时
  $dotnet="$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"

  & $dotnet build -c Release                                     # ① 编译检查
  & $dotnet publish -c Release -r win-x64 --self-contained true `
      -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true   # ② 出单文件 exe
  ```
  成品在 `bin\Release\net8.0-windows\win-x64\publish\ThoughtCanvas.exe`，复制进「…发行版」文件夹即可。
- **不开窗口快速自测**：`ThoughtCanvas.exe --selftest 输出.png` 会离屏渲染一张带富节点+叠加层的图（外加一张蜘蛛网图、一份 PDF），用来检查排版/绘制对不对。

## 代码结构（MainWindow 按功能拆成多个 partial 文件）
| 文件 | 作用 |
|------|------|
| `Topic.cs` | 数据模型：`Topic`（稳定 Id + 富节点/锚点字段）、叠加层、连线、整份 `Document` |
| `BmapIO.cs` | 读写 `.bmap`（整份 Document，与网页版互通） |
| `App.xaml(.cs)` | 启动、全局防闪退兜底、单实例锁、`--selftest` 开关 |
| `MainWindow.xaml(.cs)` | 主界面、大括号排版、键盘流、选择、文件、大纲 |
| `MainWindow.Overlays.cs` | 外框 / 概要 / 标注 / 联系 / 聚焦 |
| `MainWindow.Spider.cs` | 蜘蛛网（锚点 + 拖拽连线 + 智能整理） |
| `MainWindow.Settings.cs` | 设置面板 + 自定义快捷键 |
| `MainWindow.Export.cs` | 导出 PNG / JPG / PDF + 自测渲染 |
| `MainWindow.I18n.cs` | 中英文切换 |

> 更详细的进度与给后续开发者的提醒，见**项目根目录**的 `交接说明-C#版.md`、`交接说明-网页版.md`。
