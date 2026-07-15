# Design Language 设计语言

ChildNotes 的设计语言围绕一个目标：帮助父母轻松保存、理解并回忆孩子成长中的珍贵瞬间。它不是单纯的数据录入工具，而是一本长期可信、温暖克制的家庭成长日记。

## 设计关键词

| 关键词 | 含义 | 设计要求 |
|---|---|---|
| Warm 温暖 | 像家庭成长日记，而不是冷冰冰的数据系统 | 使用柔和色彩、亲切文案、情绪化但不过度装饰的反馈 |
| Simple 简洁 | 父母通常在碎片时间记录 | 打开即可记录，减少表单层级，允许自然语言输入 |
| Trust 信任 | 成长记录具有长期价值 | 明确保存状态、同步状态、隐私边界和 AI 输出来源 |

产品视觉应避免：过度工具化、复杂后台风格、强商业化视觉、冷色科技感。推荐方向：温暖、柔和、自然、长期陪伴感——接近一本现代化的家庭成长相册。

## 产品原则

### 1. Capture First 记录优先

记录是产品最核心的行为。任何页面、组件和流程都应优先降低记录成本：

- 核心入口必须稳定、明显、低干扰。
- 打开应用后应尽快进入记录状态，减少不必要的表单和步骤。
- 允许用户先记录，再补充结构化字段。
- 表单默认值和快捷选择应服务真实育儿场景。
- 支持自然语言输入和碎片化时间完成记录。

不应设计：复杂的信息录入流程、需要用户提前理解的数据结构、为数据库方便而牺牲用户体验的取舍。

### 2. Memory Over Data 记忆优先于数据

用户保存的是家庭记忆，不是数据库条目：

- 时间轴应该像成长相册，而不是数据列表或管理后台。
- 记录卡片应强调宝宝、时间、事件和情绪价值。
- 统计图表应帮助回忆与理解，而不是制造焦虑。
- 历史记录应该方便回忆，内容展示应体现情感价值。

记忆的形成路径：Moment（瞬间）→ Memory（记忆）→ Story（故事）。AI 可以帮助把碎片记录连接成成长故事。

### 3. AI As Companion AI 作为陪伴者

AI 是记录助手、记忆整理者、成长观察者和温和陪伴者，不替代父母表达：

- AI 输出必须可确认、可修改、可拒绝。
- 原始记录永远属于用户，AI 永远不能绕过用户确认直接修改记忆。
- 建议应温和、解释充分，避免医学诊断式表达。
- AI 只在能减少负担或提升理解时出现。

AI 不应该：替代父母表达、生成冰冷的分析报告、制造压力或焦虑、进行医疗诊断。

### 4. Consistent Experience 体验一致

不同平台保持统一核心体验：记录流程、内容结构、AI 体验一致；允许导航方式、系统控件、手势细节按平台习惯调整。

## 交互语言

交互的核心原则：One Action One Purpose（每次交互有明确目的）、Progressive Disclosure（复杂能力逐步出现）、低打断（优先轻量提示而非全屏阻塞）、状态可见、可撤回。

完整的工程级交互规范（页面跳转规则、Bottom Sheet 规范、+ 按钮交互、AI 记录交互、AI 次数限制、Record Timeline 交互、手势规范、Loading / Empty / Error 状态、动画时长、可访问性数值）见 [`interaction.md`](interaction.md)。

### 记录流程

核心流程：打开 App → 快速记录入口 → 输入内容 → AI 辅助整理（可选）→ 用户确认 → 保存成长记录。

原则：AI 永远不能绕过用户确认直接修改记忆。

### 反馈系统

所有重要操作需要反馈：

- **Success**：例如"已保存今天的成长记录"。
- **Loading**：例如"正在整理这段珍贵记忆..."。
- **Error**：应告诉用户下一步怎么办，而不是只报错。

### 空状态

空状态不是错误页面，应表达鼓励开始记录、降低首次使用压力。示例："今天还没有记录宝宝的新发现，要不要保存一个瞬间？"

### 动效原则

动效目的是增强理解而不是炫技：快速、柔和、有意义。适合场景：保存成功、AI 生成、时间轴展开。避免：快速闪烁、强刺激动画、游戏化反馈。

## AI 体验

### AI 交互原则

- **Human First 人优先**：原始记录永远属于用户。AI 输出应可修改、可确认、可拒绝。
- **Explainable 可理解**：AI 不应只给结论。错误示例："宝宝语言能力很好"；推荐："根据最近三个月记录，宝宝出现了更多主动发音和模仿行为"。
- **Emotional 温暖**：AI 回复应像家庭助手，而不是客服机器人。避免机械化、过度专业化、冷冰冰的数据报告。

### AI 组件

| 组件 | 用途 | 结构 |
|---|---|---|
| AI Summary Card | 自动总结成长阶段 | Period / Key Moments / Growth Observation / Suggestion / Memory Quote |
| AI Record Assistant | 帮助用户快速记录 | 自然语言输入 → 日期/年龄/事件/标签/情绪 → 用户确认后保存 |
| Memory Reminder | 主动发现历史记忆 | 例如"一年前的今天，宝宝第一次尝试爬行"，原则：惊喜、温暖、不打扰 |

### AI 对话设计

聊天不是普通 Chat，应围绕孩子成长、家庭记忆、历史记录，避免成为通用 AI 助手。

AI 等待过程也属于体验：推荐温和动画、成长相关提示、明确当前状态（如"正在整理宝宝最近的成长记录..."），而不是冰冷的"Loading..."。

### AI 安全

涉及儿童信息，必须保证：用户控制数据、明确 AI 边界、不制造焦虑、不进行医疗诊断。

## 平台适配原则

| 平台 | 适配重点 |
|---|---|
| Mobile | 主要使用场景。单手操作、快速记录、大触控区域、降低输入成本、支持碎片时间 |
| Android | Material Design 基础原则、系统返回行为、手势导航、适配不同屏幕尺寸、注意系统字体缩放、国内应用商店合规 |
| iOS | Human Interface Guidelines 思路、原生交互习惯、安全区域、手势返回、内容层级、App Store 审核文案 |
| Desktop | 更大内容空间、键盘操作支持、响应式布局、更高信息密度、双栏或宽卡片，但不改变核心信息顺序 |
| Mini Program | 适配生命周期/性能/原生组件限制，保持快速打开、快速记录、快速分享 |

页面通用结构：Top Navigation / Main Content / Primary Action。移动端优先保证底部核心操作可达。

## 体验自检

- 用户感受到陪伴
- 记录过程无压力
- 回忆体验有价值
- AI 增强而非替代
- 核心流程少步骤
- 用户始终拥有控制权
- AI 行为透明

## 历史来源

本规范合并自历史设计语言文档，原文保存在：

- [`../archive/design-language-v1/README.md`](../archive/design-language-v1/README.md)
- [`../archive/design-language-v1/product-manual.md`](../archive/design-language-v1/product-manual.md)
- [`../archive/design-language-v1/brand-experience.md`](../archive/design-language-v1/brand-experience.md)
- [`../archive/design-language-v1/interaction-language.md`](../archive/design-language-v1/interaction-language.md)
- [`../archive/design-language-v1/platform-guidelines.md`](../archive/design-language-v1/platform-guidelines.md)
- [`../archive/design-language-v1/ai-experience.md`](../archive/design-language-v1/ai-experience.md)
- [`../archive/design-system-v0/00-Vision.md`](../archive/design-system-v0/00-Vision.md)
- [`../archive/design-system-v0/01-Brand.md`](../archive/design-system-v0/01-Brand.md)
- [`../archive/design-system-v0/02-Design-Principles.md`](../archive/design-system-v0/02-Design-Principles.md)
