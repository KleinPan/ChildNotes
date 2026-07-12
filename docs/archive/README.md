# Archive 历史文档归档

`archive/` 用于保留历史文档原文，避免在合并重复 Design System、Components、Design Language 和工程文档时丢失上下文。

## 归档分区

| 目录 | 内容 | 当前替代文档 |
|---|---|---|
| [`design-language-v1/`](design-language-v1/) | 历史设计语言、产品手册、组件、Token、平台适配、AI 体验 | [`../design/design-language.md`](../design/design-language.md)、[`../design/components.md`](../design/components.md)、[`../design/design-tokens.md`](../design/design-tokens.md) |
| [`design-system-v0/`](design-system-v0/) | 历史品牌、愿景、设计原则、色彩系统 | [`../design/design-system.md`](../design/design-system.md) |
| [`engineering/`](engineering/) | 历史架构审视、后端迁移、发布清单、SDK、Git 迁移 | [`../development/architecture.md`](../development/architecture.md)、[`../development/backend.md`](../development/backend.md)、[`../release/app-store.md`](../release/app-store.md) |
| [`frontend-notes/`](frontend-notes/) | 前端专项问题、性能优化和调试复盘 | 视主题引用到当前开发文档 |

## 使用规则

- 归档文档可以引用，但不作为最新规范。
- 如果从归档文档恢复某项规则，请同步写入当前权威文档。
- 不删除归档内容，除非确认内容重复且已有 Git 历史可追溯。
