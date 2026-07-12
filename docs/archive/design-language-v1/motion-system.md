# ChildNotes Design Language

# Motion & Interaction Visual System

版本：v0.1

---

# 1. Overview

Motion System 定义 ChildNotes 中动画、转场和状态反馈的视觉规则。

目标：

> 让用户理解正在发生什么，并感受到成长记录过程的连续性。

动效不是装饰，而是体验的一部分。

---

# 2. Motion Principles

## Natural 自然

动画应该符合真实世界的感觉。

例如：

- 卡片展开
- 页面切换
- 内容出现

---

## Calm 平静

ChildNotes 面向家庭记录场景。

避免：

- 快速闪烁
- 强刺激动画
- 游戏化反馈

---

## Meaningful 有意义

每个动画都应该表达状态变化。

---

# 3. Motion Token

```
Motion Token
│
├── Duration
│   ├── Fast
│   ├── Normal
│   └── Slow
│
├── Easing
│
└── Transition
```

---

# 4. Page Transition

页面切换应该保持空间连续感。

例如：

时间轴 → 记录详情

体验：

> 从成长旅程中打开某一个珍贵瞬间。

---

# 5. Record Card Motion

Record Card 支持：

- 展开
- 收起
- 图片浏览
- AI内容展开

设计目标：

> 像翻开一本成长相册。

---

# 6. Timeline Motion

时间轴动画：

- 节点自然出现
- 内容逐步呈现
- 保持浏览节奏

避免：

- 信息流快速刷新感

---

# 7. AI Motion

AI生成过程应该表达陪伴感。

不推荐：

```
Loading...
```

推荐：

```
正在整理宝宝最近的成长记录...
```

表现：

- 柔和状态变化
- 内容逐步生成
- 结果卡片出现

---

# 8. Feedback Animation

## Save Success

表达：

> 已保存这个珍贵瞬间

## AI Complete

表达：

> 已整理新的成长发现

## Error

原则：

- 告知原因
- 提供下一步操作

---

# 9. Motion Checklist

- [ ] 动画有目的
- [ ] 不打扰记录流程
- [ ] 保持温暖体验
- [ ] 多端行为一致

---

下一阶段：Platform Guidelines
