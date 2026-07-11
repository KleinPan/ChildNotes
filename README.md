# ChildNotes / AiJi

ChildNotes（爱记）是一个跨平台育儿记录应用，面向家庭成员共同记录宝宝的喂养、睡眠、疫苗、里程碑和日常成长数据。仓库当前采用 **Avalonia 跨平台客户端 + ASP.NET Core 后端 + 前后端共享契约** 的组织方式。

## 一句话定位

> 本地优先、可同步、可扩展的育儿记录工具：先保证离线记录与家庭协作，再逐步增强 AI 分析、积分激励、运营后台和多端发布能力。

## 仓库结构

```text
.
├── AiJi.slnx                         # 根级解决方案：主应用项目入口
├── ChildNotes.Shared/                # 前后端共享 DTO、实体基类、常量、同步协议
├── ChildNotes/                       # Avalonia 前端（共享 UI + Desktop/Android/iOS）
├── ChildNotes.Backend/               # ASP.NET Core 后端（Api/Core/Infrastructure/Tests）
├── Docs/                             # 工程、迁移、发布、架构类文档
├── docs/                             # 产品体验与设计系统文档
└── scripts/deploy/                   # 后端服务器部署脚本与说明
```

## 技术栈速览

| 模块 | 技术 | 说明 |
|---|---|---|
| 客户端 | .NET 10、Avalonia 12、Semi.Avalonia、SQLite | 支持桌面、Android、iOS；本地优先记录与缓存 |
| 后端 | ASP.NET Core、EF Core、PostgreSQL、JWT | 提供认证、宝宝、记录、同步、AI、积分、后台管理 API |
| 共享层 | .NET 10 class library | DTO、实体基类、记录类型、同步协议在前后端复用 |
| 测试 | xUnit、ASP.NET Core Testing、Avalonia Headless | 前后端分别维护测试项目 |
| 部署 | Docker、docker-compose、systemd、bash scripts | 后端以容器/服务方式部署 |

## 解决方案入口

| 场景 | 入口 |
|---|---|
| 打开主应用项目 | `AiJi.slnx` |
| 前端开发与前端测试 | `ChildNotes/ChildNotes.slnx` |
| 后端开发与后端测试 | `ChildNotes.Backend/ChildNotes.Backend.slnx` |
| 共享契约修改 | `ChildNotes.Shared/ChildNotes.Shared.csproj` |

## 快速开始

### 1. 环境要求

- .NET SDK `10.0.301` 或兼容的 latest feature SDK（见 `global.json`）
- PostgreSQL（后端非 Testing 环境默认使用）
- Android/iOS 构建需要对应平台 SDK 与 workload

### 2. 还原与构建

```bash
dotnet restore AiJi.slnx
dotnet build AiJi.slnx
```

### 3. 运行桌面客户端

```bash
dotnet run --project ChildNotes/ChildNotes.Desktop/ChildNotes.Desktop.csproj
```

### 4. 运行后端 API

```bash
dotnet run --project ChildNotes.Backend/ChildNotes.Api/ChildNotes.Api.csproj
```

开发环境未配置 `Jwt:Secret` 时，后端会生成临时开发密钥；生产环境必须配置至少 32 字符的 JWT Secret。

## 文档导航

推荐先阅读文档总索引：[`docs/README.md`](docs/README.md)。

常用入口：

- 产品与体验：[`docs/design-language/product-manual.md`](docs/design-language/product-manual.md)
- 设计语言：[`docs/design-language/README.md`](docs/design-language/README.md)
- 品牌与视觉：[`docs/DesignSystem/README.md`](docs/DesignSystem/README.md)
- 架构审视：[`Docs/architecture-review-report.md`](Docs/architecture-review-report.md)
- 后端迁移：[`Docs/backend-migration-plan.md`](Docs/backend-migration-plan.md)
- 发布检查：[`Docs/app-store-launch-checklist.md`](Docs/app-store-launch-checklist.md)
- 部署说明：[`scripts/deploy/README.md`](scripts/deploy/README.md)

## 开发约定摘要

- 共享契约优先放在 `ChildNotes.Shared`，但仅放前后端真正共用的类型。
- 后端保持 `Api -> Infrastructure -> Core -> Shared` 的依赖方向。
- 前端平台无关逻辑放在 `ChildNotes/ChildNotes`，平台入口只承载启动和平台能力适配。
- 文档新增时优先放入 `docs/` 或 `Docs/` 并更新 `docs/README.md` 索引。
