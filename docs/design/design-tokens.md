# Design Tokens

Design Token 是 ChildNotes 视觉系统和代码实现之间的桥梁。本文件是所有具体视觉数值的**唯一权威来源**：色值、字号、字重、行高、间距、圆角、阴影、边框、Overlay、控件尺寸、Breakpoint。所有 UI 必须优先使用本文件定义的语义 Token。

## 设计方向

视觉关键词：像一本现代化的宝宝成长手账，而不是医疗管理软件。整体方向：温暖、安全、可信赖、轻量、不制造焦虑。

最终视觉目标：微信的易用性 + Notion 的信息结构 + 宝宝成长 App 的温暖感 + 恰当的智能感。

> 设计哲学与品牌方向见 [`design-language.md`](design-language.md)。本文件只负责工程级数值落地。

## Token 分类

```text
Design Token
├── Color（Brand / Secondary / Surface / Text / Semantic / Action / Border / Overlay）
├── Typography（Font Family / Font Scale / Font Weight / Line Height）
├── Spacing
├── Radius
├── Shadow
├── Motion
├── Size（Control Height / Icon）
└── Breakpoint
```

## Color

### Brand 品牌色

| Token | 色值 | 用途 |
|---|---|---|
| `color.brand.primary` | `#00C875` | 主按钮、激活状态、重要操作 |
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

| Token | 色值 | 用途 |
|---|---|---|
| `color.text.primary` | `#1F2937` | 标题、核心数据 |
| `color.text.secondary` | `#6B7280` | 描述、辅助信息 |
| `color.text.placeholder` | `#9CA3AF` | 输入提示 |
| `color.text.success` | `#16A34A` | 成功文本 |
| `color.text.warning` | `#D97706` | 警告文本 |
| `color.text.error` | `#DC2626` | 错误文本 |
| `color.text.onPrimary` | `#FFFFFF` | 品牌色底上的文字/图标 |

### Semantic 语义色

Semantic Token 是语义颜色的权威来源，`color.text.success/warning/error` 是它们在文本场景下的别名映射，避免开发时在两套 Token 之间产生歧义。

| Token | 色值 | 用途 |
|---|---|---|
| `color.semantic.success` | `#16A34A` | 已完成/已保存/正向状态（Icon、Border、Background 语义颜色扩展均以此为准） |
| `color.semantic.warning` | `#D97706` | 谨慎用于需注意场景 |
| `color.semantic.error` | `#DC2626` | 仅在需要纠正或保护时使用 |
| `color.semantic.info` | `#3B82F6` | 信息提示 |

> 文本场景的 `color.text.success` / `color.text.warning` / `color.text.error` 分别映射到对应 Semantic Token，不再单独维护色值。

### Action 交互状态色

Action Token 把控件状态从 Brand/Surface 派生为明确语义，组件不得自行计算 Hover/Pressed/Disabled 色值。

#### Primary Action

| Token | 值 / 来源 | 用途 |
|---|---|---|
| `color.action.primary.default` | `color.brand.primary` (`#00C875`) | 默认状态 |
| `color.action.primary.pressed` | `color.brand.primaryDark` (`#00A85A`) | 按下状态 |
| `color.action.primary.hover` | `#00B86B`（Desktop） | 悬停状态 |
| `color.action.primary.disabledBackground` | `color.surface.disabled` (`#F3F4F6`) | 禁用背景 |
| `color.action.primary.disabledForeground` | `color.text.placeholder` (`#9CA3AF`) | 禁用文字和图标 |
| `color.action.primary.foreground` | `color.text.onPrimary` (`#FFFFFF`) | 主按钮文字和图标（非禁用态） |

#### Secondary Action

| Token | 值 | 用途 |
|---|---|---|
| `color.action.secondary.background` | `#F3F4F6` | 默认背景 |
| `color.action.secondary.foreground` | `#374151` | 文字和图标 |
| `color.action.secondary.hover` | `#E7E8EB`（Desktop） | 悬停状态 |
| `color.action.secondary.pressed` | `#D1D3D7` | 按下状态 |
| `color.action.secondary.disabled` | `#F3F4F6` | 禁用背景 |
| `color.action.secondary.disabledForeground` | `color.text.placeholder` (`#9CA3AF`) | 禁用文字和图标 |

Hover 规则：

- **Mobile** 不要求 Hover 状态。
- **Desktop** 可提供 Hover 状态，Hover 色由平台主题或组件 Style 基于 `color.action.*.default` 统一处理，业务页面不得自行定义。

### Border 边框色

| Token | 建议初始值 | 用途 |
|---|---|---|
| `color.border.subtle` | `#EEF0F3` | Card、Divider、低干扰边界 |
| `color.border.default` | `#E5E7EB` | Input、Select、普通可编辑控件 |
| `color.border.focus` | `color.brand.primary` (`#00C875`) | Input Focus、Desktop 键盘焦点 |
| `color.border.error` | `color.text.error` (`#DC2626`) | 输入校验错误 |

使用原则：

- 不要默认给所有 Card 加明显边框。优先使用 Background、Surface、Spacing 区分层级。
- Border 仅用于需要明确边界的场景。
- Focus 状态必须独立定义（`color.border.focus`），不得只依赖颜色变化或阴影。
- Error 状态不能只改变文字颜色，应同时有明确状态反馈（边框 + 文字）。

### Overlay 遮罩

| Token | 值 | 用途 |
|---|---|---|
| `color.overlay.scrim` | `rgba(0, 0, 0, 0.32)` | Bottom Sheet / Modal / Dialog 背景遮罩 |

规则：

- Bottom Sheet、Modal、Dialog 默认复用该 Token。
- 不允许页面自行定义 30%、40%、50% 不同遮罩。
- 如未来确实存在特殊全屏沉浸场景，再单独定义新的 Overlay Token。

### 关于 AI 色

**不建立独立的 AI 品牌色体系。** AI 相关 UI 默认复用 `color.brand.*` / `color.surface.*` / `color.text.*` / `color.semantic.*`，通过 AI 图标、清晰文案、状态变化、Loading 状态、内容来源标识表达 AI 能力。

禁止为了强调 AI 单独引入：高饱和紫蓝色、大面积渐变、Glow、发光边框或独立的 AI SaaS 视觉体系。

> 例外：如果未来确实出现必须长期存在的 AI 专属视觉状态，再新增明确命名、明确数值、明确用途的 AI Token，不保留开放式的 `color.ai.*`。

## Typography

### Font Family

优先使用系统默认字体：

| 平台 | 字体 |
|---|---|
| Android | Roboto / Noto Sans SC |
| iOS | PingFang SC |
| Windows | Microsoft YaHei |

### Font Scale

字号必须是确定值，不允许 `16-18sp` 这类范围型 Token。

| Token | 字号 | 字重 | 用途 |
|---|---|---|---|
| `font.size.largeTitle` | 24sp | Bold | 页面主标题 |
| `font.size.sectionTitle` | 20sp | Bold | 大模块标题 |
| `font.size.cardTitle` | 18sp | Medium | Card 标题、重要区块标题 |
| `font.size.body` | 16sp | Regular | 正文、主要内容 |
| `font.size.label` | 14sp | Medium | Button、Tag、Label |
| `font.size.caption` | 12sp | Regular | 时间、日期、辅助元数据 |

### Font Weight

| Token | 用途 |
|---|---|
| `font.weight.regular` | 正文 |
| `font.weight.medium` | 标题、按钮、标签 |
| `font.weight.bold` | 页面/模块主标题 |

### Line Height

| Token | 倍率 | 用途 |
|---|---|---|
| `line.height.tight` | 1.2 | 标题、单行强调内容 |
| `line.height.normal` | 1.5 | 正文、普通说明 |
| `line.height.relaxed` | 1.7 | 长文本、回忆内容、AI 总结 |

规则：

- `caption` 仅用于辅助元数据，不允许用于主要正文。
- Button 文字统一使用 `font.size.label`，不要页面自行指定字号。
- 中文环境优先系统字体。

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
- 卡片间距：使用 `spacing.md` 或 `spacing.lg`
- 大模块间距：24px

## Radius

圆角表达层级和亲和感，不是单纯追求"可爱"。不要所有组件都使用大圆角。

| Token | 值 | 用途 |
|---|---|---|
| `radius.xs` | 4px | Checkbox、Progress 内部元素等极小元素 |
| `radius.small` | 8px | Tag、Chip、小型状态元素 |
| `radius.medium` | 12px | Input、Secondary Button、普通控件、小型操作容器 |
| `radius.large` | 16px | 普通 Card、List Item、主要内容容器 |
| `radius.xl` | 24px | Bottom Sheet、大型强调 Card、特殊浮层 |
| `radius.pill` | 999px | Pill Button、胶囊标签 |

使用规则：

- 不允许业务页面自行定义 10px、14px、18px 等圆角。
- 普通 Card 默认使用 `radius.large`。
- Input 和普通控件默认使用 `radius.medium`。
- Bottom Sheet 默认使用 `radius.xl`。
- Primary Button 使用 `radius.pill`（不要直接写死 24px 圆角，用 Token）。

### Image Radius

宝宝日记照片很多，图片圆角必须统一，不允许业务页面自行定义。

| 场景 | Token |
|---|---|
| 头像 | `radius.pill` |
| 小缩略图 | `radius.medium` |
| 普通照片 Card | `radius.large` |
| 全屏图片预览 | 不强制圆角 |

规则：照片圆角与所在容器保持一致时，优先继承容器 Token。不要所有图片都裁成圆形。

## Shadow

Shadow 只用于表达空间层级，不用于装饰。优先使用 Surface 差异和 Spacing 表达层级，Shadow 是辅助。

| Token | X | Y | Blur | Spread | Color | 用途 |
|---|---|---|---|---|---|---|
| `shadow.none` | 0 | 0 | 0 | 0 | transparent | 默认，用空间和背景区分层级 |
| `shadow.card` | 0 | 2 | 8 | 0 | `rgba(0, 0, 0, 0.08)` | 普通 Card 在确实需要浮起感时使用 |
| `shadow.floating` | 0 | 4 | 16 | 0 | `rgba(0, 0, 0, 0.12)` | Floating Action Button、悬浮操作栏、高于普通 Card 的轻量浮层 |
| `shadow.modal` | 0 | 8 | 24 | 0 | `rgba(0, 0, 0, 0.15)` | Modal、特殊浮层 |

规则：

- 不要求所有 Card 都有 Shadow。默认优先使用 Surface 差异和 Spacing。
- 禁止：大面积重阴影、多层阴影叠加、发光阴影、每张 Card 默认明显浮起。

## Motion

动画统一管理。每个 Token 对应确定值，不使用范围。

### Duration

| Token | 值 | 用途 |
|---|---|---|
| `motion.duration.fast` | 150ms | 点击反馈、轻量状态切换 |
| `motion.duration.normal` | 250ms | Sheet 展开、卡片进入、Tab 切换、页面切换 |
| `motion.duration.slow` | 400ms | 需要情绪表达的低频动效 |

### Easing

| Token | 用途 |
|---|---|
| `motion.easing.standard` | 默认缓动 |
| `motion.easing.emphasized` | 主操作反馈 |

动效原则：Natural（自然）、Calm（平静）、Meaningful（有意义）。避免快速闪烁、强刺激动画、游戏化反馈。

## Size

### Control Height

Button / Icon Button 的 Height / MinHeight / MinWidth / Padding 一律由 Size（Small / Medium / Large）决定，不得由 Primary / Secondary / Danger 等 Visual Type 决定。详见 [`components.md`](components.md) 的"Size 与 Visual Type 正交"规则。

| Token | 值 | 用途 |
|---|---|---|
| `size.control.height.small` | 32px | Small Button / 紧凑操作 / 辅助控件 |
| `size.control.height.medium` | 40px | Medium Button（默认）/ 普通 Desktop 控件 |
| `size.control.height.large` | 48px | Large Button / Mobile 主操作默认推荐尺寸 |

规则：

- Button 的 Height 由其 Size 决定，与 Visual Type 无关。
- Mobile Primary Action 默认使用 Large（`size.control.height.large`）。
- 重要触控操作区域不得小于交互规范要求。
- 不要让业务页面自行出现 42px、44px、46px 等随机高度。

### Icon

图标自身的视觉尺寸。**Icon Size ≠ Icon Button Visual Size ≠ Touch Target**，三者不得混用。

| Token | 值 | 用途 |
|---|---|---|
| `size.icon.small` | 16px | 辅助图标（如 Card 内小标记） |
| `size.icon.medium` | 20px | 普通操作图标 |
| `size.icon.navigation` | 24px | 导航图标 |
| `size.icon.large` | 32px | 强调图标 |

> Icon Button 的 Visual Size（容器尺寸）和 Touch Target（点击区域）见 [`components.md`](components.md) 的"5.2 Icon"。40px / 48px 不是 Icon Size，而是 Icon Button Visual Size 或 Touch Target。

风格：圆润、简洁、低饱和。**Emoji 和 Icon 不混用**。

## Breakpoint

当前项目采用 Mobile → Tablet → Desktop 的响应式优先级。

| Token | 用途 |
|---|---|
| `breakpoint.mobile` | 手机，主要使用场景 |
| `breakpoint.tablet` | 平板 |
| `breakpoint.desktop` | 桌面端，更大内容空间、键盘操作 |

> **策略说明**：Breakpoint 的具体宽度数值必须根据 Avalonia 实际布局实现和目标设备范围统一确定，确定前不得由业务页面自行定义。如果后续确定具体宽度，再统一写入本文件（采用 `breakpoint.mobile` / `breakpoint.tablet` / `breakpoint.desktop` 或 `breakpoint.compact` / `breakpoint.medium` / `breakpoint.expanded` 其中一种命名，不允许混用）。

## Button Tokens

Button 的 Visual Type 只定义视觉属性（Background / Foreground / Border / Radius / 状态色），**不定义 Height / MinHeight / MinWidth / Padding**。尺寸由 Size 决定，详见 [`components.md`](components.md) 的"Size 与 Visual Type 正交"。

| Visual Type | Background | Foreground | Radius |
|---|---|---|---|
| Primary | `color.action.primary.default` | `color.action.primary.foreground` | `radius.pill` |
| Secondary | `color.action.secondary.background` | `color.action.secondary.foreground` | `radius.medium` |
| Danger | `color.semantic.error` | `color.text.onPrimary` | `radius.medium` |

Button 文字统一使用 `font.size.label` + `font.weight.medium`。

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

## Token 使用规则

所有 UI 必须优先使用已有语义 Token。

### 禁止

- 硬编码颜色
- 硬编码常规间距
- 硬编码常规圆角
- 为单个页面新增随机字号
- 组件自行计算 Hover / Pressed / Disabled 色值
- 业务页面自行定义 Shadow
- 业务页面自行定义 Border 色
- 使用未定义的视觉数值

### 新增 Token 前必须确认

- 现有 Token 是否可以满足；
- 是否属于可跨页面复用的视觉语义；
- 是否需要同步映射到 Avalonia 资源字典。

### 原则

**先复用 Token，再新增 Token；先表达语义，再定义数值。**

AI 生成 UI 的开发规则统一见 [`components.md`](components.md) 的"AI 开发规则"章节。

## 历史来源

本规范合并自：

- [`../archive/design-language-v1/design-token-specification.md`](../archive/design-language-v1/design-token-specification.md)
- [`../archive/design-language-v1/figma-ready-specification.md`](../archive/design-language-v1/figma-ready-specification.md)
- [`../archive/design-language-v1/code-mapping-specification.md`](../archive/design-language-v1/code-mapping-specification.md)
- 外部补充：`design-tokens_v1.0.md`（具体色值/字号/间距/圆角/阴影数值）
