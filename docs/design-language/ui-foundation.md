# ChildNotes Design Language

## UI Foundation Specification

版本：v0.1

---

# 1. Overview

UI Foundation 是 ChildNotes Design Language 的第一阶段。

目标：建立统一的视觉基础，使 Web、Mobile、小程序以及未来桌面端保持一致体验。

UI Foundation 包含：

- Color System 色彩系统
- Typography 字体系统
- Spacing 间距系统
- Layout 布局系统
- Surface 表面层级
- Iconography 图标规范
- Interaction State 交互状态

---

# 2. Visual Direction 视觉方向

ChildNotes 应避免：

- 过度工具化
- 复杂后台风格
- 强商业化视觉
- 冷色科技感

推荐方向：

- 温暖
- 柔和
- 自然
- 长期陪伴感

视觉感觉接近：

> 一本现代化的家庭成长相册。

---

# 3. Color System 色彩系统

## 3.1 Brand Color

品牌色用于：

- 主按钮
- 核心操作
- 重要状态

要求：

- 柔和
- 亲近
- 不刺激

## 3.2 Semantic Colors

定义：

| 类型 | 用途 |
| --- | --- |
| Primary | 核心操作 |
| Success | 成功状态 |
| Warning | 提醒 |
| Error | 错误 |
| Info | 信息 |

## 3.3 Neutral Colors

用于：

- 背景
- 卡片
- 文本
- 分割线

---

# 4. Typography 字体系统

文字层级：

```
Display
 └── 页面标题

Heading
 └── 模块标题

Body
 └── 正文内容

Caption
 └── 辅助信息
```

原则：

- 优先保证阅读舒适度
- 避免过小文字
- 中文环境优先考虑系统字体体验

---

# 5. Spacing 间距系统

采用统一空间单位。

基础单位：4px

示例：

```
4   micro spacing
8   compact
12  normal
16  standard
24  section
32  large section
48+ page spacing
```

目的：

让所有页面拥有一致节奏。

---

# 6. Layout 布局系统

## 页面结构

```
Page
│
├── Navigation
│
├── Content
│
└── Action Area
```

原则：

- 内容优先
- 减少视觉噪音
- 保持单手操作友好

---

# 7. Surface 表面层级

层级：

```
Background
    ↓
Card
    ↓
Floating Element
    ↓
Modal
```

避免大量阴影。

优先通过：

- 空间
- 背景差异
- 边界

表达层级。

---

# 8. Component Design Principles

组件设计遵循：

## Consistency 一致

同类组件保持相同行为。

## Predictability 可预测

用户不需要学习新的操作方式。

## Emotional Connection 情感连接

成长记录相关组件应具有温度。

---

# 9. First Milestone: UI Foundation

完成标准：

- [ ] 色彩体系确定
- [ ] 字体体系确定
- [ ] 间距规范确定
- [ ] 页面布局规则确定
- [ ] 基础组件设计原则确定

---

下一阶段：Component System
