# ChildNotes Design Language

# Component Library Specification v1.0

版本：v0.1

---

# 1. Overview

Component Library 是 Design System 从规范进入开发实现的核心阶段。

目标：

> 建立可复用、可维护、跨平台一致的 ChildNotes UI 组件体系。

---

# 2. Component Layers

```
Component Library
│
├── Basic Components
│   ├── Button
│   ├── Input
│   ├── Avatar
│   ├── Tag
│   └── Divider
│
├── Content Components
│   ├── Record Card
│   ├── Timeline Card
│   ├── Photo Card
│   └── Growth Card
│
├── AI Components
│   ├── AI Summary Card
│   ├── AI Suggestion
│   └── Memory Reminder
│
└── Complex Patterns
    ├── Record Flow
    ├── Timeline Flow
    └── AI Flow
```

---

# 3. Record Card v1.0

核心业务组件。

## Purpose

承载孩子成长记录。

## Structure

```
Record Card
│
├── Time
├── Age
├── Title
├── Content
├── Media
├── Tags
└── AI Enhancement
```

## Design Goal

不是信息展示卡，而是数字成长日记。

---

# 4. Timeline Card v1.0

用途：

展示成长轨迹。

结构：

```
Timeline Card
│
├── Date Node
├── Memory Content
└── Related Moments
```

原则：

保持时间连续感。

---

# 5. AI Summary Card v1.0

用途：

展示 AI 对成长记录的理解。

结构：

```
AI Summary
│
├── Period
├── Highlights
├── Observation
├── Suggestion
└── Memory Quote
```

原则：

AI 输出必须可理解、可修改、可接受。

---

# 6. Record Editor v1.0

目标：

降低记录成本。

流程：

```
Input
 ↓
AI Assist
 ↓
Preview
 ↓
Confirm
```

避免复杂表单。

---

# 7. Component Development Rules

所有组件必须具备：

- Design Token 支持
- 状态定义
- 多端适配方案
- 可测试交互

---

# 8. Current Status

Component Library：

- [x] Architecture
- [x] Core Business Components
- [ ] Detailed API Specification
- [ ] Code Implementation Mapping

---

下一阶段：Design Token Finalization
