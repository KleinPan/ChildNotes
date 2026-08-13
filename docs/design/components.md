# Components 组件规范

版本：v1.0

宝宝日记组件系统将设计语言和设计系统转化为可复用 UI 单元。新增页面应优先复用本规范中的组件语义，而不是临时创建视觉相近但行为不同的组件。

## 1. 组件设计目标

- 保持所有页面视觉一致
- 降低 UI 复杂度
- 提高 AI 开发准确性
- 支持跨平台实现

**核心原则**：

> 一个组件只负责一种信息表达。

**禁止**：

- 一个组件承担多个功能
- 页面重复造轮子
- 为单个页面创建特殊样式

## 2. 组件分层

组件按抽象层级递进：

```text
Foundation（基础规范：Color / Spacing / Radius / Typography / Shadow）
    ↓
Basic Components（基础组件：Button / Icon / Card）
    ↓
Business Components（业务组件：Baby Header / Record Card / Insight Card / Reminder Card / AI Input Bar 等）
    ↓
Page Components（页面：Home / Timeline / Record Detail / AI Experience）
```

也可按功能分类横向理解：Foundation / Content / Interaction / AI。

每个组件需定义：Purpose（用途）/ Anatomy（结构）/ Size（尺寸）/ Variant（变体）/ State（状态）/ Behavior（行为）/ Usage（使用场景）。

## 3. Foundation 基础规范

所有组件必须使用 Design Tokens，详见 [`design-tokens.md`](design-tokens.md)。

- **Color**：禁止直接写色值，必须用 `color.*` Token。
- **Spacing**：统一使用 `spacing.*` Token，禁止随意定义 13px / 17px / 27px 等。
- **Radius**：使用 `radius.*` Token（xs / small / medium / large / xl / pill），具体数值以 `design-tokens.md` 为准。**禁止在组件文档重复维护圆角数值。**
- **Typography**：使用 `font.size.*` Token，具体数值（LargeTitle 24 / SectionTitle 20 / CardTitle 18 / Body 16 / Label 14 / Caption 12）以 `design-tokens.md` 为准。**禁止使用 16-18sp 这类范围型定义。**

## 4. 组件状态

组件必须定义**适用状态**，而非机械套用全部状态。不同组件类型的状态要求不同：

### 交互组件（Button / Icon Button / Tag.Clickable 等）

按需定义：

```text
Default
Pressed
Disabled
Focus
Hover（Desktop）
```

### 异步操作组件（含网络请求的 Button / 表单提交等）

按需定义：

```text
Loading
Success
Error
```

### 数据展示组件（Record Card / Timeline / Insight Card 等）

按需定义：

```text
Loading
Empty
Error
Content
```

### 纯展示组件（Static Card / Tag / Badge 等）

只定义实际存在的状态。

**原则**：

> 不得为了满足规范机械给组件增加不合理状态。例如纯展示的 Tag 不需要 Loading / Error 状态；Static Card 不需要 Pressed 状态。

Figma 交付时还应按需覆盖 Selected / Expanded（仅适用于存在选中/展开行为的组件）。

## 5. Basic Components 基础组件

### 5.1 Button 按钮

按钮代表用户主动行为，执行明确操作。一个页面应该只有一个最重要动作。

#### 5.1.1 视觉类型（Visual Type）

按钮的视觉风格，由内容语义决定。**Visual Type 只负责视觉和语义，不负责尺寸。**

| 类型 | 使用场景 | 视觉规则（Token 引用） |
|---|---|---|
| Primary | 保存、确认、提交等核心动作 | 背景 `color.action.primary.default` / 文字 `color.action.primary.foreground` / 圆角 `radius.pill` |
| Secondary | 辅助操作 | 背景 `color.action.secondary.background` / 文字 `color.action.secondary.foreground` / 圆角 `radius.medium` |
| Tertiary | 第三级辅助操作 | 透明底 / 文字 `color.brand.primary` |
| Danger | 破坏性操作最终确认 | 背景 `color.semantic.error` / 文字 `color.text.onPrimary` / 圆角 `radius.medium` |
| Text | 轻量操作，如跳过、稍后、查看协议 | 无背景，仅文字 |
| Icon | 小型操作入口，如 + / 设置 / 关闭 | 仅图标，无文字，详见 5.2 |

**状态**：每种 Visual Type 必须定义 Default / Pressed / Disabled（及 Desktop Hover），具体色值见 [`design-tokens.md`](design-tokens.md) 的 Action Token。组件不得自行计算状态色。

> **Danger Button 使用约束**：仅在"破坏性操作的最终确认按钮"上使用。日常的危险操作入口（如列表里的"移除"链接）必须使用 Text Button（红色文字），不得使用红色实心 Button，避免大面积红色违反"温暖家庭日记"的设计方向。

#### 5.1.2 尺寸体系（Size）

| Size | Height | Horizontal Padding | Min Width | 用途 |
|---|---|---|---|---|
| Small | 32dp | 12dp | 64dp | 紧凑场景，如卡片内辅助操作 |
| Medium（默认） | 40dp | 16dp | 72dp | 普通操作 |
| Large | 48dp | 20dp | 88dp | 主操作 / Mobile Primary Action |

**关键规则**：
- 普通文字按钮**必须有 Min Width**，不允许仅根据父容器压缩。
- Mobile 主操作按钮默认使用 Large（48dp），符合触控规范。
- 重要触控操作区域不得小于 48×48dp。

**Size 与 Visual Type 正交（强制）**：

> Size 与 Visual Type 必须正交。Visual Type 不得自行覆盖 Size 的 Height、MinHeight、Padding、MinWidth。Height/Padding/MinWidth 一律由 Size 决定，Background/Foreground/Border/状态色一律由 Visual Type 决定。

合法组合示例：

```text
Primary + Small
Secondary + Medium
Danger + Large
```

组合矩阵（✓ 允许 / ✗ 禁止）：

| Visual Type \ Size | Small | Medium | Large |
|---|---|---|---|
| Primary | ✓ | ✓ | ✓ |
| Secondary | ✓ | ✓ | ✓ |
| Tertiary | ✓ | ✓ | ✓ |
| Danger | ✓ | ✓ | ✓ |
| Text | ✓ | ✓ | ✗ |
| Icon | ✓ | ✓ | ✓ |

禁止组合：
- `Text + Large`：Text Button 是轻量操作，不需要 Large 尺寸的强视觉权重。
- 未列出的组合一律禁止。如未来确需新组合，必须先在本表登记再使用。

禁止出现：
```text
Primary 永远 48dp
Secondary 永远 48dp
Small 又尝试覆盖为 32dp
```
导致最终样式优先级不确定。

#### 5.1.3 布局类型（Layout Type）

布局类型独立于视觉类型，决定按钮如何占用父容器空间。

| 布局类型 | 说明 | 适用场景 |
|---|---|---|
| Content Width | 按文字宽度 + Padding 自适应 | 工具栏、行内操作、Dialog 双按钮 |
| Fixed Width | 固定宽度 | 表单提交、特定栅格 |
| Full Width | 占满父容器宽度 | 底部 CTA、登录页主按钮 |
| Equal Width | 同一按钮组内多个按钮等宽 | Dialog 双按钮、底部双 CTA |

**组合规则**：视觉类型 × 布局类型是正交关系。例如：
- 删除确认弹窗：确认按钮 `Danger + Equal Width`，取消按钮 `Secondary + Equal Width`（两者同组等宽）
- 底部主 CTA：`Primary + Full Width`
- 卡片内复制：`Secondary + Content Width (Small)`
- 工具栏图标：`Icon + Content Width`

**禁止**只写"删除操作使用绿色主按钮"而不指定布局类型——这会让实现方自由决定宽度，导致药丸变形。

#### 5.1.4 Button Content Rules（内容规则，强制）

1. 按钮文字必须单行显示。
2. 按钮文字必须水平和垂直居中。
3. 文字按钮不得裁剪、隐藏或缩小文字。
4. 按钮宽度必须满足：`Text Width + Left Padding + Right Padding`。
5. 父容器空间不足时：优先调整按钮组布局或 Dialog 宽度，**不得压缩按钮至无法显示完整文字**。
6. Text Button 不允许退化为无文字色块。
7. 除明确规定的 Icon Button 外，Button 不允许仅剩背景而没有可见内容。

#### 5.1.5 Dialog Action Layout（弹窗操作区，强制）

当 Dialog 有 2 个操作按钮时：

- 按钮始终位于底部操作区。
- 默认水平排列。
- 两个按钮高度必须一致。
- 两个按钮宽度使用统一策略：**Equal Width 或 Content Width，不允许混用**。
- 按钮之间间距固定（推荐 12dp）。
- 不允许其中一个按钮因空间不足被压缩为圆形、椭圆或无法显示文字。
- 两个文字按钮的文字必须完整可见。
- 如果 Dialog 宽度不足以容纳两个按钮：优先增加 Dialog 宽度；若仍不足，则改为垂直排列。
- **不允许通过压缩 Button MinWidth 解决空间不足问题**。

> **强制规则**：禁止通过缩小按钮宽度、裁剪按钮文字或隐藏按钮文字来解决 Dialog 操作区空间不足。

#### 5.1.6 业务场景映射（Component Mapping）

不同业务场景必须使用对应组件，禁止"看起来像小圆角块"都用 Button。

| 业务场景 | 必须使用 | 禁止 |
|---|---|---|
| 状态信息展示（如"当前"、"主人"标记） | Tag | Button |
| 当前身份/角色展示 | Tag | Button |
| 信息复制（如复制 ID） | Small Secondary Button | Tag |
| 危险/破坏性操作入口（列表里的"移除"链接） | Text Button（红色文字） | 红色实心 Button |
| 破坏性操作的最终确认 | Danger Button | Secondary Button / 红色 Primary Button |
| 主操作（保存、确认、提交） | Primary Button | Secondary Button |
| 辅助操作（取消、返回） | Secondary Button | Primary Button |

**Danger Button 业务场景清单（确定规则，无"或"）**：

以下场景的**最终确认按钮**必须使用 Danger Button：

```text
删除
移除家庭成员
退出家庭
清空数据
不可逆操作确认
```

以下场景的**取消/返回按钮**必须使用 Secondary Button：

```text
取消
返回
```

**默认规则**：AI 不得自行将 Danger Button 降级为 Primary 或 Secondary。如存在例外，必须在本清单中明确登记具体场景，AI 不得自行判断例外。

#### 5.1.7 Correct / Incorrect 示例

**Correct ✓**
- Primary Button 最小宽度满足文字完整显示
- Dialog 双按钮高度一致、宽度统一
- 两个按钮文字始终可见
- 空间不足时调整 Dialog 宽度或改为垂直排列
- 状态信息使用 Tag 而非 Button
- 列表内危险操作入口使用 Text Button

**Incorrect ✗**
- Button 被压缩成圆形或窄胶囊
- Button 文字被裁剪
- 只剩背景颜色但文字消失
- 同一 Button Group 高度不一致
- 通过减小 MinWidth 强行塞进容器
- Dialog 双按钮一个 Full Width 一个 Content Width（混用布局策略）
- 用绿色 Primary Button 承载删除操作
- 把状态标签做成 Button

### 5.2 Icon 图标

**风格**：圆润、简洁、低饱和、易识别。

**禁止**：

- Emoji 和专业 Icon 混用。
- 不同风格图标混搭。
- 用无语义图标代替文字操作（如用 ❌ 图标代替"删除"文字而不提供 accessible label）。

#### 5.2.1 Icon Size（图标视觉尺寸）

图标自身的视觉大小，不等同于点击区域。

| Token / 值 | 用途 |
|---|---|
| 16dp | 辅助图标（如 Card 内的小标记） |
| 20dp | 普通操作图标 |
| 24dp | 导航默认 |
| 32dp | 强调图标 / 功能入口 |

#### 5.2.2 Icon Button（图标按钮）

Icon Button 是 Visual Type 的一种（见 5.1.1），只含图标不含文字。

**Visual Size**（复用 Button Size 体系，不得自定义新尺寸）：

| Size | Touch Target | Icon Size |
|---|---|---|
| Small | 32×32dp | 16-20dp |
| Medium（默认） | 40×40dp | 20-24dp |
| Large | 48×48dp | 24dp |

**Touch Target ≠ Icon Size（强制）**：

> 图标尺寸不等于点击区域。24dp 的图标可以放在 40×40dp 或 48×48dp 的 Touch Target 中。点击区域由 Button Size 决定，不由图标大小决定。

Touch Target 最低要求（详见 [`interaction.md`](interaction.md) 的 Accessibility）：

- 普通移动端交互目标 ≥ 40×40dp
- 重要核心操作 ≥ 48×48dp

**内容与可访问性**：

- Icon Button 可以只有图标，但必须提供 Tooltip（Desktop）或 Accessible Label（无障碍标签），用于屏幕阅读器朗读。
- Pressed / Disabled / Focus（Desktop）状态必须定义，规则同 Button Visual Type。
- Mobile 端不要求 Hover；Desktop 端 Hover 由平台主题统一处理。

**禁止**：

- 用无语义图标代替文字操作而不提供 accessible label。
- 让图标视觉大小决定点击区域大小。

### 5.3 Card 通用卡片

**用途**：信息分组，如 AI 状态、疫苗提醒、成长信息。

**基础规范**：

| 属性 | 值 |
|---|---|
| 背景 | `color.surface.card` |
| 圆角 | `radius.large` |
| 内边距 | `spacing.lg` |
| 阴影 | `shadow.card`（仅在需要浮起感时使用，默认可不加） |

**禁止**：强阴影、大面积悬浮效果。不要用阴影制造层级，优先用空间和背景差异。

#### 5.3.1 交互行为分类（强制）

Card 必须明确属于以下三种之一，不得处于"看起来可点但又没有明确行为"的中间态。

**Static Card（静态卡片）**：

- 用途：信息展示，不可点击。
- 状态：无 Pressed、无 Hover 交互反馈。
- 禁止：添加 Pressed 反馈或点击事件。

**Interactive Card（可交互卡片）**：

- 用途：整卡点击，有明确导航目标。
- 必须定义：Pressed / Hover（Desktop）/ Focus 状态。
- 必须有明确点击结果（跳转、展开、选中），禁止没有明确目的的整卡可点击。

**Action Card（含子操作卡片）**：

- 用途：Card 内存在独立操作（如 Button / Icon Button）。
- 事件规则：Card 内 Button 点击不得误触 Card 点击。子操作与父 Card 点击的事件必须明确隔离（事件冒泡阻断或独立命中区域）。
- 若 Card 本身也可点击，须同时满足 Interactive Card 的规则。

#### 5.3.2 嵌套规则

- 禁止过度 Card 嵌套，默认限制嵌套层级 ≤ 2 层。
- 如需表达层级，优先使用 Surface 差异和 Spacing，不通过嵌套 Card 表达。

### 5.4 Tag / Badge 标签

**用途**：状态信息展示（如"当前"、"主人"标记、身份/角色展示）。**不是操作组件。**

> 已在 5.1.6 业务场景映射中明确：状态信息使用 Tag，不使用 Button。

#### 5.4.1 Variant

| Variant | 用途 | 颜色 |
|---|---|---|
| Neutral | 默认/中性状态 | `color.surface.disabled` 底 / `color.text.secondary` 字 |
| Success | 已完成/正向状态 | `color.semantic.success` 系 |
| Info | 信息提示 | `color.semantic.info` 系 |
| Warning | 需注意 | `color.semantic.warning` 系 |
| Error | 异常/错误 | `color.semantic.error` 系 |

#### 5.4.2 Size

复用 Token，不自定义新尺寸：

| Size | Padding | Font Size |
|---|---|---|
| Small | `spacing.xs` `spacing.sm` | `font.size.caption` (12sp) |
| Medium（默认） | `spacing.sm` `spacing.md` | `font.size.label` (14sp) |

圆角统一使用 `radius.small`。

#### 5.4.3 Content

- 单行，不换行。
- 可选 Icon（放文字前），Icon Size 16dp。
- 文本字体：`font.weight.medium`。
- 最大长度：建议 ≤ 6 个中文字符。超出时使用省略号（`…`）截断，不得换行。
- 禁止因空间不足压缩到文字不可读。

#### 5.4.4 Behavior

> 默认 Tag 是**非交互展示组件**，不得使用 Button 的 Hover / Pressed 行为。

如需可点击 Tag，必须定义为独立 Variant（如 `Tag.Clickable`）或独立组件，不得默认所有 Tag 都可点击。可点击 Tag 必须定义 Pressed / Focus 状态。

#### 5.4.5 禁止事项

- Tag 不承担主要操作（用 Button）。
- Tag 不替代 Button。
- Tag 不自行定义新尺寸。
- Tag 不因空间不足压缩到文字不可读。

### 5.5 Dialog 对话框

**用途**：高风险确认、权限说明、不可恢复操作。**不是普通信息展示容器。**

> 普通信息展示用页面内反馈、Toast 或 Bottom Sheet。Dialog 只用于需要用户明确决策的场景。

#### 5.5.1 Anatomy

```text
Dialog
├── Title（标题，必须）
├── Content（内容，必须）
└── Action Area（操作区，必须）
```

#### 5.5.2 Layout

| 属性 | 规则 |
|---|---|
| 宽度 | Mobile 默认占屏宽减去左右各 `spacing.lg` (16dp)；Desktop 居中，宽度由内容决定但不超过最大宽度 |
| 最小宽度 | 不低于 280dp |
| 最大宽度 | Mobile 不超过屏宽；Desktop 不超过 480dp |
| Padding | 外边距 `spacing.lg` (16dp) |
| Title 与 Content 间距 | `spacing.md` (12dp) |
| Content 与 Action 间距 | `spacing.xl` (24dp) |
| 最大高度 | 不超过屏高 80% |
| 长内容 Overflow | Content 区域可滚动（`ScrollViewer`），Title 和 Action Area 固定不滚动 |
| 圆角 | `radius.xl` (24dp) |
| 遮罩 | `color.overlay.scrim` |

#### 5.5.3 Action Layout（操作区布局，强制）

**1 个操作**：

- Full Width（占满操作区宽度）。
- 或 Content Width + 居中对齐，二选一，由具体场景在规范中登记，不得由页面自行决定。

**2 个操作**：

- 默认水平排列，Equal Width（等宽）。
- 按钮之间间距 `spacing.md` (12dp)。
- 空间不足以满足两个按钮的 MinWidth：优先增加 Dialog 宽度；若仍不足，自动改为垂直排列。
- **禁止**压缩 Button 至低于其 MinWidth。
- **禁止**裁剪或隐藏 Button 文字。
- **禁止**一个 Full Width 一个 Content Width（混用布局策略）。

**3 个及以上操作**：

- **禁止**横向硬塞。
- 优先重新设计操作流程，减少操作数量。
- 若无法减少，必须垂直排列，每个按钮 Full Width。

#### 5.5.4 Close Behavior（关闭规则，强制）

不同 Dialog 类型的关闭规则不同，页面不得自行决定：

| Dialog 类型 | 点击遮罩关闭 | 返回键关闭 | 说明 |
|---|---|---|---|
| 普通提示 Dialog | ✓ 允许 | ✓ 允许 | 信息确认类，无破坏性 |
| 危险确认 Dialog | ✗ 禁止 | ✗ 禁止 | 必须明确点击"取消"或"确认"，避免误触遮罩导致操作丢失或误执行 |
| Loading / Submitting | ✗ 禁止 | ✗ 禁止 | 禁止重复点击操作按钮；禁止在提交完成前关闭 |

**Loading / Submitting 额外规则**：

- 操作按钮点击后立即进入 Loading 状态，禁止重复触发同一操作。
- Loading 期间 Action Area 不可关闭 Dialog（除非有明确的"取消操作"逻辑且后端支持取消）。

## 6. Business Components 业务组件

### 6.1 Baby Header ⭐

**用途**：展示宝宝身份信息。**不是数据统计面板**。

**结构**：Avatar / Baby Name / Age / Growth Info / Actions。

**示例**：

```
👶 小铃铛
8个月28天
68cm   7.6kg
```

**规则**：

- 高度：90-110dp
- 必须：温暖、简洁、留白
- 禁止：大量统计、图表、复杂操作

### 6.2 Record Card ⭐

Record Card 是 ChildNotes 最核心的视觉组件。

**定位**：一张记录孩子成长瞬间的数字日记卡片。用户看到记录时，感受到的是"回忆"，而不是数据库条目。

**结构**：

```
Record Card
├── Date
├── Baby Age
├── Title
├── Content
├── Media
├── Tags
└── AI Insight
```

**视觉原则**：应体现收藏感、回忆感、时间感；避免表格感、后台列表感。

### 6.3 Timeline Card ⭐

成长记录的主要展示方式。

**结构**：

```
Timeline Item
├── Time Point
├── Record Card
└── Connection Line
```

**特点**：时间连续、信息密度适中、支持照片/文字/AI 总结。避免表格化、后台列表感。用户浏览时间轴时应感觉"正在翻阅孩子成长故事"。

### 6.4 Record Timeline Item 时间轴记录项

**用途**：记录页面的列表项展示。

**结构**：Time / Icon / Type / Content / Value。

**示例**：

```
09:30   🍼  喂奶    120ml
```

**高度**：56-72dp。

> 与 Timeline Card 的区别：Timeline Card 用于成长页面的时间轴展示（含 Record Card），Record Timeline Item 用于记录页面的紧凑列表项。

### 6.5 Insight Card ⭐（AI 今日状态）

**用途**：展示 AI 对宝宝**当日**状态的理解。**定位：AI = 洞察能力，不是聊天窗口**。

**结构**：AI Icon / Title / Summary / Suggestion / Action。

**示例**：

```
😊 今日状态良好

吃奶规律
睡眠比昨天增加 40 分钟

              查看详情 >
```

**禁止**：不要展示「今日数据」（吃奶 6 次 / 睡眠 10 小时 / 尿布 4 次），因为属于记录模块。

**规则**：不展示完整流水、不重复记录列表、提供下一步建议。AI 输出必须可理解、可修改、可接受，不直接修改数据。

### 6.6 AI Summary Card（AI 阶段总结）

**用途**：展示 AI 对成长记录的**阶段**整理（周/月/年度总结）。

**结构**：

```
AI Summary Card
├── Summary Title
├── Period
├── Key Moments / Highlights
├── Growth Observation
├── Suggestion
├── Memory Quote
└── Action
```

**视觉方向**：温和、可信、不突出机器感。避免聊天机器人风格、技术面板风格。

> 与 Insight Card 的区别：Insight Card 是轻量的日常状态（5 字段），AI Summary Card 是深度的阶段总结（7 字段，含 Memory Quote 等）。

### 6.7 Reminder Card 提醒卡

**用途**：展示重要事项，如疫苗、体检、异常、成长提醒。

**结构**：Icon / Title / Description / Action。

**状态**：

- 有提醒 → 显示
- 无提醒 → 隐藏

不要长期固定占据页面空间。

### 6.8 Record Editor

**用途**：创建和编辑成长记录。

**原则**：输入优先，减少表单感。流程：打开 → 输入 → AI 辅助 → 确认。

### 6.9 AI Input Bar ⭐

**用途**：首页核心记录入口。

**结构**：Placeholder / + / Remaining Count。

**示例**：

```
记录宝宝今天发生的事...
              +
AI 自动分类
剩余 8 次
```

**状态**：

| 用户类型 | 额度 |
|---|---|
| 免费用户 | 今日剩余 8/10 |
| 会员 | 今日剩余 96/100 |

### 6.10 Quick Record Sheet 快速记录面板

**用途**：AI 记录的降级方案（AI 次数不足 / 用户想快速记录 / 网络异常）。

**触发方式**：点击 +。

**展示方式**：Bottom Sheet，默认隐藏。

**内容**：

```
🍼 喂奶  😴 睡眠  💩 尿布
🌡 体温  💊 用药  🥣 辅食
📏 成长
```

**禁止**：首页长期展示九宫格。首页应该保持干净。

### 6.11 Sheet / Dialog

Sheet 与 Dialog 的完整规范见：

- **Bottom Sheet**：[`interaction.md`](interaction.md) 的"Bottom Sheet 规范"（Anatomy / 高度 / 滚动 / 关闭规则 / Loading / 嵌套）。
- **Dialog**：本文件 [`5.5 Dialog`](#55-dialog-对话框)（Anatomy / Layout / Action Layout / Close Behavior）。

使用场景区分：

| 组件 | 用途 |
|---|---|
| Bottom Sheet | 记录表单、快捷选择、上下文操作 |
| Dialog | 高风险确认、权限说明、不可恢复操作 |

不应把复杂长流程塞入 Dialog。

### 6.12 Empty State

**用途**：没有数据时展示引导。

**示例**：

```
今天还没有记录
点击 + 开始记录宝宝成长
```

**规则**：不要显示空白页面，应提供温暖引导和明确下一步操作。

### 6.13 Loading 状态

**原则**：轻量。

**推荐**：骨架屏、小型 Loading。

**禁止**：大型转圈等待。

## 7. 组件交互规则

### 点击反馈

所有可点击组件必须：

- 有触摸反馈
- 有状态变化
- 动画

### 动画

| 项 | 规范 |
|---|---|
| 时长 | 短动画 200-300ms |
| 用途 | Bottom Sheet、页面切换、状态变化 |
| 禁止 | 复杂动画影响效率 |

## 8. 组件命名规则

命名使用**业务含义**。

**推荐**：BabyHeader / InsightCard / ReminderCard / RecordTimelineItem / AIInputBar / QuickRecordSheet。

**避免**：Card1 / BoxView / CustomPanel。

Figma 交付命名规则：`Category / Component / Variant`，例如 `Button / Primary / Default`、`Card / Record / Expanded`、`AI / Insight / Default`。

组件属性应对应实际业务状态，例如 Record Card 的 `Has Media` / `Has AI Insight` / `Expanded` / `Show Tags`。

## 9. 页面级结构参考

| 页面 | 结构 |
|---|---|
| Home | 详见 [`home-page.md`](home-page.md) |
| Timeline | Date Marker / Timeline Item / Record Card / AI Memory Point |
| Record Detail | Date / Age / Content / Media / Tags / AI Insight / Related Memories |
| AI Experience | Conversation / Memory Analysis / Growth Summary / Suggestions（定位：成长记忆助手空间，不是普通聊天页面） |
| Empty Page | 简短说明 + 温暖插图/视觉 + 明确下一步操作 |

## 10. 代码映射

### 映射链路

```
Design Token → Platform Token → Component Style → Application UI
```

### Token 映射

| Design Token | 工程映射 | 应用 |
|---|---|---|
| `color.brand.primary` | `PrimaryColor` | Button / Navigation / Highlight |
| `spacing.md` | `16px` | Layout / Card Padding / Component Gap |

### 组件映射

| 设计组件 | 工程组件 | 职责 |
|---|---|---|
| Button | `PrimaryButton` / `SecondaryButton` / `TextButton` / `IconButton` | 使用 Token、保持状态一致、支持主题切换 |
| Record Card | `RecordCard` | 展示成长记录、管理媒体内容、展示 AI 信息 |
| Insight Card | `InsightCard` | AI 当日状态结构化展示、用户可确认 |
| AI Summary Card | `AiSummaryCard` | AI 阶段总结输出、不直接修改数据 |
| Reminder Card | `ReminderCard` | 重要事项提醒、无提醒时隐藏 |

### 平台映射方向

- Web：Token → CSS Variables → Vue Components
- Mobile（Avalonia）：Token → Native Resource（`App.axaml` 资源字典） → UI Component

### 工程落点建议

| 组件语义 | 推荐落点 |
|---|---|
| 基础控件样式 | `ChildNotes/ChildNotes/Styles` 或 Avalonia Resource |
| 复合组件 | `ChildNotes/ChildNotes/Controls` |
| 页面状态与交互 | `ChildNotes/ChildNotes/ViewModels` |
| 记录类型 DTO / 常量 | `ChildNotes.Shared` |

## 11. 开发规则

所有 UI 开发必须：

- 禁止硬编码颜色。
- 禁止重复定义间距。
- 优先复用组件。
- 保持 Token 驱动。

所有组件必须具备：Design Token 支持、状态定义、多端适配方案、可测试交互。

### AI 开发规则（权威源）

> 本节是 AI 生成 UI / 组件 / 交互的统一规则。`design-tokens.md`、`interaction.md`、`home-page.md` 均引用本节，不重复定义。

**AI 生成 UI 必须遵守**：

1. 不随意新增颜色。
2. 不随意改变字体层级。
3. 不增加重复信息模块。
4. 所有页面共享 Design Tokens。
5. 新组件必须先定义用途。
6. 优先复用已有组件。

**AI 新增组件前必须回答**：

1. 为什么需要这个组件？
2. 是否可以复用已有组件？
3. 数据来源是什么？
4. 用户操作是什么？

**AI 修改交互时必须遵守**：

1. 优先简单操作路径。
2. 不增加额外确认步骤。
3. 不新增隐藏复杂手势。
4. 不改变已有用户习惯。
5. 优先复用已有交互模式。

## 12. 设计交付

Figma 交付必须包含：使用组件名称、Token 引用、状态说明、特殊交互说明。

## 13. 组件质量自检

- 明确定义用途
- 定义视觉结构
- 定义交互状态
- 定义使用场景
- 支持多端实现
- 保持品牌体验
- 符合 ChildNotes 情感定位

## 14. 最终组件原则

宝宝日记组件系统不是为了做复杂 UI，目标是：

- 简单
- 温暖
- 一致
- 可维护
- AI 容易理解

最终体验：像一本智能宝宝成长手账。

## 历史来源

本规范合并自：

- [`../archive/design-language-v1/components.md`](../archive/design-language-v1/components.md)
- [`../archive/design-language-v1/component-visual-specification.md`](../archive/design-language-v1/component-visual-specification.md)
- [`../archive/design-language-v1/component-library-specification.md`](../archive/design-language-v1/component-library-specification.md)
- [`../archive/design-language-v1/code-mapping-specification.md`](../archive/design-language-v1/code-mapping-specification.md)
- [`../archive/design-language-v1/figma-ready-specification.md`](../archive/design-language-v1/figma-ready-specification.md)
- [`../archive/design-language-v1/page-layout-specification.md`](../archive/design-language-v1/page-layout-specification.md)
- 外部补充：`Home Page Specification v1.0`（首页页面级规范，已独立为 [`home-page.md`](home-page.md)）
- 外部补充：`Components Specification v1.0`（组件分层、基础组件、业务组件、交互规则、命名规则）
