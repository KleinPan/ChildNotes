# 邮箱验证码认证重构：最终实施方案（v3）

> **文档状态**：已批准，开始实施
> **创建日期**：2026-08-14
> **版本**：v3 — 最终版，基于完整审阅反馈

---

## 一、最终决策

本项目未正式上线，只有一个真实用户（开发者本人）。

> **不考虑旧客户端数据库兼容，不做旧认证迁移兼容。**
>
> **以云端 PostgreSQL 现有业务数据为准。**
>
> **后端保留现有用户 ID 和所有业务数据关系，迁移到邮箱认证。**
>
> **客户端旧数据可以直接清除，新版本登录后从云端全量拉取恢复。**

## 二、10 条铁律

1. 先备份，再修改
2. 不保留旧密码体系
3. 必须保留现有真实云端用户的 AppUser.Id
4. 正式邮箱登录后必须得到原 UserId
5. 客户端旧数据可以全部清除，不做数据库迁移兼容
6. 首次正式登录以云端为准，只做 Full Pull，不 Push
7. 后续正常同步继续使用现有 Pull → Merge → Push
8. 不重写现有同步协议
9. Token 不得保存 SQLite 明文
10. 任何认证失败不得删除业务数据

---

## 三、后端认证体系重构

### 3.1 AppUser 新结构

```
AppUser
├── Id                    ← 保留现有值
├── Email                 ← 唯一，新增
├── EmailVerifiedAt       ← 新增
├── NickName
├── AvatarUrl
├── Gender
├── MembershipExpireAt
├── CreatedAt
└── UpdatedAt
```

**删除**：`Username`、`PasswordHash`（从 Shared AppUserBase 中删除，不是改 nullable）。

### 3.2 废弃接口

| 接口 | 处理 |
|---|---|
| `POST /api/auth/register` | 删除 |
| `POST /api/auth/login` | 删除 |

### 3.3 新增接口

#### POST /api/auth/send-code

```json
{ "email": "example@example.com" }
```

#### POST /api/auth/verify-code

```json
{ "email": "example@example.com", "code": "123456" }
```

统一逻辑：Email 存在→登录，不存在→自动创建 AppUser→登录。客户端不区分注册/登录。

#### POST /api/auth/refresh

```json
{ "refreshToken": "..." }
```

返回新 accessToken + 新 refreshToken（Rotation）。

### 3.4 认证响应

```json
{
  "accessToken": "...",
  "refreshToken": "...",
  "expiresIn": 3600,
  "user": {
    "id": "...",
    "email": "...",
    "nickName": "...",
    "avatarUrl": "...",
    "gender": 0,
    "membershipExpireAt": null,
    "isMember": false
  }
}
```

---

## 四、验证码安全

- 6 位数字
- 有效期 5 分钟
- 同一邮箱 60 秒内只能重新发送一次
- 验证错误最多 5 次
- 验证成功立即失效
- 重新发送后旧验证码立即失效
- 不保存明文，只保存 Hash
- 恒定时间比较（`CryptographicOperations.FixedTimeEquals`）
- 限流：单邮箱频繁发送、单 IP 高频请求

---

## 五、邮件发送

```
IEmailSender（抽象）
    ↓
MailKitEmailSender（实现）
    ↓
QQ 邮箱 SMTP（授权码）
```

- 使用 QQ SMTP 授权码，不使用邮箱登录密码
- 授权码不提交 Git（配置/环境变量/Secret）
- 邮件发送逻辑与认证业务解耦
- 后续可替换其他邮件服务

---

## 六、RefreshToken

### 6.1 服务端存储

```
RefreshToken
├── Id
├── UserId
├── TokenHash         ← 不存明文
├── ExpiresAt
├── CreatedAt
├── RevokedAt         ← nullable
└── DeviceId          ← 如现有架构需要
```

### 6.2 Rotation

```
RefreshToken 验证成功
    ↓
旧 RefreshToken 撤销（RevokedAt = now）
    ↓
生成新 AccessToken + 新 RefreshToken
    ↓
返回客户端
```

---

## 七、客户端重构

### 7.1 删除

- `app_user` 表、`user_session` 表
- `UserRepository`、`SessionRepository`
- 本地注册、本地用户名密码登录
- PBKDF2 PasswordHash
- `TryRegisterAndLoginAsync`、`VerifyRemoteUserIdAsync`、`UpdateIdIfDifferent`
- 同步中 Username + Password 自动登录逻辑

### 7.2 身份结构

```
sync_config
├── cloud_user_id       ← 唯一权威来源
├── last_sync_at
├── server_url
├── device_id
├── last_sync_status
└── last_sync_msg
```

### 7.3 Token 存储

```
ISecureStorage
├── AccessToken
└── RefreshToken
```

| 平台 | 实现 |
|---|---|
| Android | Android Keystore |
| Windows | DPAPI |

### 7.4 App 启动

```
CloudUserId == null → 离线模式 → 直接正常使用 App
CloudUserId != null → 已登录 → 允许云同步
```

不登录也可以永久正常使用本地 SQLite。登录的作用是开启云同步、多设备使用和共享。

### 7.5 同步认证

```
CloudUserId
    ↓
SecureStorage → AccessToken
    ↓
请求 API
    ↓
401 → RefreshToken → /api/auth/refresh
    ├─ 成功 → 更新 SecureStorage → 重试
    └─ 失败 → 停止同步，提示重新登录，保留所有 SQLite 业务数据
```

### 7.6 首次登录同步

```
新安装 App → SQLite 为空
    ↓
正式邮箱验证码登录
    ↓
返回原 AppUser.Id = A
    ↓
保存 CloudUserId = A
    ↓
LastSyncAt = null
    ↓
Full Pull Only（不 Push）
    ↓
SQLite 恢复云端数据
    ↓
LastSyncAt = ServerTime
```

后续正常使用：Pull → Merge → Push（复用现有同步协议）。

---

## 八、实施顺序

### Step 0：备份

1. PostgreSQL 完整备份
2. Android SQLite 备份（额外保险）
3. Git 创建 Tag / 确认当前提交点

### Step 1：后端数据库与现有用户迁移

1. 修改 AppUser Schema（删除 Username/PasswordHash，新增 Email/EmailVerifiedAt）
2. EF Core Migration
3. 执行 Migration
4. 给现有唯一用户设置正式邮箱（保持原 Id 不变）
5. 验证：正式邮箱查询返回原 AppUser.Id

### Step 2：后端新认证

1. VerificationCodeService（生成/校验/消费验证码）
2. EmailSender + MailKitEmailSender（QQ SMTP）
3. RefreshToken 实体 + 服务（Hash + Rotation）
4. AuthService 新方法（SendCodeAsync / VerifyCodeAsync / RefreshAsync）
5. AuthController 新端点（send-code / verify-code / refresh）
6. 删除旧接口（register / login）
7. 全局搜索清理 Username/PasswordHash 残留引用
8. 测试：正式邮箱登录返回原 UserId + 全新邮箱自动创建新用户

### Step 3：客户端重构

1. 删除旧认证体系（UserRepository / SessionRepository / AppUser / 旧 AuthService）
2. DbInitializer schema v5（删除 app_user/user_session 建表，sync_config 加 cloud_user_id）
3. SecureStorage 实现（Android Keystore / Windows DPAPI）
4. AuthService 重写（邮箱验证码登录 + 登出 + TryRestoreLogin）
5. ApiSyncService 改造（EnsureTokenAsync → SecureStorage + RefreshToken）
6. SyncConfigRepository 改造（加 cloud_user_id，清除 username/password/token）
7. LoginView 重写（邮箱 + 验证码输入）
8. ServiceProvider 改造

### Step 4：同步接入与首次初始化

1. 未登录 → 不同步
2. 正式邮箱登录 → 保存 CloudUserId → LastSyncAt=null → Full Pull Only
3. 后续 → Pull → Merge → Push（复用现有同步协议）

### Step 5：端到端验证

- 正式账号：清除 App 数据 → 安装新版 → 正式邮箱登录 → 确认 UserId == 原 UserId → Full Pull → 数据完整恢复
- 新用户：测试邮箱 → 验证码 → 自动创建账号 → 创建数据 → 正常同步
- 离线：清除 App 数据 → 不登录 → 创建数据 → 重启 → 数据仍存在 → 不发同步请求

---

## 九、不改动清单

| 模块 | 原因 |
|---|---|
| `SyncTrigger.cs` | 纯调度 |
| `DbConnectionFactory.cs` | 连接管理 |
| 后端 `JwtTokenService.cs` | 与登录方式无关 |
| 后端 `CurrentUserService.cs` | 基于 uid claim |
| 后端 `SyncController.cs` / `SyncService.cs` | 按 UserId 过滤 |
| 后端 `BabyAccessService.cs` / `BabyMember.cs` | 权限模型 |
| 后端所有业务服务 | RecordService / BabyService 等 |
| 后端 Admin 认证 | 独立体系 |
| `SyncProtocol.cs` | 同步协议不变 |
| 现有 Pull/Push 主体逻辑 | 复用，只改认证前置 |
