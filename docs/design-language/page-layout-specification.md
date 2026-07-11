# ChildNotes Design Language

# Page Layout Specification

版本：v0.1

---

# 1. Overview

Page Layout Specification 定义 ChildNotes 页面级视觉结构。

目标：

> 让用户在任何页面都保持清晰、自然、连续的成长记录体验。

---

# 2. Layout Principles

## Content First

内容优先于功能入口。

## Calm Density

控制信息密度，避免复杂后台感。

## Mobile First

优先适配手机单手操作。

---

# 3. Global Page Structure

```
Page
│
├── Top Navigation
│
├── Main Content
│
└── Primary Action
```

---

# 4. Home Page 首页

目标：

快速进入记录和回忆。

结构：

```
Home
│
├── Baby Overview
├── Quick Record
├── Recent Memories
├── Growth Highlights
└── AI Suggestions
```

核心动作：

记录今天。

---

# 5. Timeline Page 时间轴

核心页面。

结构：

```
Timeline
│
├── Date Marker
├── Timeline Item
├── Record Card
└── AI Memory Point
```

体验目标：

> 像翻阅孩子成长相册。

---

# 6. Record Detail Page

用于查看单条成长记录。

结构：

```
Detail
│
├── Date
├── Age
├── Content
├── Media
├── Tags
├── AI Insight
└── Related Memories
```

---

# 7. AI Experience Page

不是普通聊天页面。

定位：

> 成长记忆助手空间。

结构：

```
AI
│
├── Conversation
├── Memory Analysis
├── Growth Summary
└── Suggestions
```

---

# 8. Empty Page

空状态需要引导用户开始。

包含：

- 简短说明
- 温暖插图/视觉
- 明确下一步操作

---

# 9. Responsive Rules

优先级：

```
Mobile
 ↓
Tablet
 ↓
Desktop
```

保持：

- 内容宽度舒适
- 操作区域易触达
- 信息层级一致

---

# 10. Current Status

Page Layout Specification：

- [x] 页面结构
- [x] 核心页面定义
- [ ] 页面详细视觉规范
- [ ] 响应式尺寸规范
- [ ] 页面状态规范

---

下一阶段：Motion & Interaction Visual System
