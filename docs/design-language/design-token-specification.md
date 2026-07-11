# ChildNotes Design Language

# Design Token Specification v1.0

版本：v0.1

---

# 1. Overview

Design Token 是 UI Design System 的基础层。

目标：

> 使用统一变量管理视觉语言，让设计和代码保持一致。

Token 分类：

```
Design Token
│
├── Color
├── Typography
├── Spacing
├── Radius
├── Shadow
├── Motion
└── Breakpoint
```

---

# 2. Color Token

## Brand

用于品牌识别和核心操作。

示例：

```
color.brand.primary
color.brand.secondary
```

---

## Surface

页面层级：

```
color.surface.background
color.surface.card
color.surface.overlay
```

---

## Text

文字颜色：

```
color.text.primary
color.text.secondary
color.text.disabled
```

---

## Semantic

状态颜色：

```
color.success
color.warning
color.error
color.info
```

---

# 3. Typography Token

结构：

```
Typography
│
├── Display
├── Title
├── Headline
├── Body
├── Caption
└── Label
```

关注：

- 字号
- 字重
- 行高
- 字间距

---

# 4. Spacing Token

基础单位：4px

定义：

```
spacing.xs   = 4
spacing.sm   = 8
spacing.md   = 16
spacing.lg   = 24
spacing.xl   = 32
spacing.xxl  = 48
```

---

# 5. Radius Token

圆角体现 ChildNotes 的亲和感。

```
radius.small
radius.medium
radius.large
radius.pill
```

推荐：

- 卡片：large
- 按钮：medium
- 标签：pill

---

# 6. Shadow Token

阴影用于层级表达。

```
shadow.none
shadow.small
shadow.medium
shadow.large
```

原则：

空间优先，阴影辅助。

---

# 7. Motion Token

动画统一管理：

```
motion.duration.fast
motion.duration.normal
motion.duration.slow

motion.easing.standard
motion.easing.emphasized
```

---

# 8. Breakpoint Token

用于响应式布局：

```
breakpoint.mobile
breakpoint.tablet
breakpoint.desktop
```

---

# 9. Code Mapping Direction

Token 应映射到各平台：

```
Design Token
      ↓
Platform Variable
      ↓
UI Component
```

例如：

```
color.brand.primary

↓

PrimaryColor

↓

Button
```

---

# 10. Current Status

Design Token：

- [x] Token 分类
- [x] 命名规范
- [x] 平台映射方向
- [ ] 最终数值定义
- [ ] Theme System

---

下一阶段：Theme System
