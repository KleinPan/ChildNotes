# ChildNotes ASP.NET 后端重写实施计划

## 背景与目标

将 Java Spring Boot 后端（`child-notes-backend-z-master`）重写为 ASP.NET Core 8 后端，让 Avalonia 客户端从「WebDAV 整库同步」改为「接入后端 API」，实现与小程序端一致的 C/S 架构家庭数据共享。

**核心问题**：当前 Avalonia 端用 WebDAV 整库同步，导致密码哈希泄露、角色信息全量共享、整库 LWW 覆盖丢数据、权限校验缺失。

**目标**：
- Avalonia 端接入 ASP.NET 后端，复用小程序端的家庭共享/权限模型
- 后端单点存储，多客户端实时请求
- 与 Java 后端 API 路径/响应格式对齐，小程序前端零改动
- 阶段 1 优先解决 Avalonia 端的账号/宝宝/记录/家庭共享核心流程

## 技术栈映射

| Java | ASP.NET |
|---|---|
| Spring Boot 2.6 | ASP.NET Core 8 Web API |
| MyBatis-Plus | EF Core 8（Code-First + Migration） |
| MySQL 8 | MySQL 8（不变） |
| 自实现 JWT | Microsoft.AspNetCore.Authentication.JwtBearer |
| `@TableLogic` | EF Core `HasQueryFilter` 软删除 |
| `@ConfigurationProperties` | `IOptions<T>` |
| OncePerRequestFilter | ASP.NET Core Middleware |
| ThreadLocal AuthContext | `IHttpContextAccessor` + Scoped `ICurrentUserService` |
| `Response<T>` | `ApiResponse<T>` + Action Filter 自动包装 |

## 项目结构

```
ChildNotes.Backend/
├── ChildNotes.Api/                  # Web API 启动项目
├── ChildNotes.Core/                 # 实体、DTO、接口
├── ChildNotes.Infrastructure/       # EF Core、Service 实现
└── ChildNotes.Tests/                # xUnit 测试
```

## 分阶段实施

### 阶段 1：MVP 后端（核心账号/宝宝/记录/家庭共享）

**目标**：Avalonia 端可接入后端，实现账号登录、宝宝管理、记录增删查、家庭成员共享。

**交付物**：
- 4 个项目骨架（Api/Core/Infrastructure/Tests）
- EF Core DbContext + MySQL Migration
- `ApiResponse<T>` 统一响应 + JWT 认证
- 3 个 Controller：
  - `AuthController`：注册/登录/获取当前用户/更新资料
  - `BabyController`：创建/列表/当前宝宝/家庭成员/加入家庭/修改自己角色
  - `RecordController`：14 种记录类型增删查 + 今日/历史/统计
- 5 张表：`app_user`、`baby`、`baby_member`、`child_record`、`user_points`
- Swagger 接口文档
- xUnit 冒烟测试

**验收标准**：
- Avalonia 端能用账号密码注册/登录
- 创建宝宝后自动建 owner 成员记录
- 邀请家人通过 babyId 加入家庭，后端为该主人所有宝宝建成员
- 每人只能修改自己的角色
- 所有 API 返回 `{state, msg, data}` 格式
- Swagger 可完整测试所有接口

### 阶段 2：完善体验（积分/上传/AI/限流）

**交付物**：
- `PointsController`（签到 + 积分账户 + 邀请奖励）
- `UploadController`（本地文件存储，OSS 后补）
- `AiAnalysisController`（DeepSeek 调用）
- 限流 + IP 黑名单中间件
- 邀请链接加入家庭流程

### 阶段 3：补全管理后台

**交付物**：
- `AdminAuthController` + `AdminStatsController` + `AdminLotteryController`
- `DiscussionController`
- 阿里云 OSS 接入

### 阶段 4：Avalonia 客户端改造

**交付物**：
- 新增 `ChildNotes.Services.Api` 项目（HttpClient 封装）
- `AuthService`/`BabyService`/`RecordService` 改为调后端 API
- 本地 SQLite 退化为离线缓存（可选）
- 废弃 `SyncService`/`WebDavClient`/`SyncTrigger`

## 阶段间衔接方案

1. **阶段 1 → 阶段 2**：阶段 1 的 JWT/ApiResponse/DbContext 基础设施完全复用，阶段 2 只新增 Controller/Service
2. **阶段 2 → 阶段 3**：阶段 2 的限流中间件被阶段 3 的 admin 接口复用；OSS 服务在阶段 3 替换阶段 2 的本地存储
3. **阶段 3 → 阶段 4**：阶段 3 完成后后端 API 完整可用，阶段 4 改造 Avalonia 端接入。改造时保留旧 WebDAV 代码作 fallback，通过配置开关切换

## 与现有项目的集成方案

### 数据交互契约

后端 API 路径与响应格式完全对齐 Java 后端，Avalonia 端通过 HttpClient 调用。

**统一响应**：
```json
{ "state": "000000", "msg": "success", "data": {} }
```

**JWT 认证**：`Authorization: Bearer <token>`，Payload 含 `uid` + `openid`

**数据模型对齐**：实体字段与 Avalonia 端 [Models/](file:///e:\0_Code\5_Git\AiJi\ChildNotes\ChildNotes\Models) 完全一致

### 集成接口清单（阶段 1）

| 接口 | 方法 | 路径 | 说明 |
|---|---|---|---|
| 注册 | POST | /api/auth/register | 账号密码注册（Java 端无，Avalonia 端需要） |
| 登录 | POST | /api/auth/login | 账号密码登录（Java 端无，Avalonia 端需要） |
| 当前用户 | GET | /api/auth/me | 获取当前登录用户 |
| 更新资料 | PUT | /api/auth/profile | 更新昵称/头像/性别 |
| 当前宝宝 | GET | /api/baby/current | 获取当前宝宝 |
| 宝宝列表 | GET | /api/baby/list | 用户可访问的所有宝宝 |
| 创建宝宝 | POST | /api/baby/add | 创建宝宝 + 自动建 owner 成员 |
| 更新宝宝 | PUT | /api/baby/update | 更新宝宝信息 |
| 家庭成员 | GET | /api/baby/family/members | 列出家庭成员 |
| 修改自己角色 | PUT | /api/baby/family/my-role | 只能改自己 |
| 加入家庭 | POST | /api/baby/family/join | 通过 babyId 加入 |
| 今日记录 | GET | /api/records/today | 按类型分组 |
| 历史记录 | GET | /api/records/history | 倒序分页 |
| 新增记录 | POST | /api/records/{type} | 14 种类型 |
| 删除记录 | DELETE | /api/records/{id} | 逻辑删除 |

## 质量控制与测试标准

### 测试分层

1. **单元测试**（xUnit）：Service 层业务逻辑
2. **集成测试**（WebApplicationFactory）：API 端到端
3. **契约测试**：与 Java 后端 API 响应格式对齐验证

### 验收测试用例（阶段 1）

- 注册/登录/获取当前用户
- 创建宝宝 → 自动建 owner 成员
- 邀请家人加入家庭 → 后端为该主人所有宝宝建成员
- 修改自己角色 → 成功；修改他人角色 → 403
- 14 种记录类型增删查
- 今日记录按类型分组
- 未登录访问 → 401

### 构建验证

```bash
cd ChildNotes.Backend && dotnet build -v quiet --nologo
cd ChildNotes.Backend && dotnet test
cd ChildNotes.Backend && dotnet run --project ChildNotes.Api  # Swagger: http://localhost:5000/swagger
```

## 交付物清单

- [ ] 实施计划文档（本文档）
- [ ] 阶段 1 源代码（4 个项目）
- [ ] EF Core Migration 脚本
- [ ] Swagger 接口文档
- [ ] xUnit 测试报告
- [ ] 与 Avalonia 端集成验证结果
