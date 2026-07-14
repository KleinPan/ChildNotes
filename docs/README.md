# ChildNotes 文档中心

本文档中心是 ChildNotes（爱记）的统一文档入口。当前目录已经按「设计 / 开发 / 发布 / 归档」重组，重复内容已精简到当前规范；仍需指导后续演进的 `design-system-v0` 与 `design-language-v1` 保留在 `design/`，不归档。

## 目录结构

```text
docs/
├── README.md
├── design/
│   ├── README.md
│   ├── design-language.md
│   ├── design-system.md
│   ├── components.md
│   ├── design-tokens.md
│   ├── design-system-v0/
│   └── design-language-v1/
├── development/
│   ├── architecture.md
│   ├── backend.md
│   ├── ai.md
│   └── backup.md
├── release/
│   └── app-store.md
└── archive/
```

## 推荐阅读顺序

1. 项目全貌：[`../README.md`](../README.md)
2. 设计语言：[`design/design-language.md`](design/design-language.md)
3. 设计系统：[`design/design-system.md`](design/design-system.md)
4. 组件与 Token：[`design/components.md`](design/components.md)、[`design/design-tokens.md`](design/design-tokens.md)
5. 工程架构：[`development/architecture.md`](development/architecture.md)
6. 后端与 AI：[`development/backend.md`](development/backend.md)、[`development/ai.md`](development/ai.md)
7. 发布准备：[`release/app-store.md`](release/app-store.md)

## 当前权威文档

### 设计

| 文档 | 说明 |
|---|---|
| [`design/README.md`](design/README.md) | 设计文档入口与维护规则 |
| [`design/design-language.md`](design/design-language.md) | 产品体验、设计理念、交互、平台适配与 AI 体验原则 |
| [`design/design-system.md`](design/design-system.md) | 视觉系统、品牌、色彩、布局、动效与可访问性 |
| [`design/components.md`](design/components.md) | 组件分类、核心组件、AI 组件和实现映射 |
| [`design/design-tokens.md`](design/design-tokens.md) | 颜色、字体、间距、圆角、阴影、动效 Token |
| [`design/design-system-v0/`](design/design-system-v0/) | Design System 后续演进指导资料 |
| [`design/design-language-v1/`](design/design-language-v1/) | Design Language 后续演进指导资料 |

### 开发

| 文档 | 说明 |
|---|---|
| [`development/architecture.md`](development/architecture.md) | 仓库结构、依赖方向、架构风险与演进方向 |
| [`development/backend.md`](development/backend.md) | 后端结构、运行命令、能力边界与部署入口 |
| [`development/ai.md`](development/ai.md) | AI 体验原则、当前能力与工程边界 |

### 发布

| 文档 | 说明 |
|---|---|
| [`release/app-store.md`](release/app-store.md) | Android/iOS 应用商店发布与合规清单 |

### 归档

| 目录 | 内容 |
|---|---|
| [`archive/engineering/`](archive/engineering/) | 历史架构、迁移、发布、SDK 文档原文 |
| [`archive/frontend-notes/`](archive/frontend-notes/) | 前端专项问题、性能优化和调试复盘原文 |

## 维护规则

- 新文档默认进入 `design/`、`development/` 或 `release/`，不要再新增 `Docs/`、`docs/DesignSystem/`、`docs/design-language/` 等平行目录。
- 重复主题先合并到当前权威文档；只有阶段性、废弃或仅供追溯的旧版本才移入 `archive/`。
- `design/design-system-v0/` 与 `design/design-language-v1/` 是未来指导资料，不移入 `archive/`。
- 归档文档只做保留和必要链接修复，不作为最新规范引用。
- 新增或移动文档后必须更新本 README 和相关交叉链接。
