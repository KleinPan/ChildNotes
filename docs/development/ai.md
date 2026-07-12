# AI 功能说明

ChildNotes 的 AI 能力用于降低记录整理成本、发现成长规律和生成温和的阶段性总结。AI 是陪伴者，不是诊断者或决策者。

## 体验原则

- 原始记录始终属于用户，AI 输出必须可确认、可修改、可拒绝。
- AI 不应替代父母表达，不应制造焦虑。
- 涉及健康、发育、疫苗等内容时，输出必须避免医疗诊断式口吻。
- AI 需要解释依据，例如引用最近记录趋势，而不是只给结论。

## 当前能力

| 能力 | 说明 |
|---|---|
| 自然语言记录解析 | 将用户输入拆分为结构化记录候选 |
| 成长分析 | 聚合一段时间的记录生成总结或建议 |
| AI 记忆卡片 | 在时间轴或报告中呈现温和洞察 |
| 积分消耗 | 后端按配置扣减 AI 分析成本 |

## 工程边界

- 前后端共享解析 DTO 位于 `ChildNotes.Shared/Dtos/AiNoteParseDtos.cs`。
- 后端 AI 服务接口位于 `ChildNotes.Backend/ChildNotes.Core/Services/`。
- DeepSeek 调用与外部集成位于 `ChildNotes.Backend/ChildNotes.Infrastructure/External/`。
- 前端 AI 体验应遵循 [`../design/design-language.md`](../design/design-language.md) 与 [`../design/components.md`](../design/components.md)。

## 历史来源

- AI 体验规范原文：[`../archive/design-language-v1/ai-experience.md`](../archive/design-language-v1/ai-experience.md)
- 架构审视原文：[`../archive/engineering/architecture-review-report.md`](../archive/engineering/architecture-review-report.md)
