# Design Tokens

Design Token 是 ChildNotes 视觉系统和代码实现之间的桥梁。所有颜色、字体、间距、圆角、阴影和动效都应优先以语义 Token 表达。

## Token 分类

```text
Design Token
├── Color
├── Typography
├── Spacing
├── Radius
├── Shadow
├── Motion
└── Breakpoint
```

## Color

| Token 族 | 用途 |
|---|---|
| `color.brand.*` | 品牌识别、主操作、重点强调 |
| `color.surface.*` | 页面背景、卡片、浮层、遮罩 |
| `color.text.*` | 主文本、次文本、禁用文本、反色文本 |
| `color.semantic.*` | 成功、警告、错误、信息状态 |
| `color.ai.*` | AI 入口、AI 总结、AI 生成中状态 |

## Typography

| Token 族 | 用途 |
|---|---|
| `font.family.*` | 平台字体族与内置字体选择 |
| `font.size.*` | 标题、正文、辅助说明、标签 |
| `font.weight.*` | 常规、强调、标题 |
| `line.height.*` | 长文本阅读和卡片摘要 |

## Spacing / Radius / Shadow

- `space.*` 用于页面边距、卡片内边距、列表间距和表单项间距。
- `radius.*` 用于按钮、输入框、卡片、Sheet 和头像。
- `shadow.*` 仅用于需要层级感的卡片、浮层和底部操作区，避免滥用。

## Motion

| Token | 用途 |
|---|---|
| `motion.duration.fast` | 点击反馈、轻量状态切换 |
| `motion.duration.normal` | Sheet 展开、卡片进入、Tab 切换 |
| `motion.duration.slow` | 需要情绪表达的低频动效 |
| `motion.easing.standard` | 默认缓动 |
| `motion.easing.emphasized` | 主操作反馈 |

## 历史来源

本规范合并自：

- [`../archive/design-language-v1/design-token-specification.md`](../archive/design-language-v1/design-token-specification.md)
- [`../archive/design-language-v1/figma-ready-specification.md`](../archive/design-language-v1/figma-ready-specification.md)
