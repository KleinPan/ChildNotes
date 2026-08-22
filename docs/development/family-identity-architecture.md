# Family 身份架构（v4 定稿）

> 本文是身份模型的唯一权威规格（single source of truth），整合多轮架构审查与代码核实的最终结论。
> 历史讨论过程不在此保留；实施以本文为准，与本文冲突的旧描述一律以本文修正。
> 状态：**已定稿，实施中**（阶段划分见第 11 节）。

## 1. 背景与路线决策

原有模型为 User-centric（登录后本地数据 user_id 迁移为 CloudUserId，登出迁回 LocalUserId），存在：

- 卸载重装后 LocalUserId/DeviceId 变为全新 GUID，云端积累孤儿设备记录
- A 登录 → 登出 → B 登录的静默数据串联路径
- 登录/登出双向迁移 + 补偿机制复杂且脆弱

而后端代码已存在半成型家庭共享基建（已核实）：

- baby 已是 owner + 成员共享模型（`GetAccessibleBabyIdsAsync`）
- join request 邀请流程已实现
- milestone 同步刻意保留创建者 UserId 透传（为共享预留）

**路线决策：Family-now。** 产品未正式发布（仅开发者数据），现在一次性把数据归属改为 Family，避免上线后带着生产数据做 User → Family 二次迁移。

## 2. 最终决策表

| 项 | 决策 |
|---|---|
| 数据归属路线 | Family-now（定案） |
| 家庭业务数据归属 | FamilyId，核心同步表全表冗余 + `INDEX(FamilyId, UpdatedAt)` |
| 积分/签到/AI 配额/个人任务 | per-User；家庭配额留给未来订阅产品，不进基础模型 |
| user_points 清理时机 | 换**云账号**时清理；换家庭**不动** |
| baby_members 迁移 | 按成员集合分组建 Family（不做并集，防权限扩大） |
| 本地业务表 user_id 列 | 保留物理列，语义降级为 LocalDataSpaceId |
| user_points.user_id | 真实 CloudUserId（个人表与家庭表代码显式分界） |
| 登录 API 形状 | 返回 `families[] + currentFamilyId`（一人多家庭预留） |
| 绑定记忆字段 | `last_bound_family_id`（单值，语义为"最近绑定"） |
| cross-family skip | 服务端校验 existing.FamilyId ≠ JWT family → terminal；返回 skippedForeignIds |
| 服务端信任边界 | payload FamilyId 仅作路由/日志；归属以 JWT + FamilyMember 鉴权为准 |
| Owner 模型 | `FamilyMember.Role = Owner` 单真相 + 单 Owner 唯一约束；Family 表**无** OwnerUserId |
| join request 存量 | Pending 全部失效清理 |
| rebind 顺序 | 停 Trigger → 等 `_syncLock` → 事务 → Full Pull Only → 常规 Push |
| 阶段划分 | 0 → 1A → 1B → 1C → 2 → 3 → 4 |

### 已废弃的机制（禁止恢复）

- `MigrateUserId(L → C)` / `MigrateUserId(C → L)` 登录登出迁移
- LastCloudUserId 启动补偿迁移
- 任何"为换绑修改业务表 user_id"的路径

## 3. 身份模型

| 身份 | 含义 | 不变量 |
|---|---|---|
| DeviceId（D） | 设备标识 | X-Device-Id / 推送注册 / 冲突归因；与登录态无关 |
| LocalDataSpaceId（原 LocalUserId） | 本机数据空间 Id | 家庭业务表本地 user_id 恒为此值；登录/登出/换绑均不改 |
| CloudUserId（C） | 登录凭证对应的云账号（JWT 主体） | 仅用于：个人数据（积分/签到/AI 配额）归属 + FamilyMember 成员身份；**不拥有家庭数据** |
| FamilyId（F） | 家庭数据空间 | Baby/Record/Feeding/Growth/Diary/Photo 的真正归属 |
| last_bound_family_id | 本数据空间最近绑定的家庭 | 用于换绑检测；除"清除本地数据"外永不清空（含 SoftLogout/401） |

两类数据、两类表的显式分界（代码中必须体现，不允许一个 user_id 列隐式承担双重语义）：

```text
家庭业务表（Baby/Record/Milestone/Feeding/Growth/Diary/Photo/…）
  云端：FamilyId 归属
  本地：user_id = LocalDataSpaceId（兼容列，逐步清理）

个人表（UserPoints/SignIn/AI Usage/RefreshToken/…）
  云端+本地：UserId = 真实 CloudUserId
```

## 4. DeviceId / LocalDataSpaceId 派生（ANDROID_ID）

```
DeviceId         = 既有值 ?? SHA256("childnotes-device-v1:"       + ANDROID_ID) ?? GUID
LocalDataSpaceId = 既有值 ?? SHA256("childnotes-local-user-v1:"   + ANDROID_ID) ?? GUID
```

- 不变量：sync_config 已有非空值 → 永远直接使用；为空 → seed 可用则派生，否则 GUID；写入后永久冻结
- 初始化链：`Application.OnCreate()` 设置 `IDeviceIdentityProvider.Current` → Avalonia 启动 → ServiceProvider → EnsureDeviceId / EnsureLocalUserId
- 新增 `IDeviceIdentityProvider`（默认返回 null，桌面端回退 GUID）+ `AndroidDeviceIdentityProvider`（读 Settings.Secure.ANDROID_ID）
- 派生 ID 定性为假名化设备标识符（非匿名）；Google Play Data safety 按 "Device or other IDs" 申报
- 接受边界：恢复出厂 ANDROID_ID 可能变（= 新设备）；清除本地数据后派生得到相同值是正确行为（清数据 ≠ 换设备）

## 5. 云端模型

### 5.1 新增实体

```text
Family:        Id, Name, CreatedTime            （无 OwnerUserId）
FamilyMember:  Id, FamilyId, UserId, Role, UNIQUE(FamilyId, UserId)
               Role ∈ { Owner, Member, Readonly }
```

- Owner 唯一真相：`FamilyMember.Role = Owner`，以部分唯一索引保证单 Owner
- Family 删除策略：MVP 不支持删除 Family；Owner 转让 = 同事务改两行 Role
- 必须保证 Family 至少存在一个 Owner（业务事务校验，禁止删除最后一个 Owner）

### 5.2 业务表加列

Baby/Record/Milestone/Feeding/Growth/Diary/Photo 全表冗余 `FamilyId`（不采用仅 Baby 挂 + JOIN 方案——同步 upsert/权限过滤/skip 校验都需要直接分区键）：

```sql
ALTER TABLE baby     ADD COLUMN family_id TEXT NOT NULL DEFAULT '';
ALTER TABLE record   ADD COLUMN family_id TEXT NOT NULL DEFAULT '';
-- …同理其余家庭业务表
CREATE INDEX ix_<table>_family_updated ON <table>(family_id, updated_at);
```

### 5.3 唯一索引审计

- 家庭表中原 `UNIQUE(UserId, x)` → `UNIQUE(FamilyId, x)`
- 个人表（user_points/sign_in/ai_usage/refresh_tokens）保持 User 维度**不动**

### 5.4 存量迁移（未发布，仅开发者数据，一次性 + 幂等）

```text
1. 按 baby_members 成员集合分组建 Family（成员集合相同的 baby 归同一 Family）
   —— 禁止同 Owner 全部成员取并集（会扩大权限：B 突然能看 Baby2）
2. Owner → FamilyMember(Role=Owner)；成员 → FamilyMember(Role=Member)
3. 各 baby 的家庭业务数据回填 FamilyId
4. Pending join request 全部失效清理
5. Accepted 的按实际成员关系生成 FamilyMember
```

### 5.5 服务端信任边界（安全红线）

```csharp
// 禁止：
entity.FamilyId = request.FamilyId;   // 直接信任客户端 payload

// 必须：
var familyId = ResolveAuthorizedFamily(jwtUserId, request.FamilyId);
// = 校验 FamilyMember 存在且 Role 有写权限后，以鉴权上下文为准
```

## 6. 同步协议改造

### 6.1 Push（客户端 Mapper 改写）

- `MapToBabyItem / MapToRecordItem / MapToMilestoneItem` 等：UserId/FamilyId 字段一律注入**当前授权 FamilyId**，禁止读 entity.UserId
- SignIn 等**个人数据** Push：注入当前 CloudUserId（不随家庭切换）
- 已核实：`GetByUpdatedAt` 无 user_id 条件（数据空间整体上送），Mapper 是唯一身份注入点；实施时全局枚举 Push 路径确认无绕过

### 6.2 Pull（客户端 Mapper 改写）

- 家庭业务表 Mapper：本地 UserId 一律写 LocalDataSpaceId（幂等）
- 个人表 Mapper（user_points/sign_in）：写当前 CloudUserId
- Pull 冲突沿用现有 LWW（`WHERE excluded.updated_at > local.updated_at`），不新增机制
- Full Pull 复用现有 `isFirstLogin → Full Pull Only` 规则（语义扩展为"绑定新家庭"）

### 6.3 Cross-family skip（terminal，防跨家庭覆盖）

风险（已核实）：RecordId 客户端 GUID 全局唯一，服务端 upsert 按全局 Id 查找且不校验归属——曾同步到家庭 G 的记录，换绑 F 后再 Push 会 LWW 覆盖 **G 的云端数据**。

服务端：

```csharp
if (existing != null && existing.FamilyId != currentFamilyId)
{
    skippedForeign.Add(item.Id);   // terminal skip，计入响应
    continue;
}
```

- 响应（SyncBatchResponse 扩展）：各实体类型新增 `skippedForeignIds`（Id 列表，非仅计数）
- 原因码最小化两类（同批先 baby 后 record 的既定结构下，"baby 尚未 Push"场景不存在）：
  - `ForeignFamily`（含 babyId-not-accessible 级联）→ terminal
  - 其他失败 → retryable
- milestone：UserId 保留创建者透传（家庭共享预留），但 FamilyId 归属校验同样适用

客户端：

```text
upserted + skippedForeign == count → MarkSynced 全批（foreign 行记录冲突日志后视为终态）
upserted + skippedForeign <  count → 不 MarkSynced，真实失败重试
"部分丢弃"警告排除 skippedForeign 行
```

### 6.4 换绑（rebind）事务

```text
确认框弹出前：停止 SyncTrigger
await _syncLock（复用现有 SemaphoreSlim，禁止新造锁；禁止 UI 线程同步阻塞）
BEGIN TX:
  UPDATE sync_config SET cloud_user_id=…, current_family_id=F,
                         last_bound_family_id=F, last_sync_at=NULL
  UPDATE baby/child_record/milestone SET synced_at = NULL   -- sign_in 无此列
COMMIT
释放锁 → Full Pull Only → 之后常规同步 Push
```

换绑语义（已定案，禁止用 Id 重生成"兑现迁移"——二次换绑会产生无法去重的重复数据）：

| 数据 | 换绑 F 后结局 |
|---|---|
| 从未同步过的本机数据 | 作为新数据 Push 给 F |
| 曾同步到 G 的记录（含后续编辑） | 永久留本机（cross-family skip 拦截并 MarkSynced） |
| 换绑后新建记录 | 正常同步 F |
| 积分/签到 | **不动**（per-User，与家庭无关） |
| 换回原家庭 G | 滞留编辑恢复同步（清 synced_at 的核心价值） |

### 6.5 换云账号（区别于换家庭）

```text
本地 user_points / sign_in 按 user_id UNIQUE → 换账号登录前清理本地个人表行，
新账号数据由 Pull 重建。触发条件 = CloudUserId 变更（≠ FamilyId 变更）。
```

## 7. 登录 / 登出状态机

### 7.1 登录（VerifyCode 成功后）

```text
服务端返回 families[] + currentFamilyId（MVP 单家庭自动选择）
1. C != null（已登录）：换号 → 硬拒绝："请先退出登录"
2. C == null：
   本地无数据                → 直接绑定
   last_bound_family == F    → 同家庭重登，静默绑定（最高频，不弹框）
   last_bound_family = G ≠ F → 弹换绑确认框（文案见 7.2）
```

### 7.2 换绑确认框文案（如实声明语义）

> 此设备本机数据曾关联另一个家庭。绑定到新家庭后：
> - 新建的记录将同步到新家庭
> - 既有历史记录（含后续修改）将保留在本机、不会同步到新家庭
> - 原家庭云端数据不受影响
>
> 是否继续？

- 取消：本地状态零改动
- 继续：执行 6.4 rebind 事务

### 7.3 登出（LogoutAsync / SoftLogout / Token 失效 / 401 全部路径统一）

```text
停止同步 → 清 Token/RefreshToken → C = null → 保留 last_bound_family_id → 结束
```

不迁移任何数据；用户离线立即可用（本地 user_id 恒为 LocalDataSpaceId，天然可见）。

## 8. 本地身份 API

```csharp
GetLocalDataSpaceId()   // 家庭业务表本地查询
GetCloudUserId()        // 个人数据 + 云端身份（可 null）
GetCurrentFamilyId()    // 当前绑定家庭（可 null）
GetDeviceId()           // X-Device-Id / 推送 / 冲突归因
```

- `AppState.UserId`（`C ?? L` 混合语义）删除；消费点按第 10 节阶段 0 清单逐个归类
- sync_config 新增列：`current_family_id`、`last_bound_family_id`（AddColumnIfNotExists 增量）

## 9. 已知接受的边界（非 bug，勿"修复"）

1. 曾同步旧数据换绑后永久留本机——确认框文案已如实声明
2. 引用原家庭宝宝档案的新记录无法同步新家庭（用户在新家庭重建档案后恢复）
3. 一人多家庭 UI 留阶段 4；API 形状已预留（families[]）
4. 家庭配额类订阅产品（FamilyQuota）属未来产品设计，不进当前模型
5. 恢复出厂 = 新设备；卸载重装后 last_bound_family_id 丢失 = 直接绑定（本机无数据，提示无意义）

## 10. 与既有 v3 身份解耦方案的关系

v3 本地侧设计 ~80% 复用（身份 API 拆分、Mapper 双向改写、废弃迁移机制、ANDROID_ID 派生、rebind 锁）；云端侧归属目标从 User 改为 Family。**v3 文档作废，以本文为准。**

## 11. 实施阶段

| 阶段 | 内容 | 验证点 |
|---|---|---|
| 0 | 枚举 AppState.UserId / CloudUserId 全部消费点，产出 L/C/F/D 分类清单 | 清单覆盖 ViewModel/Repository/Cache/AI/JoinRequest/通知/日志/DTO |
| 1A | 云端：Family/FamilyMember 实体 + 业务表 FamilyId 加列 + 存量迁移 + 权限查询改 Family | API 回归通过；客户端未切换 |
| 1B | 同步协议：Push/Pull Mapper 改写 + cross-family skip + SyncBatchResponse 扩展 | 双端协议测试 |
| 1C | 本地：身份 API 拆分 + 消费点改造 + 废弃 MigrateUserId/补偿 + 一次性 fixup（存量 user_id → L） | 老 GUID 不变；fixup 幂等 |
| 2 | ANDROID_ID 派生 + Application.OnCreate Provider + rebind 状态机/锁/清 synced_at + 换账号清个人表 | 换绑/换账号/回原家庭链路 |
| 3 | 成员邀请 UI 化（复用 join request 语义）+ Role 权限控制（Readonly 拦截写） | 权限矩阵测试 |
| 4（远期） | 多家庭切换 UI、分享链接、家庭配额 | — |

### 实施进度（2026-08-22 更新）

| 阶段 | 状态 | 落地内容 |
|---|---|---|
| 0 | ✅ 完成 | 消费点清单见附录 A |
| 1A | ✅ 完成 | Family/FamilyMember 实体 + AddFamilyModel 迁移（存量按成员集合分组建 Family，无权限扩大）+ FamilyService + BabyAccessService 家过滤 + 业务创建路径写 FamilyId + AuthResponse 携带 currentFamilyId/families |
| 1B | ✅ 完成 | 服务端：SyncService 按 Family 作用域 Pull + cross-family terminal skip + SkippedForeignIds 响应；客户端：sync_config.current_family_id（schema v6→v7）+ Push Mapper 身份注入（UserId=CloudUserId 归因/FamilyId 路由）+ MarkSynced 计入 skippedForeign（防无限重推）；SyncFlowTests 12/12 通过（含 terminal skip / 成员可见性 / UserId 归因语义用例） |
| 1C | ✅ 完成 | AppState 拆分为 GetLocalDataSpaceId/GetCloudUserId/GetCurrentFamilyId/GetDeviceId（删除 UserId 混合语义）；消费点按 L/C 分类落地（表单 VM×10、Baby/Record/Ai×10 处 → L；Points/InAppMessage×14 处 → C??L）；Pull Mapper 改写（家庭表→L、个人表→C）；RunIdentityFixup 一次性事务（家庭表归 L + 个人表按登录态归位 + baby_member 成员名单不动 + 标志/last_bound_family_id 同事务）；AdoptPersonalDataOnLogin（6.5 换账号清遗留行 + L→C 归并）；废弃 MigrateUserId 双向迁移与 v6 last_cloud_user_id 反迁移补偿（fixup 内清空该字段防回滚）；schema v7→v8（last_bound_family_id + identity_fixup_done）；DbSchemaUpgradeTests 8/8 通过（含 fixup 已登录/未登录两条链路） |
| 2 | ✅ 完成 | ANDROID_ID 派生：IDeviceIdentityProvider + DeviceIdentityDerivation（SHA256(prefix+ANDROID_ID)，null 回退 GUID）+ Android Application.OnCreate 进程级注入 + EnsureDeviceId/EnsureLocalUserId 接入（既有值冻结不变量）；rebind 状态机（7.1）：VerifyCodeAsync 检测 last_bound_family ≠ F → pending 暂存 + NeedsRebindConfirmation 返回，== F 或空 → 静默绑定；ConfirmRebindAsync（token 写入 → SyncTrigger.Pause + ExecuteExclusiveDuringRebindAsync 复用现有 _syncLock → ExecuteRebind 单事务 → CompleteLogin）+ CancelRebind（本地零改动）；UI：LoginView DialogHost 换绑确认框（7.2 文案 zh/en）+ LoginViewModel Confirm/Cancel 命令 + 成功流程统一收尾；换绑语义按 6.4 表格（曾同步 G 的记录靠服务端 cross-family skip 终态，清 synced_at 价值在换回 G 恢复）；DbSchemaUpgradeTests 10/10（新增 ExecuteRebind + 派生确定性用例） |

一次性 fixup（1C）事务：

```text
BEGIN:
  MigrateUserId(存量非 L 的 user_id → L)     // 复用现有方法，幂等（0 行 Affected）
  last_bound_family_id = 服务端返回的 currentFamilyId（若已登录）
  identity_fixup_done = 1
COMMIT
```

- 崩溃安全：标志与数据同事务，SQLite 原子性保证可重跑
- fixup 执行完毕后 MigrateUserId 方法整体删除

## 12. 回归测试要点

身份：首装派生 / 老用户 GUID 不变 / ANDROID_ID 不可用回退 / 同家庭重登不弹框
换绑：确认 = 仅 C/F/last_bound/synced_at 变化；取消 = 零改动；曾同步数据无无限重推（skippedForeign 生效）；原家庭云端零改动；换回原家庭滞留编辑恢复
换账号：本地个人表清理、新账号积分正确 Pull、无 UNIQUE 冲突
登出（含 SoftLogout/401）：last_bound_family_id 保留、离线立即可用
升级：存量 user_id=C → fixup → L，幂等可重跑；baby_members 按集合分组迁移正确（无权限扩大）
服务端：cross-family upsert 拦截；payload FamilyId 不可信（越权测试）；最后 Owner 不可退出

## 附录 A：阶段 0 消费点审计清单（已枚举，2026-08-22）

分类规则：**L** = LocalDataSpaceId（家庭业务表本地查询）；**C** = CloudUserId（个人数据 + 云端身份）；**D** = DeviceId。

| 消费点 | 位置 | 分类 | 说明 |
|---|---|---|---|
| 表单 VM 写入（补记/辅食/成长/里程碑） | ComplementaryFormViewModel ×3、SupplementFormViewModel ×6、GrowthViewModel、MilestoneEditViewModel | L | 本地业务数据 user_id |
| 业务查询/创建 | BabyService ×3、RecordService ×5 | L | 本地查询身份 |
| AI 分析记录 | AiAnalysisService(L145)、AiNoteParseService(L521) | L | AiAnalysisRecord.UserId 本地查询字段；server 配额走 JWT（C，客户端不传） |
| 积分/签到 | PointsService ×7 | C | per-User 个人数据；未登录离线态挂 L（登录后处理见 6.5，1C 落地） |
| 站内信 | InAppMessageService ×7（`User?.Id`） | C | 个人数据；未登录 null |
| Join request 通知 | ApiSyncService(L341 myUid) | C | 云端 uid 匹配 |
| AppState.UserId 定义 | AppState.cs（`C ?? L` 混合语义） | — | 1C 拆分为四个 API 后删除 |
| 同步 Mapper | ApiSyncService L566-668（Pull）+ MapToXxxItem（Push） | 1B | Pull→L / Push→C/F，见第 6 节 |
| 日志 | ServiceProvider(L188) | 保留 | 调试用 |
| X-Device-Id | PushApiClient 等 | D | 不变 |

后端既有：`GetAccessibleBabyIdsAsync(uid)`（owner+成员模型）、milestone 创建者透传——阶段 1B 切换为 Family 过滤。
