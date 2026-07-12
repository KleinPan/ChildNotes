# ChildNotes Design Language

# Theme System Specification v1.0

版本：v0.1

---

# 1. Overview

Theme System 建立在 Design Token 之上，用于管理不同视觉主题和运行环境。

目标：

> 保持 ChildNotes 核心体验不变，同时支持不同设备、模式和品牌表达。

---

# 2. Theme Architecture

```
Theme System
│
├── Base Theme
│   ├── Color
│   ├── Typography
│   ├── Surface
│   └── Component Style
│
├── Appearance
│   ├── Light Mode
│   └── Dark Mode
│
└── Brand Extension
    └── Growth Theme
```

---

# 3. Light Theme

默认主题。

设计方向：

- 明亮
- 温暖
- 亲和

适合：

- 日常记录
- 家庭分享
- 成长浏览

---

# 4. Dark Theme

目标：

不是简单反色，而是保持阅读舒适。

要求：

- 降低视觉疲劳
- 保持内容层级
- 保留情感温度

---

# 5. Growth Theme

ChildNotes 特色主题。

方向：

> 表达孩子成长过程中的时间、记忆和变化。

应用场景：

- 成长报告
- 特别纪念日
- 年度回顾

---

# 6. Theme Rules

主题切换不应该影响：

- 信息架构
- 组件行为
- AI交互
- 记录流程

只改变：

- 视觉表达
- 色彩
- 表面样式

---

# 7. Token Override

主题通过 Token 覆盖实现：

```
Base Token
    ↓
Theme Override
    ↓
Component
```

例如：

```
color.surface.background

Light:
white

Dark:
dark surface
```

---

# 8. Current Status

Theme System：

- [x] 架构定义
- [x] Light Theme
- [x] Dark Theme
- [x] Growth Theme
- [x] Token Override
- [ ] 最终视觉参数

---

下一阶段：Code Mapping Specification
