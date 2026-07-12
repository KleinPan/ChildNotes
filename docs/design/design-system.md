# Design System 设计系统

ChildNotes 的设计系统负责把设计语言落到可执行的视觉规则：色彩、字体、布局、动效、组件状态和可访问性。

## 系统目标

- **统一**：同一语义在不同页面和平台保持一致视觉表达。
- **克制**：减少噪音，让用户关注宝宝记录本身。
- **可扩展**：新增记录类型、AI 卡片和运营活动时复用现有 Token 与组件。
- **可实现**：所有视觉规则最终映射到 Avalonia 样式、资源或组件参数。

## 视觉原则

| 原则 | 说明 |
|---|---|
| Warm but Professional | 保留家庭温度，同时呈现长期工具的可靠感 |
| Calm Experience | 用留白、柔和背景和低饱和强调色减少压力 |
| Content First | 卡片、图表、AI 总结都应服务成长内容，而非装饰 |
| Accessible | 颜色对比、字号、触控区域和状态反馈满足基础可用性 |

## 信息层级

1. **页面背景**：承载整体情绪，保持低干扰。
2. **内容容器**：卡片、列表、时间轴，是记录承载主体。
3. **重点行动**：记录、保存、生成 AI 总结等主操作。
4. **辅助信息**：同步状态、来源、标签、时间、备注。

## 色彩系统摘要

- 品牌色用于主行动、品牌识别和关键强调。
- Surface 色用于背景、卡片、浮层、分割层级。
- Semantic 色用于成功、警告、错误、信息提示。
- AI 相关颜色应与品牌色区分，但保持温和、可信。

详细 Token 见 [`design-tokens.md`](design-tokens.md)。

## 布局与动效

- 移动端优先保证底部核心操作可达。
- 桌面端可以使用双栏或宽卡片，但不改变核心信息顺序。
- 动效用于反馈状态变化，不用于吸引无意义注意力。
- 关键动效包括：保存成功、Sheet 展开、AI 生成、错误重试、Tab 切换。

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
