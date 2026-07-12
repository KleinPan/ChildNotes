# Architecture 架构说明

ChildNotes 采用 Avalonia 客户端、ASP.NET Core 后端和共享契约项目组成的跨平台架构。

## 仓库结构

```text
.
├── AiJi.slnx
├── ChildNotes.Shared/          # 前后端共享 DTO、实体基类、常量、同步协议
├── ChildNotes/                 # Avalonia 前端：共享 UI + Desktop/Android/iOS
├── ChildNotes.Backend/         # ASP.NET Core 后端：Api/Core/Infrastructure/Tests
├── docs/                       # 当前文档体系
└── scripts/deploy/             # 部署脚本
```

## 依赖方向

```text
Frontend App ─┐
              ├─> ChildNotes.Shared
Backend Core ─┘
Api -> Infrastructure -> Core -> Shared
```

- `ChildNotes.Shared` 只放前后端真正共享的 DTO、常量和协议。
- `ChildNotes.Backend/ChildNotes.Core` 定义后端领域实体、DTO、接口和业务异常。
- `ChildNotes.Backend/ChildNotes.Infrastructure` 承载 EF Core、外部服务、认证和业务服务实现。
- `ChildNotes.Backend/ChildNotes.Api` 承载 Controller、过滤器、中间件和启动配置。
- `ChildNotes/ChildNotes` 承载平台无关的 Avalonia 页面、ViewModel、Service、Control 和本地数据访问。

## 核心风险与演进方向

| 领域 | 当前关注点 | 建议方向 |
|---|---|---|
| 前端依赖管理 | 手写服务定位器影响测试和生命周期 | 逐步引入标准 DI / ViewModel Factory |
| 同步 | LWW 冲突解决可能静默覆盖 | 引入版本号、冲突检测或字段级合并 |
| 共享层边界 | Shared 容易收纳后端专用类型 | 只保留真正跨端契约 |
| 安全 | 账号、JWT、上传、隐私合规需要持续收紧 | 强化生产配置校验与文件校验 |

## 历史来源

详细架构审视原文保存在 [`../archive/engineering/architecture-review-report.md`](../archive/engineering/architecture-review-report.md)。
