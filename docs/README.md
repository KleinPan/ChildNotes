# ChildNotes 文档中心

本文档中心用于整理 ChildNotes（爱记）的产品、设计、工程、部署与发布资料，避免资料分散在多个目录后难以查找。

## 阅读顺序建议

1. **先看项目全貌**：仓库根目录 [`README.md`](../README.md)
2. **理解产品与设计方向**：[`design-language/product-manual.md`](design-language/product-manual.md)、[`design-language/README.md`](design-language/README.md)
3. **理解工程架构**：[`../Docs/architecture-review-report.md`](../Docs/architecture-review-report.md)
4. **进行后端/同步相关开发**：[`../Docs/backend-migration-plan.md`](../Docs/backend-migration-plan.md)、[`../scripts/deploy/README.md`](../scripts/deploy/README.md)
5. **准备发布**：[`../Docs/app-store-launch-checklist.md`](../Docs/app-store-launch-checklist.md)

## 文档分区

### 产品与设计语言

| 文档 | 用途 |
|---|---|
| [`design-language/product-manual.md`](design-language/product-manual.md) | 产品说明、核心场景和功能范围 |
| [`design-language/README.md`](design-language/README.md) | 设计语言目录入口 |
| [`design-language/brand-experience.md`](design-language/brand-experience.md) | 品牌体验与情绪目标 |
| [`design-language/ui-foundation.md`](design-language/ui-foundation.md) | UI 基础规范 |
| [`design-language/components.md`](design-language/components.md) | 组件清单与使用说明 |
| [`design-language/interaction-language.md`](design-language/interaction-language.md) | 交互语言与动效反馈原则 |
| [`design-language/ai-experience.md`](design-language/ai-experience.md) | AI 功能体验规范 |
| [`design-language/platform-guidelines.md`](design-language/platform-guidelines.md) | 多平台适配规则 |

### 设计系统与视觉规范

| 文档 | 用途 |
|---|---|
| [`DesignSystem/README.md`](DesignSystem/README.md) | 旧版/品牌设计系统入口 |
| [`DesignSystem/00-Vision.md`](DesignSystem/00-Vision.md) | 产品视觉愿景 |
| [`DesignSystem/01-Brand.md`](DesignSystem/01-Brand.md) | 品牌规范 |
| [`DesignSystem/02-Design-Principles.md`](DesignSystem/02-Design-Principles.md) | 设计原则 |
| [`DesignSystem/03-Color-System.md`](DesignSystem/03-Color-System.md) | 色彩系统 |
| [`design-language/design-token-specification.md`](design-language/design-token-specification.md) | Design Token 规范 |
| [`design-language/figma-ready-specification.md`](design-language/figma-ready-specification.md) | 面向 Figma 落地的规格 |

### 架构、迁移与工程资料

| 文档 | 用途 |
|---|---|
| [`../Docs/architecture-review-report.md`](../Docs/architecture-review-report.md) | 当前架构审视、风险与改进建议 |
| [`../Docs/backend-migration-plan.md`](../Docs/backend-migration-plan.md) | 后端迁移与实施计划 |
| [`../Docs/git-repo-root-migration.md`](../Docs/git-repo-root-migration.md) | Git 仓库根目录迁移记录 |
| [`../Docs/sdk-list.md`](../Docs/sdk-list.md) | SDK / 工具链信息 |
| [`../scripts/deploy/README.md`](../scripts/deploy/README.md) | 部署脚本与服务器部署说明 |

### 发布与合规

| 文档 | 用途 |
|---|---|
| [`../Docs/app-store-launch-checklist.md`](../Docs/app-store-launch-checklist.md) | 应用商店上线检查清单 |
| [`../ChildNotes/ChildNotes/Assets/UserAgreement.md`](../ChildNotes/ChildNotes/Assets/UserAgreement.md) | 用户协议资产 |
| [`../ChildNotes/ChildNotes/Assets/PrivacyPolicy.md`](../ChildNotes/ChildNotes/Assets/PrivacyPolicy.md) | 隐私政策资产 |

### 前端专项问题记录

| 文档 | 用途 |
|---|---|
| [`../ChildNotes/ChildNotes/docs/Avalonia与小程序功能对比分析报告.md`](../ChildNotes/ChildNotes/docs/Avalonia与小程序功能对比分析报告.md) | Avalonia 与小程序能力对比 |
| [`../ChildNotes/ChildNotes/docs/异步加载机制全面审查与优化报告.md`](../ChildNotes/ChildNotes/docs/异步加载机制全面审查与优化报告.md) | 异步加载机制审查 |
| [`../ChildNotes/ChildNotes/docs/疫苗列表加载性能优化方案.md`](../ChildNotes/ChildNotes/docs/疫苗列表加载性能优化方案.md) | 疫苗列表性能优化 |
| [`../ChildNotes/ChildNotes/docs/疫苗补记面板性能优化.md`](../ChildNotes/ChildNotes/docs/疫苗补记面板性能优化.md) | 疫苗补记面板优化 |
| [`../ChildNotes/ChildNotes/docs/P1功能实现方案-隐私协议-DeepLink-推送通知.md`](../ChildNotes/ChildNotes/docs/P1功能实现方案-隐私协议-DeepLink-推送通知.md) | P1 功能实现方案 |
| [`../ChildNotes/ChildNotes/docs/TimeWheelPicker-双击问题调试经验.md`](../ChildNotes/ChildNotes/docs/TimeWheelPicker-双击问题调试经验.md) | TimeWheelPicker 调试经验 |

## 维护规则

- 新增文档后，请同步更新本索引。
- 产品/设计文档优先放入 `docs/design-language/` 或 `docs/DesignSystem/`。
- 工程、迁移、发布类文档优先放入 `Docs/`。
- 前端某个具体控件或性能问题的复盘可保留在 `ChildNotes/ChildNotes/docs/`，但需要在本索引中挂入口。
- 文档标题建议使用清晰中文名，文件名可用英文或中文，但同一目录内尽量保持风格一致。
