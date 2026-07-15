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
- **Spacing**：统一使用 4/8/12/16/20/24/32/40/48，禁止随意定义 13px / 17px / 27px 等。
- **Radius**：Small 8dp / Medium 16dp / Large 24dp / Pill。
- **Typography**：5 级字号层级（LargeTitle 24 / SectionTitle 20 / CardTitle 16-18 / Body 16 / Caption 14）。

## 4. 组件状态

所有组件必须定义以下状态：Default / Pressed / Disabled / Loading / Error / Empty。Figma 交付时还应覆盖 Selected / Expanded。

## 5. Basic Components 基础组件

### 5.1 Button 按钮

按钮代表用户主动行为，执行明确操作。一个页面应该只有一个最重要动作。

| 类型 | 使用场景 | 规范 |
|---|---|---|
| Primary | 保存、确认、提交等核心动作 | Height 48dp / Radius 24dp / Text 16sp Medium / 品牌色底 |
| Secondary | 辅助操作 | 背景 Surface Secondary / 文字 Primary Text |
| Text | 轻量操作，如跳过、稍后、查看协议 | 无背景，仅文字 |
| Icon | 小型操作入口，如 + / 设置 / 关闭 | 尺寸 40×40dp / 图标 20-24dp |

### 5.2 Icon 图标

**风格**：圆润、简洁、低饱和、易识别。

**禁止**：

- Emoji 和专业 Icon 混用。
- 不同风格图标混搭。

**尺寸规范**：

| 场景 | 尺寸 |
|---|---|
| 导航 | 24dp |
| 普通操作 | 20-32dp |
| 功能入口 | 40-48dp |

### 5.3 Card 通用卡片

**用途**：信息分组，如 AI 状态、疫苗提醒、成长信息。

**基础规范**：

| 属性 | 值 |
|---|---|
| 背景 | Surface（`color.surface.card`） |
| 圆角 | 16dp（`radius.medium`） |
| 内边距 | 16dp |
| 阴影 | 轻微（`shadow.card`） |

**禁止**：强阴影、大面积悬浮效果。不要用阴影制造层级，优先用空间和背景差异。

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

| 组件 | 用途 |
|---|---|
| Sheet | 记录表单、快捷选择、上下文操作 |
| Dialog | 高风险确认、权限说明、不可恢复操作 |

不应把复杂长流程塞入 Dialog。Bottom Sheet 用于新增记录：点击 + 后出现，默认隐藏，类似微信操作体验。

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
