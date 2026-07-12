# ChildNotes Design Language

## AI Experience System Specification

版本：v0.1

---

# 1. Overview

AI 是 ChildNotes 的重要能力，但不是产品的主人。

核心理念：

> AI 帮助父母整理成长记忆，而不是替代父母记录孩子。

AI 在产品中的角色：

- 记录助手
- 记忆整理者
- 成长观察者
- 温和陪伴者

---

# 2. AI Interaction Principles

## 2.1 Human First 人优先

原始记录永远属于用户。

AI 输出应该：

- 可修改
- 可确认
- 可拒绝

---

## 2.2 Explainable 可理解

AI 不应该只给结论。

例如：

错误：

> 宝宝语言能力很好。

推荐：

> 根据最近三个月记录，宝宝出现了更多主动发音和模仿行为。

---

## 2.3 Emotional 温暖

AI 回复应该像家庭助手，而不是客服机器人。

避免：

- 机械化
- 过度专业化
- 冷冰冰的数据报告

---

# 3. AI Components

## 3.1 AI Summary Card

用途：

自动总结成长阶段。

结构：

```
AI Summary Card
│
├── Period
├── Key Moments
├── Growth Observation
├── Suggestion
└── Memory Quote
```

---

## 3.2 AI Record Assistant

帮助用户快速记录。

输入：

自然语言：

> 今天第一次叫妈妈

AI 转换：

```
日期
年龄
事件
标签
情绪
```

用户确认后保存。

---

## 3.3 Memory Reminder

主动发现历史记忆。

示例：

> 一年前的今天，宝宝第一次尝试爬行。

设计原则：

- 惊喜
- 温暖
- 不打扰

---

# 4. AI Conversation Design

聊天不是普通 Chat。

应该围绕：

- 孩子成长
- 家庭记忆
- 历史记录

避免成为通用 AI 助手。

---

# 5. AI Loading Experience

等待过程也属于体验。

推荐：

- 温和动画
- 成长相关提示
- 明确当前状态

例如：

> 正在整理宝宝最近的成长记录...

---

# 6. AI Safety

涉及儿童信息，必须保证：

- 用户控制数据
- 明确 AI 边界
- 不制造焦虑
- 不进行医疗诊断

---

# 7. AI Experience Checklist

- [ ] AI 输出可编辑
- [ ] AI 行为可解释
- [ ] 保留用户原始表达
- [ ] 不制造育儿压力
- [ ] 保持陪伴感

---

下一阶段：Interaction Language
