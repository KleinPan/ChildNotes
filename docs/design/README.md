# 设计文档

本目录是 ChildNotes 设计相关内容的唯一权威入口。原设计语言、Design System 与工程侧设计系统草稿中的重复内容已经合并为以下 4 份规范；历史原文保留在 `../archive/`。

## 当前规范

| 文档 | 说明 | 合并来源 |
|---|---|---|
| [`design-language.md`](design-language.md) | 产品体验、设计理念、交互、平台适配与 AI 体验原则 | `design-language-v1/README.md`、`product-manual.md`、`brand-experience.md`、`interaction-language.md`、`platform-guidelines.md`、`ai-experience.md` |
| [`design-system.md`](design-system.md) | 视觉系统、品牌、色彩、布局、动效与可访问性 | `DesignSystem/`、`ui-design-system.md`、`ui-foundation.md`、`theme-system.md`、`motion-system.md` |
| [`components.md`](components.md) | 组件分类、核心组件、AI 组件和实现映射 | `components.md`、`component-visual-specification.md`、`component-library-specification.md`、`code-mapping-specification.md` |
| [`design-tokens.md`](design-tokens.md) | 颜色、字体、间距、圆角、阴影、动效 Token | `design-token-specification.md`、`figma-ready-specification.md` |

## 维护规则

- 新设计决策优先写入本目录的当前规范，不再新增平行的 Design System / Design Language 文档。
- 大段历史资料、阶段性评审和废弃方案放入 `../archive/`。
- 如果设计规范影响代码实现，请同步更新 `components.md` 中的实现映射或在相关代码注释中标明 Token 名称。
