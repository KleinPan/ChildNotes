# ChildNotes Design Language

## Component System Specification

版本：v0.1

---

# 1. Overview

Component System 是 UI Foundation 之后的第二阶段。

目标：建立 ChildNotes 的核心 UI 组件语言，让不同平台（Web、Mobile、小程序、桌面端）保持一致体验。

组件设计原则：

- 可复用（Reusable）
- 可理解（Understandable）
- 有温度（Emotional）
- 服务成长记录（Memory First）

---

# 2. Component Architecture

```
Components
│
├── Foundation Components
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
│   └── Growth Summary
│
├── Interaction Components
│   ├── Bottom Action
│   ├── Sheet
│   ├── Dialog
│   └── Gesture Panel
│
└── AI Components
    ├── AI Insight Card
    ├── AI Summary
    ├── Memory Reminder
    └── Conversation Panel
```

---

# 3. Button 按钮规范

按钮代表用户主动行为。

类型：

## Primary

用于核心动作：

- 创建记录
- 保存
- 生成 AI 总结

## Secondary

用于辅助操作。

## Text Button

用于轻量操作。

原则：

> 一个页面应该只有一个最重要动作。

---

# 4. Record Card 记录卡片

Record Card 是 ChildNotes 最核心组件。

包含：

```
Record Card
│
├── Date
├── Age
├── Content
├── Media
├── Tags
└── AI Enhancement
```

设计目标：

用户看到记录时，感受到的是“回忆”，而不是数据库条目。

---

# 5. Timeline Card 时间轴卡片

成长记录的主要展示方式。

特点：

- 时间连续
- 信息密度适中
- 支持照片、文字、AI总结

避免：

- 表格化
- 后台列表感

---

# 6. Component States

所有组件必须定义：

- Default
- Pressed
- Disabled
- Loading
- Error
- Empty

---

# 7. Component Quality Checklist

组件完成标准：

- [ ] 明确定义用途
- [ ] 支持多端实现
- [ ] 定义状态
- [ ] 定义交互
- [ ] 符合 ChildNotes 情感定位

---

下一阶段：AI Experience System
