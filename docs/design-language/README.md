# ChildNotes Design Language

> 幼儿简记（ChildNotes）的产品设计语言规范。
>
> 本文档用于统一产品体验、UI 设计、交互行为和未来多端实现标准。

## 目录

- [1. Design Philosophy 设计理念](#1-design-philosophy-设计理念)
- [2. Product Principles 产品原则](#2-product-principles-产品原则)
- [3. Design System Architecture 设计体系架构](#3-design-system-architecture-设计体系架构)
- [4. UI Foundation UI 基础规范](#4-ui-foundation-ui-基础规范)
- [5. Interaction Language 交互语言](#5-interaction-language-交互语言)
- [6. Component System 组件体系](#6-component-system-组件体系)
- [7. Content & AI Experience 内容与 AI 体验](#7-content--ai-experience-内容与-ai-体验)
- [8. Motion & Feedback 动效反馈](#8-motion--feedback-动效反馈)
- [9. Accessibility 可用性](#9-accessibility-可用性)

---

# 1. Design Philosophy 设计理念

ChildNotes 是一个面向父母记录孩子成长过程的应用。

设计核心不是“记录数据”，而是：

> 帮助父母轻松保存、理解并回忆孩子成长中的珍贵瞬间。

设计语言围绕三个关键词：

## Warm 温暖

界面应该像一本家庭成长日记，而不是冷冰冰的数据管理工具。

## Simple 简洁

父母记录孩子时通常处于碎片时间，操作必须快速、低负担。

## Trust 信任

成长记录具有长期价值，需要稳定、安全、可靠的体验。

---

# 2. Product Principles 产品原则

## 2.1 Capture First 记录优先

任何核心流程都应该降低记录成本。

目标：

- 打开应用即可记录
- 最少输入完成一次记录
- 支持自然语言输入

## 2.2 Memory Over Data 记忆优先于数据

记录不是数据库条目，而是家庭记忆。

## 2.3 AI As Companion AI 作为陪伴者

AI 不应该替代父母表达，而应该帮助整理、理解和发现成长规律。

---

# 3. Design System Architecture 设计体系架构

ChildNotes Design Language 分为以下层级：

```
Design Language
│
├── UI Foundation
│   ├── Color
│   ├── Typography
│   ├── Spacing
│   ├── Layout
│   └── Iconography
│
├── Components
│   ├── Button
│   ├── Card
│   ├── Timeline
│   ├── Record Editor
│   └── AI Assistant
│
├── Interaction
│   ├── Gesture
│   ├── Animation
│   └── Feedback
│
└── Experience
    ├── Recording Flow
    ├── Growth Timeline
    └── AI Memory
```

---

# 4. UI Foundation UI 基础规范

详细规范见：

- [UI Foundation Specification](./ui-foundation.md)

---

# 后续章节

后续逐步补充：

- 组件规范
- 时间轴设计规范
- AI 记忆交互规范
- 多端一致性规范
- 品牌视觉规范

