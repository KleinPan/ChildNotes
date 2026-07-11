# ChildNotes Design Language

# Figma Ready Specification v1.0

版本：v0.1

---

# 1. Overview

Figma Ready Specification 定义设计团队如何使用 ChildNotes Design System 创建界面。

目标：

> 让设计、开发和产品保持同一套语言。

---

# 2. Figma File Structure

推荐结构：

```
ChildNotes Design System
│
├── Cover
├── Foundations
│   ├── Colors
│   ├── Typography
│   ├── Spacing
│   ├── Radius
│   └── Motion
│
├── Components
│   ├── Basic
│   ├── Content
│   └── AI
│
├── Patterns
│
└── Screens
```

---

# 3. Component Naming

规则：

```
Category / Component / Variant
```

示例：

```
Button / Primary / Default
Card / Record / Expanded
AI / Summary / Default
```

---

# 4. Variant Rules

组件状态统一：

```
Default
Pressed
Disabled
Loading
Selected
Expanded
```

---

# 5. Auto Layout Rules

所有组件优先使用 Auto Layout。

要求：

- 内容自适应
- 间距来自 Token
- 避免固定定位

---

# 6. Component Property

组件属性应该对应实际业务状态。

例如 Record Card：

```
Has Media
Has AI Insight
Expanded
Show Tags
```

---

# 7. Prototype Rules

原型重点表达：

- 页面关系
- 操作流程
- AI反馈
- 动效节奏

不追求复杂动画模拟。

---

# 8. Design Handoff

交付必须包含：

- 使用组件名称
- Token引用
- 状态说明
- 特殊交互说明

---

# 9. Current Status

Figma Ready Specification：

- [x] 文件结构
- [x] 命名规范
- [x] Variant规范
- [x] Auto Layout规范
- [x] Handoff规范

---

下一阶段：Design System v1.0 Final Review
