# ChildNotes Design Language

# UI Design System v1.0

版本：v0.1

---

# 1. Overview

UI Design System 是 ChildNotes 从产品体验语言进入视觉系统的阶段。

目标：

> 建立统一、可扩展、跨平台的视觉表达体系。

覆盖：

- Design Tokens
- Visual Style
- Components
- Layout Rules
- Platform Adaptation

---

# 2. Design Principles

## Warm but Professional

ChildNotes 需要有家庭温度，同时保持长期产品可信度。

## Calm Experience

减少视觉噪音，让用户关注孩子成长内容。

## Consistent

不同页面、不同平台保持统一体验。

---

# 3. Design Token System

所有 UI 设计最终应该转换为 Token。

结构：

```
Token
│
├── Color
├── Typography
├── Spacing
├── Radius
├── Shadow
└── Motion
```

---

# 4. Color Token

颜色分为：

## Brand

品牌核心颜色。

用途：

- 主操作
- 重点内容
- 品牌识别

---

## Semantic

表达状态：

```
Success
Warning
Error
Info
```

---

## Surface

页面层级：

```
Background
Surface
Elevated Surface
Overlay
```

---

# 5. Typography System

文字用于表达信息层级。

```
Large Title
Title
Headline
Body
Caption
Label
```

原则：

- 优先可读性
- 避免信息过载
- 中文优先适配

---

# 6. Spacing System

统一空间尺度。

基础单位：4px

```
XS   4
SM   8
MD   16
LG   24
XL   32
XXL  48
```

用于：

- 页面间距
- 卡片间距
- 内容布局

---

# 7. Radius System

圆角表达亲和感。

定义：

```
Small
Medium
Large
Pill
```

ChildNotes 推荐：

- 卡片使用较大圆角
- 控件使用中等圆角
- 标签使用 Pill

---

# 8. Shadow System

阴影用于表达层级，不用于装饰。

层级：

```
None
Small
Medium
Large
```

原则：

优先使用空间和背景区分层级。

---

# 9. Component Visual Direction

核心组件：

## Record Card

视觉目标：

> 像保存的一页家庭日记。

## Timeline

视觉目标：

> 展示成长过程，而不是数据列表。

## AI Card

视觉目标：

> 像一个温和的成长助手。

---

# 10. Current Status

UI Design System 开始阶段：

- [x] Design Token 架构
- [x] 视觉原则
- [x] 基础规范框架
- [ ] 最终颜色方案
- [ ] 组件视觉规范
- [ ] 页面规范

---

下一阶段：Component Visual Specification
