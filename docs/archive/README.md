# Archive 历史文档归档

`archive/` 用于保留阶段性、已废弃或仅供追溯的历史文档原文。仍会指导后续设计演进的 `design-system-v0/` 与 `design-language-v1/` 已移回 `../design/`，不再作为归档内容。

## 归档分区

| 目录 | 内容 | 当前替代文档 |
|---|---|---|
| [`engineering/`](engineering/) | 历史架构审视、后端迁移、发布清单、SDK、Git 迁移和配套图片素材 | [`../development/architecture.md`](../development/architecture.md)、[`../development/backend.md`](../development/backend.md)、[`../release/app-store.md`](../release/app-store.md) |
| [`frontend-notes/`](frontend-notes/) | 前端专项问题、性能优化和调试复盘 | 视主题引用到当前开发文档 |

## 使用规则

- 归档文档可以引用，但不作为最新规范。需要继续指导设计演进的资料应放在 `../design/`。
- 如果从归档文档恢复某项规则，请同步写入当前权威文档。
- 不删除归档内容，除非确认内容重复且已有 Git 历史可追溯。
