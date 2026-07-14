# 数据库定时备份方案

> 本文档描述 ChildNotes 项目数据库从云服务器定时备份到本地电脑的完整方案。
> 配置日期：2026-07-14
> 服务器：47.83.255.132（PostgreSQL 14.23，数据库名 `child_notes`）

## 一、背景与架构

### 数据架构

项目采用"本地优先 + 服务器同步"模式：

```
┌──────────────────┐         ┌──────────────────┐
│  App 本地 SQLite  │ ←────→ │  服务器 PostgreSQL │
│  (权威数据源)      │  Pull/Push │  (同步副本)       │
└──────────────────┘         └──────────────────┘
```

- **App 本地 SQLite**：权威数据源，App 完全离线可用
- **服务器 PostgreSQL**：同步副本，存储用户账号、积分、会员订单等服务端权威数据
- **同步范围**：`baby` + `child_record` + `milestone` + `sign_in`（按 `updated_at` 增量）
- **积分/会员**：Pull-only（服务端为准，App 不上送）

同步实现见 [ApiSyncService.cs](../../ChildNotes/ChildNotes/Services/ApiSyncService.cs)，服务器地址管理见 [ServerEndpoints.cs](../../ChildNotes/ChildNotes/Services/ServerEndpoints.cs)。

### 备份架构

```
[云服务器 PostgreSQL] ──pg_dump──> [/backup/ 服务器本地中转]
                                          │
                                          │ SCP（SSH 密钥免密）
                                          ↓
[本地电脑 E:\Backup\childnotes\daily\<日期>\]
```

| 层级 | 位置 | 保留期 | 频率 |
|---|---|---|---|
| 服务器本地 | `/backup/` | 14 天 | 由本地拉取脚本触发 |
| 本地电脑 | `E:\Backup\childnotes\daily\` | 30 天 | 每天 03:30 自动 |

### 为什么要备份

即使 App 本地有 SQLite 数据，以下场景仍需服务器备份：

1. **服务器换新**：恢复 dump 可保留用户账号、积分、会员订单、签到历史
2. **用户换设备/重装 App**：本地 SQLite 丢失，需从服务器 Pull 恢复
3. **积分/会员数据**：服务端权威，App 不上送，只能靠 dump 恢复
4. **用户身份匹配**：恢复 dump 后老账号能登录，避免 user_id 不匹配导致 Push 被跳过

## 二、服务器端配置

### 备份脚本

**路径**：`/usr/local/bin/backup_childnotes.sh`

**功能**：
- 生成 `pg_dump` custom 格式（恢复用，压缩）
- 生成 `pg_dump` SQL 文本格式（跨版本兼容）
- 打包 `/opt/childnotes/uploads` 目录
- 自动清理 14 天前的旧备份
- 日志写入 `/backup/backup.log`

**手动执行**：
```bash
sudo /usr/local/bin/backup_childnotes.sh
```

**脚本内容要点**：
```bash
#!/bin/bash
set -euo pipefail
BACKUP_DIR=/backup
DB_NAME=child_notes
RETAIN_DAYS=14

# 1. custom 格式（恢复用）
sudo -u postgres pg_dump -Fc -d "$DB_NAME" --no-owner --no-privileges \
    > "$BACKUP_DIR/${DB_NAME}_${TS}.custom"

# 2. SQL 文本（跨版本兼容）
sudo -u postgres pg_dump -d "$DB_NAME" --no-owner --no-privileges --no-password \
    > "$BACKUP_DIR/${DB_NAME}_${TS}.sql"

# 3. uploads 目录打包
tar -czf "$BACKUP_DIR/uploads_${TS}.tar.gz" -C /opt/childnotes uploads

# 4. 清理旧备份
find "$BACKUP_DIR" -maxdepth 1 -type f \
    \( -name "${DB_NAME}_*.custom" -o -name "${DB_NAME}_*.sql" -o -name "uploads_*.tar.gz" \) \
    -mtime +$RETAIN_DAYS -delete
```

### 服务器端目录

```
/backup/
├── backup.log                              # 备份日志
├── child_notes_20260714_173241.custom      # pg_dump custom 格式
├── child_notes_20260714_173241.sql         # 纯 SQL 文本
├── uploads_20260714_173241.tar.gz          # 上传文件打包
└── ...                                     # 保留 14 天
```

## 三、本地配置

### SSH 密钥免密登录

为支持定时任务无人值守执行，配置了 SSH 密钥认证：

- **私钥**：`C:\Users\59902081\.ssh\childnotes_backup_id`
- **公钥**：已部署到服务器 `~/.ssh/authorized_keys`
- **算法**：ed25519

**测试免密登录**：
```powershell
ssh -i "$env:USERPROFILE\.ssh\childnotes_backup_id" `
    -o BatchMode=yes -o ConnectTimeout=10 `
    root@47.83.255.132 "echo OK"
```

### 拉取脚本

**路径**：`E:\Backup\childnotes\pull-backup.ps1`

**功能**：
1. SSH 到云服务器，调用 `/usr/local/bin/backup_childnotes.sh` 生成最新 dump
2. 分别取每类文件最新的一个（custom / sql / uploads）
3. 用 SCP 下载到本地 `E:\Backup\childnotes\daily\<日期>\`
4. 清理本地 30 天前的旧备份
5. 清理 60 天前的日志
6. 全程日志写入 `E:\Backup\childnotes\logs\pull-<日期>.log`

**手动执行**：
```powershell
powershell -ExecutionPolicy Bypass -File "E:\Backup\childnotes\pull-backup.ps1"
```

**关键配置项**（脚本开头）：
```powershell
$ServerHost = '47.83.255.132'
$ServerPort = 22
$ServerUser = 'root'
$SshKeyPath = "$env:USERPROFILE\.ssh\childnotes_backup_id"
$RemoteBackupDir = '/backup'
$RemoteScript = '/usr/local/bin/backup_childnotes.sh'

$LocalBackupRoot = 'E:\Backup\childnotes'
$LocalDailyDir = "$LocalBackupRoot\daily"
$LocalLogDir = "$LocalBackupRoot\logs"
$RetainDays = 30
```

### Windows 任务计划

**任务名**：`ChildNotesBackup`

**配置**：
- 触发器：每天 03:30
- 操作：`powershell.exe -ExecutionPolicy Bypass -NoProfile -File "E:\Backup\childnotes\pull-backup.ps1"`
- 运行账户：当前用户（Interactive 登录）
- 超时：10 分钟
- 电池供电时也运行
- 错过后补跑（StartWhenAvailable）

**管理命令**：
```powershell
# 查看任务状态
Get-ScheduledTask -TaskName "ChildNotesBackup" | Select-Object TaskName, State
Get-ScheduledTaskInfo -TaskName "ChildNotesBackup" | Format-List NextRunTime, LastRunTime, LastTaskResult

# 手动触发一次
Start-ScheduledTask -TaskName "ChildNotesBackup"

# 禁用任务
Disable-ScheduledTask -TaskName "ChildNotesBackup"

# 启用任务
Enable-ScheduledTask -TaskName "ChildNotesBackup"

# 删除任务
Unregister-ScheduledTask -TaskName "ChildNotesBackup" -Confirm:$false
```

### 本地目录结构

```
E:\Backup\childnotes\
├── pull-backup.ps1                  # 拉取脚本
├── daily\
│   ├── 2026-07-14\
│   │   ├── child_notes_20260714_175418.custom   # pg_dump custom（恢复用）
│   │   ├── child_notes_20260714_175418.sql      # 纯 SQL（跨版本兼容）
│   │   └── uploads_20260714_175418.tar.gz       # 上传文件
│   ├── 2026-07-15\
│   │   └── ...
│   └── ...                          # 保留 30 天
└── logs\
    ├── pull-2026-07-14.log          # 每日拉取日志
    └── ...                          # 保留 60 天
```

## 四、恢复方法

### 场景：服务器换新，需恢复数据

#### 步骤 1：在新服务器部署 PostgreSQL

```bash
# Ubuntu/Debian
sudo apt update && sudo apt install -y postgresql

# 启动
sudo systemctl enable --now postgresql
```

#### 步骤 2：创建数据库和用户

```bash
sudo -u postgres psql << EOF
CREATE USER childnotes WITH PASSWORD '你的密码';
CREATE DATABASE child_notes OWNER childnotes;
GRANT ALL PRIVILEGES ON DATABASE child_notes TO childnotes;
EOF
```

#### 步骤 3：上传备份文件到新服务器

```powershell
# 在本地执行，把最新备份上传到新服务器
$latestDir = Get-ChildItem "E:\Backup\childnotes\daily" -Directory | Sort-Object Name -Descending | Select-Object -First 1
scp -i "$env:USERPROFILE\.ssh\childnotes_backup_id" `
    "$($latestDir.FullName)\child_notes_*.custom" `
    "$($latestDir.FullName)\uploads_*.tar.gz" `
    root@新服务器IP:/tmp/
```

#### 步骤 4：恢复数据库

```bash
# 方式 A：用 custom 格式恢复（推荐）
sudo -u postgres pg_restore -d child_notes \
    --no-owner --no-privileges --clean --if-exists \
    /tmp/child_notes_XXXX.custom

# 方式 B：用 SQL 文本恢复（跨大版本必用）
sudo -u postgres psql -d child_notes -f /tmp/child_notes_XXXX.sql
```

#### 步骤 5：恢复上传文件

```bash
sudo mkdir -p /opt/childnotes
sudo tar -xzf /tmp/uploads_XXXX.tar.gz -C /opt/childnotes
# 按新服务器运行用户调整属主
sudo chown -R www-data:www-data /opt/childnotes/uploads
```

#### 步骤 6：部署后端应用

- 修改 `appsettings.json` 的 `ConnectionStrings:Default` 指向新 PostgreSQL
- 部署 ChildNotes.Api
- 确认 `/api/health` 可访问

#### 步骤 7：App 端切换服务器地址

- 在 App 的"数据同步"页修改 `ServerUrl` 指向新服务器
- 触发一次同步，确认 Pull/Push 正常

### 恢复后验证

- [ ] 老用户账号能登录（说明 dump 恢复成功）
- [ ] `child_record` 表数据正常（应有历史记录）
- [ ] 积分余额正确（服务端权威数据）
- [ ] 会员订单历史完整
- [ ] App 同步 Pull/Push 均成功

## 五、日常维护

### 检查备份是否正常

```powershell
# 查看最新日志
Get-Content "E:\Backup\childnotes\logs\pull-$(Get-Date -Format 'yyyy-MM-dd').log" -Tail 20

# 查看本地备份文件
Get-ChildItem "E:\Backup\childnotes\daily" -Directory | Sort-Object Name -Descending | Select-Object -First 5
Get-ChildItem "E:\Backup\childnotes\daily\$(Get-Date -Format 'yyyy-MM-dd')" -File | Select-Object Name, @{N='SizeKB';E={[math]::Round($_.Length/1KB,1)}}

# 查看任务计划下次运行时间
Get-ScheduledTaskInfo -TaskName "ChildNotesBackup" | Format-List NextRunTime, LastRunTime, LastTaskResult
```

### 日志关键字

- 成功：`==== Backup done. Local size: X MB ====`
- 失败：`ERROR:` 或 `==== Backup FAILED ====`

### 手动触发备份

```powershell
# 方式 1：通过任务计划
Start-ScheduledTask -TaskName "ChildNotesBackup"

# 方式 2：直接运行脚本
powershell -ExecutionPolicy Bypass -File "E:\Backup\childnotes\pull-backup.ps1"
```

### 修改配置

| 需求 | 修改位置 |
|---|---|
| 修改本地保留期 | `pull-backup.ps1` 中的 `$RetainDays = 30` |
| 修改服务器保留期 | `/usr/local/bin/backup_childnotes.sh` 中的 `RETAIN_DAYS=14` |
| 修改服务器地址 | `pull-backup.ps1` 中的 `$ServerHost` |
| 修改备份时间 | 任务计划触发器：`Set-ScheduledTask -TaskName "ChildNotesBackup" -Trigger (New-ScheduledTaskTrigger -Daily -At 4:00AM)` |
| 修改本地备份目录 | `pull-backup.ps1` 中的 `$LocalBackupRoot` |

## 六、故障排查

### 问题 1：SSH 连接失败

**现象**：日志出现 `ssh: connect to host 47.83.255.132 port 22: Connection timed out`

**排查**：
```powershell
# 测试网络连通性
Test-NetConnection 47.83.255.132 -Port 22

# 测试 SSH 密钥认证
ssh -i "$env:USERPROFILE\.ssh\childnotes_backup_id" -v root@47.83.255.132 "echo OK"
```

**可能原因**：
- 服务器关机 / 重启中
- 安全组未放行 22 端口
- SSH 服务未运行（服务器端 `sudo systemctl status sshd`）
- 密钥被删除（重新部署公钥到 `~/.ssh/authorized_keys`）

### 问题 2：pg_dump 失败

**现象**：日志出现 `Remote backup script failed (exit=1)`

**排查**：
```bash
# 登录服务器查看详细日志
sudo tail -30 /backup/backup.log

# 手动执行备份脚本看详细错误
sudo /usr/local/bin/backup_childnotes.sh
```

**可能原因**：
- PostgreSQL 服务未运行：`sudo systemctl status postgresql`
- 数据库不存在：`sudo -u postgres psql -l`
- 磁盘空间不足：`df -h /`

### 问题 3：SCP 下载失败

**现象**：日志出现 `scp failed for /backup/xxx (exit=1)`

**排查**：
```powershell
# 手动下载测试
scp -i "$env:USERPROFILE\.ssh\childnotes_backup_id" -P 22 `
    root@47.83.255.132:/backup/child_notes_XXXX.custom `
    E:\temp\test.custom
```

**可能原因**：
- 服务器端文件不存在（备份脚本未生成）
- 本地目录权限不足
- 磁盘空间不足

### 问题 4：任务计划未执行

**排查**：
```powershell
# 查看任务状态
Get-ScheduledTask -TaskName "ChildNotesBackup" | Select-Object TaskName, State
Get-ScheduledTaskInfo -TaskName "ChildNotesBackup" | Format-List *

# 查看任务历史（事件日志）
Get-WinEvent -FilterHashtable @{LogName='Microsoft-Windows-TaskScheduler/Operational'; ProviderName='Microsoft-Windows-TaskScheduler'; ID=200,201,202,203} -MaxEvents 10 | Format-List TimeCreated, Id, Message
```

**可能原因**：
- 电脑在 03:30 关机/休眠（设置 `StartWhenAvailable` 后会补跑）
- 任务被禁用：`Enable-ScheduledTask -TaskName "ChildNotesBackup"`
- 用户密码过期（Interactive 登录方式需要有效会话）

### 问题 5：`close - IO is still pending` 告警

**现象**：日志或控制台输出 `close - IO is still pending on closed socket`

**说明**：这是 Windows OpenSSH 客户端的已知无害告警，不影响功能，可忽略。

## 七、数据完整性说明

### 服务器换新后的数据完整性

| 数据类型 | 是否丢失 | 说明 |
|---|---|---|
| App 本地 SQLite 数据 | 不丢 | 本地是权威源 |
| 服务器历史数据（恢复 dump） | 不丢 | dump 恢复 |
| dump 时间点之后的 App 增量 | 不丢 | App 同步 Push 到新服务器 |
| 积分/会员订单（恢复 dump） | 不丢 | 服务端权威，dump 恢复 |
| 用户账号（恢复 dump） | 不丢 | 老账号可登录，user_id 匹配 |

### 不恢复 dump 的后果

| 数据类型 | 是否丢失 | 原因 |
|---|---|---|
| 用户账号 | 丢失 | 老账号不存在，需重新注册 |
| App Push 的数据 | 丢失 | 新 user_id 与本地数据对不上，Push 被跳过 |
| 积分余额 | 丢失 | Pull-only，App 不上送，从 0 开始 |
| 会员订单历史 | 丢失 | 服务端权威数据 |

**结论**：服务器换新时，**必须恢复 dump**，否则会触发 user_id 不匹配导致数据丢失。后端 `SyncService.PushAsync` 会校验 `item.UserId == 当前登录用户 id`，不匹配的数据会被静默跳过（见 [SyncService.cs](../../ChildNotes.Backend/ChildNotes.Infrastructure/Services/SyncService.cs)）。

## 八、附录

### 数据库基本信息

| 项 | 值 |
|---|---|
| 数据库类型 | PostgreSQL 14.23 |
| 数据库名 | `child_notes` |
| 数据库所有者 | `childnotes` |
| 字符编码 | UTF8 |
| 表数量 | 19 张 |
| 最大表 | `child_record`（约 125 行） |
| 数据库大小 | 约 7 MB |
| 上传文件大小 | 约 710 KB |

### 关键代码位置

| 文件 | 说明 |
|---|---|
| [appsettings.json](../../ChildNotes.Backend/ChildNotes.Api/appsettings.json) | 后端配置（连接字符串） |
| [ApiSyncService.cs](../../ChildNotes/ChildNotes/Services/ApiSyncService.cs) | 同步服务（本地优先+服务器同步） |
| [ServerEndpoints.cs](../../ChildNotes/ChildNotes/Services/ServerEndpoints.cs) | 服务器地址管理 |
| [SyncConfig.cs](../../ChildNotes/ChildNotes/Models/SyncConfig.cs) | 同步配置模型 |
| [SyncService.cs](../../ChildNotes.Backend/ChildNotes.Infrastructure/Services/SyncService.cs) | 后端同步服务实现 |

### 备份相关文件路径

| 位置 | 路径 |
|---|---|
| 服务器备份脚本 | `/usr/local/bin/backup_childnotes.sh` |
| 本地拉取脚本 | `E:\Backup\childnotes\pull-backup.ps1` |
| 本地文档 | `E:\Backup\childnotes\README.md` |
| 项目文档 | `docs/development/backup.md`（本文件） |
| SSH 私钥 | `C:\Users\59902081\.ssh\childnotes_backup_id` |
| Windows 任务计划 | `ChildNotesBackup` |
