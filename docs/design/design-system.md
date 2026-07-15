# Design System 设计系统

ChildNotes 的设计系统负责把设计语言落到可执行的视觉规则：色彩、字体、布局、动效、组件状态和可访问性。

## 系统目标

- **统一**：同一语义在不同页面和平台保持一致视觉表达。
- **克制**：减少噪音，让用户关注宝宝记录本身。
- **可扩展**：新增记录类型、AI 卡片和运营活动时复用现有 Token 与组件。
- **可实现**：所有视觉规则最终映射到 Avalonia 样式、资源或组件参数。

## 设计方向

整体方向与视觉关键词见 [`design-tokens.md`](design-tokens.md) 的"设计方向"章节。本规范聚焦视觉规则的落地。

## 信息层级

所有页面遵循统一层级，不要让所有内容拥有同等视觉权重：

```
核心信息
    ↓
重要提醒
    ↓
辅助信息
    ↓
操作入口
```

具体分层：

1. **页面背景**：承载整体情绪，保持低干扰。
2. **内容容器**：卡片、列表、时间轴，是记录承载主体。
3. **重点行动**：记录、保存、生成 AI 总结等主操作。
4. **辅助信息**：同步状态、来源、标签、时间、备注。

## 卡片原则

卡片用于信息分组、状态展示、操作入口。

不要：无限堆叠卡片、每个数据一个大卡片、用阴影制造层级。优先通过空间和背景差异表达层级。

## 视觉原则

| 原则 | 说明 |
|---|---|
| Warm but Professional | 保留家庭温度，同时呈现长期工具的可靠感 |
| Calm Experience | 用留白、柔和背景和低饱和强调色减少压力 |
| Content First | 卡片、图表、AI 总结都应服务成长内容，而非装饰 |
| Accessible | 颜色对比、字号、触控区域和状态反馈满足基础可用性 |

品牌应避免：过度卡通的儿童界面、过度装饰、冰冷的企业软件感。推荐方向：现代家庭产品、平静的科技感、有情绪的简洁。

## 品牌语言

ChildNotes 是家庭的陪伴者，帮助捕捉、整理和理解童年记忆。品牌围绕一个简单信念：每一个微小瞬间都值得被记住。

品牌个性：

- **Warm**：让人感到受欢迎和安全，创造情绪舒适感而不是压力。
- **Gentle**：尊重父母有限的时间和注意力。
- **Trustworthy**：家庭记忆私密且重要，体验必须传达可靠性。
- **Intelligent**：AI 应安静地帮助用户从记忆中发现意义，不替代人类情感。

文案语气应亲切、简洁、鼓励；避免命令式、机械式、焦虑式。普通产品说"创建成功"，ChildNotes 说"已保存这个珍贵瞬间"。

## 色彩系统

色彩应表达温暖、信任和平静。

| 色彩族 | 用途 | 说明 |
|---|---|---|
| Brand 品牌色 | 主操作、品牌识别、关键强调 | 柔和、亲近、不刺激，避免侵略性饱和度 |
| Surface 表面色 | 背景、卡片、浮层、遮罩 | 大面积使用柔和中性背景，让照片和记忆内容突出 |
| Text 文字色 | 主文本、次文本、禁用文本、反色文本 | 保证阅读舒适度 |
| Semantic 语义色 | 成功、警告、错误、信息 | Success 用于已完成/已保存/正向状态；Warning 谨慎用于需注意场景；Error 仅在需要纠正或保护时使用 |
| AI 色 | AI 入口、AI 总结、AI 生成中状态 | 与品牌色区分但保持温和可信 |

详细 Token 见 [`design-tokens.md`](design-tokens.md)。

## 字体系统

文字层级：

| 层级 | 用途 |
|---|---|
| Display / Large Title | 页面标题 |
| Title / Heading | 模块标题 |
| Headline | 区块标题 |
| Body | 正文内容 |
| Caption | 辅助信息 |
| Label | 标签、按钮文字 |

原则：优先可读性、避免信息过载、中文优先适配系统字体。避免过小文字。

## 间距系统

统一空间尺度，基础单位 4px，让所有页面拥有一致节奏。

| Token | 值 | 用途 |
|---|---|---|
| `spacing.xs` | 4 | micro spacing |
| `spacing.sm` | 8 | compact |
| `spacing.md` | 12 | normal |
| `spacing.lg` | 16 | standard |
| `spacing.xl` | 24 | section |
| `spacing.xxl` | 32 | large section |
| `spacing.3xl` | 48+ | page spacing |

## 圆角系统

圆角表达亲和感：

| Token | 用途 |
|---|---|
| `radius.small` | 小控件 |
| `radius.medium` | 按钮、输入框 |
| `radius.large` | 卡片 |
| `radius.pill` | 标签 |

ChildNotes 推荐：卡片使用较大圆角，控件使用中等圆角，标签使用 Pill。

## 表面层级

```
Background
    ↓
Card
    ↓
Floating Element
    ↓
Modal
```

避免大量阴影。优先通过空间、背景差异、边界表达层级。阴影用于表达层级，不用于装饰。

## 主题系统

```
Theme System
│
├── Base Theme
│   ├── Color / Typography / Surface / Component Style
│
├── Appearance
│   ├── Light Mode（默认：明亮、温暖、亲和）
│   └── Dark Mode（不是简单反色，而是保持阅读舒适：降低视觉疲劳、保持内容层级、保留情感温度）
│
└── Brand Extension
    └── Growth Theme（特色主题：表达时间、记忆和变化，用于成长报告、纪念日、年度回顾）
```

主题切换不应影响信息架构、组件行为、AI 交互、记录流程，只改变视觉表达、色彩、表面样式。主题通过 Token 覆盖实现：Base Token → Theme Override → Component。

## 布局与动效

### 布局

页面通用结构：Top Navigation / Main Content / Primary Action。

- 移动端优先保证底部核心操作可达，单手操作友好。
- 桌面端可使用双栏或宽卡片，但不改变核心信息顺序。
- 响应式优先级：Mobile → Tablet → Desktop，保持内容宽度舒适、操作区域易触达、信息层级一致。

### 动效

动效目的是让用户理解正在发生什么，并感受到成长记录过程的连续性。动效不是装饰，而是体验的一部分。

原则：Natural（自然，符合真实世界感觉）、Calm（平静，避免快速闪烁/强刺激/游戏化）、Meaningful（有意义，每个动画都表达状态变化）。

关键动效场景：保存成功、Sheet 展开、AI 生成、错误重试、Tab 切换、时间轴展开。

| 场景 | 体验目标 |
|---|---|
| 页面切换（时间轴 → 记录详情） | "从成长旅程中打开某一个珍贵瞬间"，保持空间连续感 |
| Record Card 展开/收起/图片浏览/AI 内容展开 | "像翻开一本成长相册" |
| Timeline 节点出现 | 节点自然出现、内容逐步呈现、保持浏览节奏，避免信息流快速刷新感 |
| AI 生成过程 | 表达陪伴感，柔和状态变化、内容逐步生成、结果卡片出现 |
| Save Success | "已保存这个珍贵瞬间" |
| AI Complete | "已整理新的成长发现" |
| Error | 告知原因、提供下一步操作 |

## 可访问性

- 颜色对比满足基础可用性。
- 字号避免过小。
- 触控区域足够大。
- 状态反馈明确，不仅依赖颜色。

## 历史来源

本规范合并自：

- [`../archive/design-system-v0/README.md`](../archive/design-system-v0/README.md)
- [`../archive/design-system-v0/00-Vision.md`](../archive/design-system-v0/00-Vision.md)
- [`../archive/design-system-v0/01-Brand.md`](../archive/design-system-v0/01-Brand.md)
- [`../archive/design-system-v0/02-Design-Principles.md`](../archive/design-system-v0/02-Design-Principles.md)
- [`../archive/design-system-v0/03-Color-System.md`](../archive/design-system-v0/03-Color-System.md)
- [`../archive/design-language-v1/ui-design-system.md`](../archive/design-language-v1/ui-design-system.md)
- [`../archive/design-language-v1/ui-foundation.md`](../archive/design-language-v1/ui-foundation.md)
- [`../archive/design-language-v1/theme-system.md`](../archive/design-language-v1/theme-system.md)
- [`../archive/design-language-v1/motion-system.md`](../archive/design-language-v1/motion-system.md)
- [`../archive/design-language-v1/page-layout-specification.md`](../archive/design-language-v1/page-layout-specification.md)
- 外部补充：`design-tokens_v1.0.md`（设计方向、信息层级、卡片原则）
