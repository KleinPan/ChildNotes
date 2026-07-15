# Design Tokens

Design Token 是 ChildNotes 视觉系统和代码实现之间的桥梁。所有颜色、字体、间距、圆角、阴影和动效都应优先以语义 Token 表达，让设计和代码保持一致。

## 设计方向

视觉关键词：像一本现代化的宝宝成长手账，而不是医疗管理软件。整体方向：温暖、安全、可信赖、轻量、不制造焦虑。

最终视觉目标：微信的易用性 + Notion 的信息结构 + 宝宝成长 App 的温暖感 + AI 助手的智能感。

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

### Brand 品牌色

| Token | 色值 | 用途 |
|---|---|---|
| `color.brand.primary` | `#00C875` | 主按钮、激活状态、AI 记录入口、重要操作 |
| `color.brand.primaryLight` | `#E8FFF4` | 浅底强调（如"回到今天"按钮底色） |
| `color.brand.primaryDark` | `#00A85A` | 按下态、深色变体 |

### Secondary 业务色

| Token | 色值 | 用途 |
|---|---|---|
| `color.growth.blue` | `#3B82F6` | 成长、身高、体重 |
| `color.feeding.orange` | `#FF9F43` | 喂奶、辅食 |
| `color.sleep.purple` | `#8B7CF6` | 睡眠 |
| `color.medicine.red` | `#F56565` | 用药、异常 |
| `color.vaccine.yellow` | `#F6C344` | 疫苗提醒 |

### Surface 表面色

| Token | 色值 | 用途 |
|---|---|---|
| `color.surface.background` | `#F5F6F8` | 所有页面默认背景 |
| `color.surface.card` | `#FFFFFF` | 卡片、弹窗 |
| `color.surface.disabled` | `#F3F4F6` | 禁用背景、Secondary 按钮底 |

### Text 文字色

| Token | 色值 | 字号 | 用途 |
|---|---|---|---|
| `color.text.primary` | `#1F2937` | 18-22sp | 标题、核心数据 |
| `color.text.secondary` | `#6B7280` | 14-16sp | 描述、辅助信息 |
| `color.text.placeholder` | `#9CA3AF` | 14-16sp | 输入提示 |
| `color.text.success` | `#16A34A` | — | 成功文本 |
| `color.text.warning` | `#D97706` | — | 警告文本 |
| `color.text.error` | `#DC2626` | — | 错误文本 |

### Semantic 语义色

| Token | 用途 |
|---|---|
| `color.semantic.success` | 已完成/已保存/正向状态 |
| `color.semantic.warning` | 谨慎用于需注意场景 |
| `color.semantic.error` | 仅在需要纠正或保护时使用 |
| `color.semantic.info` | 信息提示 |

### AI 色

`color.ai.*` 用于 AI 入口、AI 总结、AI 生成中状态，与品牌色区分但保持温和可信。

品牌色要求柔和、亲近、不刺激，避免侵略性饱和度。大面积使用柔和中性背景，让照片和记忆内容突出。

## Typography

### Font Family

优先使用系统默认字体：

| 平台 | 字体 |
|---|---|
| Android | Roboto / Noto Sans SC |
| iOS | PingFang SC |
| Windows | Microsoft YaHei |

### Font Scale

| Token | 字号 | 字重 | 用途 |
|---|---|---|---|
| `font.size.largeTitle` | 24sp | Bold | 页面标题 |
| `font.size.sectionTitle` | 20sp | Bold | 模块标题 |
| `font.size.cardTitle` | 16-18sp | Medium | 卡片标题 |
| `font.size.body` | 16sp | Regular | 正文 |
| `font.size.caption` | 14sp | Regular | 时间、辅助说明 |
| `font.size.label` | — | — | 标签、按钮文字 |

| Token 族 | 用途 |
|---|---|
| `font.weight.*` | Regular / Medium / Bold |
| `line.height.*` | 长文本阅读和卡片摘要 |

原则：优先可读性、避免信息过载、避免过小文字、中文环境优先系统字体。

## Spacing

基础单位 4px，统一采用 4 的倍数，让所有页面拥有一致节奏。

| Token | 值 | 用途 |
|---|---|---|
| `spacing.xs` | 4 | micro spacing |
| `spacing.sm` | 8 | compact |
| `spacing.md` | 12 | normal |
| `spacing.lg` | 16 | standard（页面水平边距、卡片间距） |
| `spacing.lg2` | 20 | — |
| `spacing.xl` | 24 | section（大模块间距） |
| `spacing.xxl` | 32 | large section |
| `spacing.3xl` | 40 | — |
| `spacing.4xl` | 48 | page spacing |

场景规则：

- 页面水平边距（手机）：16px
- 卡片间距：12-16px
- 大模块间距：24px

## Radius

圆角体现 ChildNotes 的亲和感，整体偏大圆角。

| Token | 值 | 推荐用途 |
|---|---|---|
| `radius.small` | 8px | 按钮、小标签 |
| `radius.medium` | 16px | 普通卡片 |
| `radius.large` | 24px | Bottom Sheet |
| `radius.pill` | — | 标签、胶囊按钮 |

ChildNotes 推荐：卡片使用较大圆角，控件使用中等圆角，标签使用 Pill。Primary Button 高度 48px、圆角 24px。

## Shadow

整体采用轻阴影，不要使用明显浮雕效果。空间优先，阴影辅助。

| Token | 参数 | 用途 |
|---|---|---|
| `shadow.none` | — | 默认，用空间和背景区分层级 |
| `shadow.card` | Y:2 / Blur:8 / Opacity:8% | 卡片 |
| `shadow.modal` | Y:8 / Blur:24 / Opacity:15% | Modal、底部操作区 |

## Motion

动画统一管理：

| Token | 用途 |
|---|---|
| `motion.duration.fast` | 点击反馈、轻量状态切换 |
| `motion.duration.normal` | Sheet 展开、卡片进入、Tab 切换 |
| `motion.duration.slow` | 需要情绪表达的低频动效 |
| `motion.easing.standard` | 默认缓动 |
| `motion.easing.emphasized` | 主操作反馈 |

动效原则：Natural（自然）、Calm（平静）、Meaningful（有意义）。避免快速闪烁、强刺激动画、游戏化反馈。

## Breakpoint

用于响应式布局，优先级 Mobile → Tablet → Desktop：

| Token | 用途 |
|---|---|
| `breakpoint.mobile` | 手机，主要使用场景 |
| `breakpoint.tablet` | 平板 |
| `breakpoint.desktop` | 桌面端，更大内容空间、键盘操作 |

## Button Tokens

| 类型 | 规格 |
|---|---|
| Primary Button | 高度 48px / 圆角 24px / 文字 16sp Medium / 品牌色底 |
| Secondary Button | 背景 `#F3F4F6` / 文字 `#374151` |

## Icon Rules

| 场景 | 尺寸 |
|---|---|
| 导航 | 24px |
| 普通操作 | 20px |
| 列表 | 24-32px |
| 大功能入口 | 40-48px |

风格：圆润、简洁、低饱和。**Emoji 和 Icon 不混用**。

## Baby Theme Rules

推荐：柔和颜色、圆角、大留白、温暖插画。

禁止：医院风、大面积红色、复杂数据仪表盘、高密度表格。

## 代码映射方向

Token 应映射到各平台：

```
Design Token
      ↓
Platform Variable
      ↓
UI Component
```

示例：

```
color.brand.primary
      ↓
PrimaryColor（Avalonia 资源字典 / CSS Variable）
      ↓
Button
```

### 平台映射

| 平台 | 映射方向 |
|---|---|
| Avalonia（本项目） | Token → `App.axaml` 资源字典（`SolidColorBrush` / `x:Double` 等） → 控件 Style / Classes |
| Web | Token → CSS Variables → Vue Components |

### 开发规则

所有 UI 开发必须：

- 禁止硬编码颜色。
- 禁止重复定义间距。
- 优先复用组件。
- 保持 Token 驱动。

AI 生成 UI 的开发规则统一见 [`components.md`](components.md) 的"AI 开发规则"章节。

## 历史来源

本规范合并自：

- [`../archive/design-language-v1/design-token-specification.md`](../archive/design-language-v1/design-token-specification.md)
- [`../archive/design-language-v1/figma-ready-specification.md`](../archive/design-language-v1/figma-ready-specification.md)
- [`../archive/design-language-v1/code-mapping-specification.md`](../archive/design-language-v1/code-mapping-specification.md)
- 外部补充：`design-tokens_v1.0.md`（具体色值/字号/间距/圆角/阴影数值）
