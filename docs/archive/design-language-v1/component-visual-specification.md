# ChildNotes Design Language

# Component Visual Specification

版本：v0.1

---

# 1. Overview

Component Visual Specification 定义 ChildNotes 核心组件的视觉规则。

目标：

> 让每一个 UI 组件都具有统一的视觉语言和一致的情感表达。

组件设计遵循：

- 简洁
- 温暖
- 可预测
- 可扩展

---

# 2. Component Structure

每个组件需要定义：

```
Component
│
├── Purpose
├── Anatomy
├── Size
├── Variant
├── State
├── Behavior
└── Usage
```

---

# 3. Button

## Purpose

用于触发用户主动操作。

---

## Variants

### Primary Button

用途：

- 创建记录
- 保存内容
- AI生成

视觉：

- 强调品牌色
- 明确点击区域

---

### Secondary Button

用途：

辅助操作。

---

### Text Button

用途：

轻量操作。

---

## States

必须支持：

```
Default
Pressed
Disabled
Loading
```

---

# 4. Record Card ⭐

Record Card 是 ChildNotes 最核心视觉组件。

定位：

> 一张记录孩子成长瞬间的数字日记卡片。

---

## Anatomy

```
Record Card
│
├── Date
├── Baby Age
├── Title
├── Content
├── Media
├── Tags
└── AI Insight
```

---

## Visual Principles

应该体现：

- 收藏感
- 回忆感
- 时间感

避免：

- 表格感
- 后台列表感

---

# 5. Timeline Card ⭐

用途：

展示孩子成长轨迹。

---

## Structure

```
Timeline Item
│
├── Time Point
├── Record Card
└── Connection Line
```

---

## Experience

用户浏览时间轴时应该感觉：

> 正在翻阅孩子成长故事。

---

# 6. AI Summary Card ⭐

用途：

展示 AI 对成长记录的整理。

---

## Structure

```
AI Summary Card
│
├── Summary Title
├── Key Moments
├── Growth Observation
├── Suggestion
└── Action
```

---

## Visual Direction

AI 卡片应该：

- 温和
- 可信
- 不突出机器感

避免：

- 聊天机器人风格
- 技术面板风格

---

# 7. Record Editor

用途：

创建和编辑成长记录。

---

## Principles

输入优先：

```
打开
 ↓
输入
 ↓
AI辅助
 ↓
确认
```

减少表单感。

---

# 8. Empty State Component

空状态应该提供引导，而不是告诉用户什么都没有。

例如：

> 记录宝宝今天的新发现吧。

---

# 9. Component Quality Checklist

- [ ] 定义视觉结构
- [ ] 定义交互状态
- [ ] 定义使用场景
- [ ] 保持品牌体验
- [ ] 支持多端实现

---

下一阶段：Page Layout Specification
