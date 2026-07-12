# ChildNotes Design Language

# Code Mapping Specification v1.0

版本：v0.1

---

# 1. Overview

Code Mapping Specification 定义 Design System 与工程实现之间的映射关系。

目标：

> 让设计规范可以直接指导代码实现，保持 Design 与 Development 一致。

---

# 2. Mapping Architecture

```
Design Token
      ↓
Platform Token
      ↓
Component Style
      ↓
Application UI
```

---

# 3. Token Mapping

## Color

Design:

```
color.brand.primary
```

映射：

```
PrimaryColor
```

应用：

- Button
- Navigation
- Highlight

---

## Spacing

Design:

```
spacing.md
```

映射：

```
16px
```

应用：

- Layout
- Card Padding
- Component Gap

---

# 4. Component Mapping

## Button

Design Component:

```
Button
```

工程组件：

```
PrimaryButton
SecondaryButton
TextButton
```

要求：

- 使用 Token
- 保持状态一致
- 支持主题切换

---

## Record Card

Design Component:

```
Record Card
```

工程组件：

```
RecordCard
```

职责：

- 展示成长记录
- 管理媒体内容
- 展示 AI 信息

---

## AI Summary Card

Design Component:

```
AI Summary Card
```

工程组件：

```
AiSummaryCard
```

要求：

- AI 输出结构化
- 用户可确认
- 不直接修改数据

---

# 5. Platform Mapping

## Web

方向：

```
Token
 ↓
CSS Variables
 ↓
Vue Components
```

---

## Mobile

方向：

```
Token
 ↓
Native Resource
 ↓
UI Component
```

---

# 6. Development Rules

所有 UI 开发必须：

- 禁止硬编码颜色
- 禁止重复定义间距
- 优先复用组件
- 保持 Token 驱动

---

# 7. Design Review Checklist

开发完成后检查：

- [ ] 是否符合 Design Token
- [ ] 是否支持主题
- [ ] 是否符合组件规范
- [ ] 是否保持跨平台体验

---

# 8. Current Status

Code Mapping：

- [x] Token Mapping
- [x] Component Mapping
- [x] Platform Direction
- [ ] Framework Implementation

---

下一阶段：Figma Ready Specification
