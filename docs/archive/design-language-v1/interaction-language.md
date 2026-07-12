# ChildNotes Design Language

## Interaction Language Specification

版本：v0.1

---

# 1. Overview

Interaction Language 定义 ChildNotes 中用户与产品之间的行为关系。

目标：

> 让记录孩子成长成为一种自然、轻松、没有压力的行为。

交互设计关注：

- 降低记录成本
- 提供及时反馈
- 保持情感连接

---

# 2. Core Interaction Principles

## 2.1 One Action One Purpose

每一次交互都应该有明确目的。

例如：

打开应用 → 记录今天发生的事情

而不是：

打开应用 → 浏览复杂功能列表

---

## 2.2 Progressive Disclosure

复杂能力逐步出现。

用户第一次记录：

```
文字输入
↓
保存
```

高级能力：

```
AI整理
标签
成长分析
```

随着使用深入出现。

---

# 3. Record Flow 记录流程

核心流程：

```
打开 App
    ↓
快速记录入口
    ↓
输入内容
    ↓
AI辅助整理（可选）
    ↓
用户确认
    ↓
保存成长记录
```

原则：

AI 永远不能绕过用户确认直接修改记忆。

---

# 4. Gesture Language 手势语言

## Swipe

用于：

- 时间浏览
- 页面切换
- 快速操作

要求：

自然、符合移动设备习惯。

---

## Long Press 长按

用于：

- 编辑
- 更多操作
- 收藏

避免作为主要功能入口。

---

# 5. Empty State 空状态

空状态不是错误页面。

应该表达：

- 鼓励开始记录
- 降低首次使用压力

示例：

> 今天还没有记录宝宝的新发现，要不要保存一个瞬间？

---

# 6. Feedback System 反馈系统

所有重要操作需要反馈：

## Success

例如：

> 已保存今天的成长记录

## Loading

例如：

> 正在整理这段珍贵记忆...

## Error

应该告诉用户下一步怎么办。

---

# 7. Animation Principles

动效目的：

不是炫技，而是增强理解。

原则：

- 快速
- 柔和
- 有意义

适合：

- 保存成功
- AI生成
- 时间轴展开

---

# 8. Mobile First

ChildNotes 优先考虑移动场景。

要求：

- 单手可操作
- 大触控区域
- 减少输入成本
- 支持碎片时间使用

---

# 9. Interaction Checklist

- [ ] 核心流程少步骤
- [ ] 用户始终拥有控制权
- [ ] AI行为透明
- [ ] 有明确反馈
- [ ] 符合陪伴感

---

下一阶段：Brand & Experience System
