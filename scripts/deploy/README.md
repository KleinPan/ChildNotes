# ChildNotes 后端部署方案

基于 GitHub Release self-contained 产物部署,**目标服务器无需装 .NET SDK/Runtime**,无需 clone 源码仓库。

## 部署架构概览

```
┌─────────────────────────────────────────────────────────────┐
│  开发机 (Windows)                                            │
│  └─ 推 tag → GitHub Actions 自动构建                          │
│       ├─ 产物: ChildNotes-backend-vX.X.X.tar.gz (自包含)     │
│       └─ 发布到 GitHub Release                               │
│                                                              │
│  部署机 (Ubuntu 云服务器)                                    │
│  ├─ /opt/childnotes/                                         │
│  │   ├─ current -> releases/v0.5.4/  (软链,切换版本)         │
│  │   ├─ releases/                     (版本化目录)           │
│  │   │   ├─ v0.5.4/                                           │
│  │   │   │   ├─ ChildNotes.Api        (可执行文件)           │
│  │   │   │   ├─ appsettings.json      (默认配置)             │
│  │   │   │   ├─ appsettings.Production.json -> ../../shared/ │
│  │   │   │   ├─ uploads -> ../../uploads/                    │
│  │   │   │   ├─ logs -> /var/log/childnotes/                 │
│  │   │   │   ├─ version.txt          (版本号)                │
│  │   │   │   └─ *.dll / *.so / createdump (运行时+依赖)      │
│  │   │   └─ v0.5.5/                                           │
│  │   ├─ shared/                       (跨版本共享)           │
│  │   │   └─ appsettings.Production.json (生产配置,含密码)    │
│  │   └─ uploads/                      (用户上传文件)         │
│  │                                                           │
│  ├─ /etc/childnotes/deploy.env       (GH_TOKEN 等部署配置)   │
│  ├─ /etc/systemd/system/childnotes-api.service               │
│  ├─ /usr/local/bin/childnotes-deploy (deploy.sh 安装位置)    │
│  ├─ /var/log/childnotes/             (应用日志)              │
│  │                                                           │
│  ├─ PostgreSQL (共用实例,5432)                               │
│  │   └─ 数据库: child_notes (独立于 hacloud)                 │
│  │                                                           │
│  └─ Caddy (反代,共用实例)                                    │
│      └─ childnotes.hacloud.asia → 127.0.0.1:8080            │
└─────────────────────────────────────────────────────────────┘
```

## 文件类型与存放路径

| 文件类型 | 来源 | 存放位置 | 说明 |
|---|---|---|---|
| **可执行程序** | Release tar.gz | `/opt/childnotes/releases/vX.X.X/ChildNotes.Api` | self-contained,含运行时 |
| **依赖 DLL** | Release tar.gz | `/opt/childnotes/releases/vX.X.X/*.dll` | 随版本更新 |
| **默认配置** | Release tar.gz | `/opt/childnotes/releases/vX.X.X/appsettings.json` | 不含敏感信息,随版本更新 |
| **生产配置** | bootstrap 生成 | `/opt/childnotes/shared/appsettings.Production.json` | **跨版本共享**,含密码/JWT,不随版本更新 |
| **上传文件** | 运行时产生 | `/opt/childnotes/uploads/` | 跨版本共享,通过软链挂入 |
| **日志** | 运行时产生 | `/var/log/childnotes/` | 跨版本共享,通过软链挂入 |
| **版本号** | Release tar.gz | `/opt/childnotes/releases/vX.X.X/version.txt` | CI 构建时写入 |
| **systemd 单元** | bootstrap 安装 | `/etc/systemd/system/childnotes-api.service` | 一次性安装 |
| **部署脚本** | bootstrap 安装 | `/usr/local/bin/childnotes-deploy` | deploy.sh 的安装位置 |
| **部署配置** | bootstrap 生成 | `/etc/childnotes/deploy.env` | GH_TOKEN 等部署参数 |
| **PostgreSQL 数据** | 运行时产生 | `/var/lib/postgresql/` | 由系统包管理 |

## 与 HA_Cloud 共存

同一台服务器完全隔离,互不影响:

| 资源 | HA_Cloud | ChildNotes |
|---|---|---|
| 系统用户 | `hacloud` | `childnotes` |
| 安装目录 | `/opt/hacloud/` | `/opt/childnotes/` |
| 数据库 | `hacloud` | `child_notes` |
| 数据库用户 | `hacloud` | `childnotes` |
| 监听端口 | 127.0.0.1:8888 | 127.0.0.1:8080 |
| systemd 服务 | `hacloud.service` | `childnotes-api.service` |
| 域名 | `hacloud.asia` | `childnotes.hacloud.asia` |
| 部署配置 | `/etc/hacloud/deploy.env` | `/etc/childnotes/deploy.env` |
| 日志目录 | `/var/log/hacloud/` | `/var/log/childnotes/` |
| 部署命令 | `hacloud-deploy` | `childnotes-deploy` |

PostgreSQL 实例共用(5432),数据库独立;Caddy 实例共用,各走各的子域名。

## 文件清单

| 文件 | 作用 |
|---|---|
| `bootstrap.sh` | 服务器首次初始化:建用户/目录/数据库/systemd/生成生产配置 |
| `deploy.sh` | 从 GitHub Release 拉取 self-contained 产物部署,支持回滚 |
| `childnotes-api.service` | systemd 单元文件 |

## 完整部署流程

### 步骤 1: GitHub 配置(只做一次)

1. **仓库 Visibility**:确认 `KleinPan/ChildNotes` 是 Public
2. **生成 PAT**:访问 https://github.com/settings/tokens?type=beta
   - 选 Fine-grained token
   - Repository access: Only select repositories → `KleinPan/ChildNotes`
   - Permissions: Contents = Read-only
   - 复制 token(形如 `github_pat_xxx...`)

### 步骤 2: DNS 解析(只做一次)

到 DNS 服务商加 A 记录:
- 主机记录: `childnotes`
- 记录类型: A
- 记录值: 云服务器公网 IP
- TTL: 600

验证生效:
```bash
dig childnotes.hacloud.asia +short
# 或
nslookup childnotes.hacloud.asia
```

### 步骤 3: 上传部署脚本到云服务器

**只能用 scp 上传**(私有仓库不能匿名 wget raw 文件)。

在本地 Windows PowerShell:
```powershell
# 进入本地仓库的部署脚本目录
cd E:\0_Code\5_Git\AiJi\scripts\deploy

# 先在云服务器上建目录 (SSH 进去执行 mkdir -p ~/childnotes-deploy)
# 或直接 scp -r 会自动建

# 上传 3 个文件到云服务器的 ~/childnotes-deploy/
scp bootstrap.sh deploy.sh childnotes-api.service 用户名@云服务器IP:~/childnotes-deploy/
```

如果云服务器上没有 `~/childnotes-deploy` 目录,先 SSH 进去建:
```bash
mkdir -p ~/childnotes-deploy
```

### 步骤 4: 跑初始化(只做一次)

SSH 进云服务器:
```bash
cd ~/childnotes-deploy
sudo bash bootstrap.sh
```

**脚本会**:
1. 装 curl/jq/tar/postgresql(已装跳过)
2. 建系统用户 `childnotes`
3. 建目录 `/opt/childnotes/{releases,shared,uploads}` + `/var/log/childnotes`
4. 在 PostgreSQL 建 `child_notes` 数据库 + `childnotes` 用户
5. 生成 `/opt/childnotes/shared/appsettings.Production.json`(随机 JWT + Admin 密码)
6. 写 `/etc/childnotes/deploy.env`(占位 GH_TOKEN)
7. 装 systemd 单元 `/etc/systemd/system/childnotes-api.service`
8. 装 `deploy.sh` 到 `/usr/local/bin/childnotes-deploy`

**完成后会输出**:
```
================================================
生成的密码 (请立即保存!)
================================================
Admin 初始密码: xxxxxxxxxxxxxxxx
JWT Secret:     xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
数据库密码:     childnotes123  (默认,可改 /opt/childnotes/shared/appsettings.Production.json)
配置文件:       /opt/childnotes/shared/appsettings.Production.json
================================================
```

**务必保存这段信息**,尤其是 Admin 密码。

### 步骤 5: 填 GitHub Token

```bash
sudo nano /etc/childnotes/deploy.env
```

把 `GH_TOKEN=ghp_xxx` 改成你步骤 1 生成的 PAT:
```env
GH_TOKEN=github_pat_xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
REPO=KleinPan/ChildNotes
KEEP_RELEASES=3
HEALTH_URL=http://127.0.0.1:8080/api/auth/me
```

保存退出(Ctrl+O, Enter, Ctrl+X)。

### 步骤 6: 部署后端

```bash
# 部署指定版本 (首次用 --first,健康检查失败不回滚)
sudo childnotes-deploy v0.5.4 --first

# 或拉最新 release
sudo childnotes-deploy --first
```

**脚本会**:
1. 用 GH_TOKEN 调 GitHub API 查 release
2. 下载 `ChildNotes-backend-v0.5.4.tar.gz`
3. 解压到 `/opt/childnotes/releases/v0.5.4/`
4. 软链 `appsettings.Production.json`、`uploads`、`logs` 进去
5. 切换 `/opt/childnotes/current` 软链
6. 重启 systemd 服务
7. 健康检查 `http://127.0.0.1:8080/api/auth/me`(401 也算正常)
8. 清理旧版本(保留最近 3 个)

成功输出:
```
[deploy] ✅ 部署完成: v0.5.4
[deploy]    版本号:   0.5.4
[deploy]    日志:     journalctl -u childnotes-api -f
[deploy]    健康检查: curl http://127.0.0.1:8080/api/auth/me
[deploy]    外网访问: https://childnotes.hacloud.asia/api/auth/me
```

### 步骤 7: 配置 Caddy 反代

在 `/etc/caddy/Caddyfile` **末尾追加**(不删 HA_Cloud 的配置):
```
childnotes.hacloud.asia {
    reverse_proxy 127.0.0.1:8080
}
```

```bash
sudo nano /etc/caddy/Caddyfile      # 编辑
sudo systemctl reload caddy          # 重载
sudo systemctl status caddy          # 确认无错
```

Caddy 自动申请 HTTPS 证书。

### 步骤 8: 验证

```bash
# 本机直连(不走 Caddy)
curl http://127.0.0.1:8080/api/auth/me
# 应返回: {"code":401,"message":"未登录","data":null}

# 通过 Caddy + HTTPS
curl https://childnotes.hacloud.asia/api/auth/me
# 同样返回 401 JSON = 成功

# 看版本号
cat /opt/childnotes/current/version.txt
# 应输出: 0.5.4
```

### 步骤 9: App 配置同步

手机 App → 我的 → 同步设置:
- 服务器地址: `https://childnotes.hacloud.asia`
- 启用云同步
- 注册新账号
- 立即同步

## 日常维护命令

```bash
# 看服务状态
sudo systemctl status childnotes-api

# 看实时日志
sudo journalctl -u childnotes-api -f

# 看最近 200 行日志
sudo journalctl -u childnotes-api -n 200 --no-pager

# 重启服务
sudo systemctl restart childnotes-api

# 升级到新版本
sudo childnotes-deploy v0.5.5

# 拉最新 release
sudo childnotes-deploy

# 回滚到上一个版本
sudo childnotes-deploy rollback

# 强制重装当前版本
sudo childnotes-deploy v0.5.4 --force

# 看当前版本
cat /opt/childnotes/current/version.txt

# 看历史版本
ls -1t /opt/childnotes/releases/

# 看生产配置
sudo cat /opt/childnotes/shared/appsettings.Production.json
```

## 升级流程(可重复)

1. 开发机推新 tag → GitHub Actions 自动构建并发布 Release
2. 云服务器执行 `sudo childnotes-deploy vX.X.X`
3. 健康检查通过 → 完成
4. 健康检查失败 → 自动回滚到上一版本

**升级不会丢失数据**:数据库、uploads、shared 配置都跨版本保留。

## 故障排查

### 健康检查失败

```bash
# 1. 看服务日志(最关键)
sudo journalctl -u childnotes-api -n 200 --no-pager

# 2. 手动测试
curl -v http://127.0.0.1:8080/api/auth/me

# 3. 看服务状态
sudo systemctl status childnotes-api

# 4. 确认配置文件存在
ls -la /opt/childnotes/current/appsettings.Production.json
ls -la /opt/childnotes/shared/appsettings.Production.json
```

### 数据库连不上

```bash
# 测试 PostgreSQL 连接
PGPASSWORD=childnotes123 psql -U childnotes -h 127.0.0.1 -d child_notes -c "\q"

# 看配置里的连接串
sudo cat /opt/childnotes/shared/appsettings.Production.json | jq .ConnectionStrings

# 看 PostgreSQL 状态
sudo systemctl status postgresql
```

### GitHub Release 拉不下来

```bash
# 测试 token 是否有效
source /etc/childnotes/deploy.env
curl -H "Authorization: Bearer ${GH_TOKEN}" \
     -H "X-GitHub-Api-Version: 2022-11-28" \
     https://api.github.com/repos/KleinPan/ChildNotes/releases/latest | jq .tag_name

# 看 deploy.env 配置
sudo cat /etc/childnotes/deploy.env
```

### Caddy 反代不工作

```bash
# 看 Caddy 状态
sudo systemctl status caddy

# 看 Caddy 日志
sudo journalctl -u caddy -n 50 --no-pager

# 测试 Caddyfile 语法
sudo caddy validate --config /etc/caddy/Caddyfile

# 看 Caddy 是否监听 443
sudo ss -tlnp | grep ':443'
```

### DNS 没生效

```bash
# 查 DNS 解析
dig childnotes.hacloud.asia +short
# 应返回云服务器公网 IP

# 如未生效,等 DNS TTL 过期(默认 600 秒)
# 或换 DNS 服务器测试: dig @8.8.8.8 childnotes.hacloud.asia +short
```

## 备份方案

### 数据库备份

```bash
# 手动备份
pg_dump -U childnotes -h 127.0.0.1 child_notes | gzip > ~/child_notes-$(date +%Y%m%d).sql.gz

# 用 cron 自动备份(每天 3 点)
sudo crontab -e
# 加一行:
0 3 * * * pg_dump -U childnotes -h 127.0.0.1 child_notes | gzip > /var/backups/child_notes-$(date +\%Y\%m\%d).sql.gz
```

### 上传文件备份

```bash
# 备份 uploads 目录
tar -czf ~/childnotes-uploads-$(date +%Y%m%d).tar.gz /opt/childnotes/uploads/
```

### 配置备份

```bash
# 备份生产配置 + 部署配置
sudo cp /opt/childnotes/shared/appsettings.Production.json ~/childnotes-prod-config.json
sudo cp /etc/childnotes/deploy.env ~/childnotes-deploy.env
```

**这些备份文件请妥善保管,包含 JWT Secret 和 Admin 密码!**
