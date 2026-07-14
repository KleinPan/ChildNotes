# 设计文档

本目录是 ChildNotes 设计相关内容的统一入口。精简后的 4 份规范用于日常查阅；`design-system-v0/` 与 `design-language-v1/` 保留为后续设计演进的指导资料，不归档。

## 当前规范

| 文档 | 说明 | 合并来源 |
|---|---|---|
| [`design-language.md`](design-language.md) | 产品体验、设计理念、交互、平台适配与 AI 体验原则 | `design-language-v1/README.md` |
| [`design-system.md`](design-system.md) | 视觉系统、品牌、色彩、布局、动效与可访问性 | `design-system-v0/README.md`、`design-language-v1/README.md` |
| [`components.md`](components.md) | 组件分类、核心组件、AI 组件和实现映射 | `design-language-v1/README.md` |
| [`design-tokens.md`](design-tokens.md) | 颜色、字体、间距、圆角、阴影、动效 Token | `design-language-v1/README.md` |

## 指导资料

| 目录 | 定位 |
|---|---|
| [`design-system-v0/`](design-system-v0/) | 后续 Design System 演进指导摘要 |
| [`design-language-v1/`](design-language-v1/) | 后续 Design Language 演进指导摘要 |

## 维护规则

- 日常查阅优先使用本目录的 4 份精简规范；需要追溯原则或扩展新规范时参考 `design-system-v0/` 与 `design-language-v1/`。
- `design-system-v0/` 与 `design-language-v1/` 是未来指导资料，不放入 `../archive/`，也不要按废弃文档处理。
- 大段历史资料、阶段性评审和废弃方案才放入 `../archive/`。
- 如果设计规范影响代码实现，请同步更新 `components.md` 中的实现映射或在相关代码注释中标明 Token 名称。
