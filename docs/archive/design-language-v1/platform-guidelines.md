# ChildNotes Design Language

# Platform Guidelines

版本：v0.1

---

# 1. Overview

Platform Guidelines 定义 ChildNotes 在不同终端上的设计一致性规则。

目标：

> 同一个 ChildNotes，在不同设备上拥有相同的成长记录体验。

支持平台：

- Mobile
- Tablet
- Desktop
- Mini Program

---

# 2. Platform Principle

## Experience First

不同平台可以有不同实现，但核心体验不能变化。

保持：

- 记录流程一致
- 内容结构一致
- AI体验一致

---

# 3. Mobile Guidelines

Mobile 是主要使用场景。

重点：

- 单手操作
- 快速记录
- 大触控区域
- 降低输入成本

布局：

```
Mobile
│
├── Header
├── Content
└── Bottom Action
```

---

# 4. Android Guidelines

遵循：

- Material Design 基础原则
- 系统返回行为
- 手势导航习惯

重点：

- 适配不同屏幕尺寸
- 注意系统字体缩放
- 保持性能流畅

---

# 5. iOS Guidelines

遵循：

- Human Interface Guidelines 思路
- 原生交互习惯

重点：

- 安全区域
- 手势返回
- 内容层级

---

# 6. Web Guidelines

Web 不是简单放大移动端。

需要：

- 更大的内容空间
- 键盘操作支持
- 响应式布局

结构：

```
Desktop
│
├── Navigation
├── Main Content
└── Side Panel
```

---

# 7. Mini Program Guidelines

适配小程序限制：

- 页面生命周期
- 性能限制
- 原生组件能力

保持：

- 快速打开
- 快速记录
- 快速分享

---

# 8. Cross Platform Consistency

一致内容：

- 颜色
- 组件行为
- 信息架构
- AI交互

允许差异：

- 导航方式
- 系统控件
- 手势细节

---

# 9. Current Status

Platform Guidelines：

- [x] 平台原则
- [x] Mobile规范
- [x] Android规范
- [x] iOS规范
- [x] Web规范
- [x] 小程序规范

---

下一阶段：Design System v1.0 Consolidation
