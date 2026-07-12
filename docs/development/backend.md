# Backend 后端开发说明

ChildNotes 后端采用 ASP.NET Core + EF Core + PostgreSQL，按 Api / Core / Infrastructure / Tests 分层组织。

## 项目结构

```text
ChildNotes.Backend/
├── ChildNotes.Api/              # Web API 启动项目、Controllers、Filters、Middleware
├── ChildNotes.Core/             # 实体、DTO、服务接口、配置、异常
├── ChildNotes.Infrastructure/   # DbContext、EF Migration、认证、服务实现、外部客户端
└── ChildNotes.Tests/            # xUnit 测试
```

## 运行命令

```bash
dotnet restore ChildNotes.Backend/ChildNotes.Backend.slnx
dotnet build ChildNotes.Backend/ChildNotes.Backend.slnx
dotnet test ChildNotes.Backend/ChildNotes.Tests/ChildNotes.Tests.csproj
dotnet run --project ChildNotes.Backend/ChildNotes.Api/ChildNotes.Api.csproj
```

## 后端能力边界

- 认证：注册、登录、JWT、当前用户。
- 宝宝与家庭：宝宝资料、家庭成员、邀请与权限。
- 记录：多类型育儿记录、历史、统计与同步。
- AI：记录解析、成长分析、积分消耗。
- 运营：积分、签到、抽奖、管理员后台。
- 上传：本地/OSS 文件上传能力。

## 配置与部署

- 开发/Testing 环境可使用临时 JWT Secret；生产环境必须配置至少 32 字符的 `Jwt:Secret`。
- 非 Testing 环境使用 PostgreSQL，并在启动时应用 EF Core Migration。
- 部署脚本位于 [`../../scripts/deploy/`](../../scripts/deploy/)。

## 历史来源

- 后端迁移计划：[`../archive/engineering/backend-migration-plan.md`](../archive/engineering/backend-migration-plan.md)
- 部署说明：[`../../scripts/deploy/README.md`](../../scripts/deploy/README.md)
