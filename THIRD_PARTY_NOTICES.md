# 第三方组件与许可证 (Third-Party Notices)

ThoughtCanvas 自有代码采用 GPL-3.0 许可证（见 LICENSE）。下列第三方组件以原样打包在 `libs/` 内，
版权归各自作者所有，均为宽松开源许可证，使用时保留其版权与许可声明。

## JavaScript 库
| 组件 | 用途 | 许可证 |
|------|------|--------|
| jsPDF | 导出 PDF | MIT |

## UI 皮肤（CSS 框架，位于 `libs/skins/`）
| 皮肤 | 上游项目 | 许可证 |
|------|----------|--------|
| skeleton | Skeleton (dhg/Skeleton) | MIT |
| tailwind | Tailwind CSS | MIT |
| pico | Pico.css (picocss/pico) | MIT |
| water | Water.css (kognise/water.css) | MIT |
| photon | Photon (connors/photon) | MIT |
| coreui | CoreUI (free, coreui/coreui) | MIT |
| shoelace | Shoelace (shoelace-style/shoelace) | MIT |
| win98 | 98.css (jdan/98.css) | MIT |
| xp | XP.css (botoxparty/XP.css) | MIT |
| win7 | 7.css (khang-nd/7.css) | MIT |
| nes | NES.css (nostalgic-css/NES.css) | MIT |

## 字体
| 字体 | 用途 | 许可证 |
|------|------|--------|
| Press Start 2P (`libs/skins/nes/PressStart2P.ttf`) | NES 像素皮肤 | SIL Open Font License 1.1 (OFL) |

## 关于 "MS Sans Serif" 字体（已排除）
98.css / XP.css 上游会附带一份 "MS Sans Serif" 网页字体（`ms_sans_serif*.woff*`）。
该字体源自微软专有字体，重分发存在版权风险，**因此本仓库通过 `.gitignore` 将其排除、
不随仓库分发**。win98 / xp 皮肤在缺少该字体时会自动回退到系统无衬线字体，功能不受影响。
如需像素级复古观感，请自行在本地放入合法授权的同名字体文件。
