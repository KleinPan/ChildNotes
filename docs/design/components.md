# Components 组件规范

组件体系将设计语言和设计系统转化为可复用 UI 单元。新增页面应优先复用本规范中的组件语义，而不是临时创建视觉相近但行为不同的组件。

## 组件分层

```text
Components
├── Foundation Components    # Button / Input / Avatar / Tag / Divider
├── Content Components       # Record Card / Timeline Card / Photo Card / Growth Summary
├── Interaction Components   # Bottom Action / Sheet / Dialog / Gesture Panel
└── AI Components            # AI Insight Card / AI Summary / Memory Reminder / Conversation Panel
```

## 核心组件规则

### Button

- Primary：创建记录、保存、生成 AI 总结等核心动作。
- Secondary：辅助操作，如筛选、补充信息、查看详情。
- Text：轻量操作，如跳过、稍后、查看协议。

### Record Card / Timeline Card

- 必须清晰展示记录类型、时间、宝宝、核心内容和同步状态。
- 疫苗、身高体重、喂养、睡眠等结构化记录可显示摘要字段。
- 长文本内容默认折叠，避免时间轴被单条记录压垮。

### Sheet / Dialog

- Sheet 用于记录表单、快捷选择和上下文操作。
- Dialog 用于高风险确认、权限说明和不可恢复操作。
- 不应把复杂长流程塞入 Dialog。

### AI Components

- AI 卡片必须标明来源和生成状态。
- AI 建议需要提供确认、编辑或忽略入口。
- 涉及健康、发育、疫苗等内容时应避免诊断口吻。

## 代码映射建议

| 组件语义 | 推荐落点 |
|---|---|
| 基础控件样式 | `ChildNotes/ChildNotes/Styles` 或 Avalonia Resource |
| 复合组件 | `ChildNotes/ChildNotes/Controls` |
| 页面状态与交互 | `ChildNotes/ChildNotes/ViewModels` |
| 记录类型 DTO / 常量 | `ChildNotes.Shared` |

## 历史来源

本规范提炼自 [`design-language-v1/README.md`](design-language-v1/README.md)。
