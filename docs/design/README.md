# 设计文档

本目录是 ChildNotes 设计相关内容的唯一权威入口。原设计语言、Design System 与工程侧设计系统草稿中的内容已经合并为以下 6 份规范；历史原文保留在 `../archive/`。

## 由浅入深的阅读顺序

6 份规范按 4 层递进组织，从"为什么做"到"是什么"到"怎么做"再到"具体落地"。建议新读者按序阅读：

```text
Layer 1 — 为什么（Why）
  └─ design-language.md    设计哲学、产品原则、AI 作为陪伴者

Layer 2 — 是什么（What）
  ├─ design-system.md      视觉规则（色彩/字体/间距/圆角/动效/主题）
  └─ interaction.md        交互规则（跳转/手势/反馈/状态/可访问性）

Layer 3 — 怎么做（How）
  ├─ components.md         组件库（Button/Card/Header/Record Card/Insight Card 等）
  └─ design-tokens.md      工程映射（Token → 代码）

Layer 4 — 具体落地（Apply）
  └─ home-page.md          首页规范（页面级应用示范）
```

| 层 | 文档 | 定位 | 回答的问题 |
|---|---|---|---|
| 1 | [`design-language.md`](design-language.md) | 设计哲学 | 为什么做这个产品？产品原则是什么？AI 的定位是什么？ |
| 2 | [`design-system.md`](design-system.md) | 视觉规则 | 色彩/字体/间距/圆角/动效/主题应该怎么用？ |
| 2 | [`interaction.md`](interaction.md) | 交互规则 | 页面怎么跳转？手势怎么用？状态怎么反馈？错误怎么处理？ |
| 3 | [`components.md`](components.md) | 组件库 | 有哪些组件？每个组件的结构/状态/尺寸/命名是什么？AI 开发规则（权威源） |
| 3 | [`design-tokens.md`](design-tokens.md) | 工程映射 | Token 的具体数值是什么？怎么映射到 Avalonia 资源字典和代码？ |
| 4 | [`home-page.md`](home-page.md) | 页面应用 | 首页应该长什么样？各区域怎么组织？信息权重怎么排？ |

## 文档职责边界

为避免内容重复，各文档的职责严格区分：

| 主题 | 权威文档 | 其他文档的处理方式 |
|---|---|---|
| 设计方向（微信+Notion+温暖+智能） | `design-tokens.md` | `design-system.md` 引用 |
| AI 开发规则（生成 UI / 新增组件 / 修改交互） | `components.md` | `design-tokens.md` / `interaction.md` / `home-page.md` 引用 |
| Token 具体数值（色值/字号/间距） | `design-tokens.md` | `design-system.md` / `components.md` 仅引用 Token 名 |
| 组件具体结构（字段/状态/尺寸） | `components.md` | `home-page.md` 仅引用组件名 |
| 首页页面级规范 | `home-page.md` | `components.md` 仅引用 |

## 维护规则

- 新设计决策优先写入本目录的当前规范，不再新增平行的 Design System / Design Language 文档。
- 大段历史资料、阶段性评审和废弃方案放入 `../archive/`。
- 如果设计规范影响代码实现，请同步更新 `components.md` 中的实现映射或在相关代码注释中标明 Token 名称。
- 跨文档引用的主题（如 AI 开发规则、Token 数值）只在一个权威文档中定义，其他文档用链接引用，避免内容漂移。

## 合并来源

| 文档 | 合并来源 |
|---|---|
| `design-language.md` | `design-language-v1/`：README、product-manual、brand-experience、interaction-language、platform-guidelines、ai-experience；`design-system-v0/`：Vision、Brand、Design-Principles |
| `design-system.md` | `design-system-v0/`：README、00-Vision、01-Brand、02-Design-Principles、03-Color-System；`design-language-v1/`：ui-design-system、ui-foundation、theme-system、motion-system、page-layout-specification |
| `components.md` | `design-language-v1/`：components、component-visual-specification、component-library-specification、code-mapping-specification、figma-ready-specification、page-layout-specification；外部补充：`Components Specification v1.0` |
| `design-tokens.md` | `design-language-v1/`：design-token-specification、figma-ready-specification、code-mapping-specification；外部补充：`design-tokens_v1.0.md` |
| `home-page.md` | 外部补充：`Home Page Specification v1.0`、`Home_Page_Design_V2.md` |
| `interaction.md` | `design-language.md` 的"交互语言"章节（理念级）；外部补充：`Interaction Specification v1.0`（工程级） |
