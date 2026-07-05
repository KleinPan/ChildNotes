# ChildNotes 后端部署脚本

参考 HA_Cloud 的部署方案,使用 GitHub Release 自包含产物 + systemd + Caddy 反代。

## 文件说明

| 文件 | 用途 |
|---|---|
| `bootstrap.sh` | 服务器首次初始化:建用户/目录/数据库/systemd/配置占位 |
| `deploy.sh` | 拉取指定版本 release 并部署,支持回滚 |
| `childnotes-api.service` | systemd 单元文件(由 bootstrap.sh 安装) |

## 部署流程

### 1. DNS 解析(子域名)

到 DNS 服务商加 A 记录:
- 主机记录: `childnotes`
- 记录值: 云服务器公网 IP

### 2. 上传脚本到服务器

```bash
# 在云服务器上
mkdir -p ~/childnotes-deploy
cd ~/childnotes-deploy
# 用 scp 或 git clone 上传这 3 个文件
```

或直接 git clone 整个仓库(只需要这 3 个文件):
```bash
git clone --depth 1 https://github.com/KleinPan/ChildNotes.git
cd ChildNotes/scripts/deploy
```

### 3. 跑初始化

```bash
sudo bash bootstrap.sh
```

完成后会输出:
- 自动生成的 Admin 密码
- 自动生成的 JWT Secret
- 数据库密码
- 配置文件路径 `/opt/childnotes/shared/appsettings.Production.json`

### 4. 配置 GitHub Token

```bash
sudo nano /etc/childnotes/deploy.env
```

把 `GH_TOKEN=ghp_xxx` 改成你自己的 PAT(需要 `repo` 权限读 release 资源)。
生成: https://github.com/settings/tokens?type=beta

### 5. 部署后端

```bash
# 首次部署(健康检查失败不回滚,保留现场)
sudo childnotes-deploy v0.5.4 --first

# 或拉最新 release
sudo childnotes-deploy --first
```

脚本会:
1. 从 GitHub Release 下载 `ChildNotes-backend-v0.5.4.tar.gz`
2. 解压到 `/opt/childnotes/releases/v0.5.4/`
3. 软链 `appsettings.Production.json` 到解压目录
4. 切换 `/opt/childnotes/current` 软链
5. 重启 systemd 服务
6. 健康检查 `http://127.0.0.1:8080/api/auth/me`(401 也算正常)

### 6. 配置 Caddy 反代

编辑 Caddyfile(通常 `/etc/caddy/Caddyfile`),**末尾追加**:

```
childnotes.hacloud.asia {
    reverse_proxy 127.0.0.1:8080
}
```

重载:
```bash
sudo systemctl reload caddy
```

Caddy 自动申请 HTTPS 证书。

### 7. 验证

```bash
# 本机
curl http://127.0.0.1:8080/api/auth/me
# 返回 {"code":401,...} = 成功

# 外网
curl https://childnotes.hacloud.asia/api/auth/me
```

App 同步地址填: `https://childnotes.hacloud.asia`

## 常用命令

```bash
# 看状态
sudo systemctl status childnotes-api

# 看日志
sudo journalctl -u childnotes-api -f

# 重启
sudo systemctl restart childnotes-api

# 升级到新版本
sudo childnotes-deploy v0.5.5

# 拉最新 release
sudo childnotes-deploy

# 回滚到上一个版本
sudo childnotes-deploy rollback

# 强制重装当前版本
sudo childnotes-deploy v0.5.4 --force
```

## 目录结构

```
/opt/childnotes/
├── current -> releases/v0.5.4    # 软链,指向当前版本
├── releases/
│   ├── v0.5.4/                     # 各版本解压目录
│   └── v0.5.5/
├── shared/
│   └── appsettings.Production.json # 跨版本共享的生产配置
└── uploads/                        # 上传的文件存储

/var/log/childnotes/                # 日志目录
/etc/childnotes/deploy.env          # 部署配置(GH_TOKEN 等)
```

## 与 HA_Cloud 共存

同一台服务器上 HA_Cloud 和 ChildNotes 完全隔离:

| 资源 | HA_Cloud | ChildNotes |
|---|---|---|
| 用户 | `hacloud` | `childnotes` |
| 目录 | `/opt/hacloud/` | `/opt/childnotes/` |
| 数据库 | `hacloud` | `child_notes` |
| 监听端口 | 127.0.0.1:8888 | 127.0.0.1:8080 |
| systemd | `hacloud.service` | `childnotes-api.service` |
| 域名 | hacloud.asia | childnotes.hacloud.asia |
| 配置 | `/etc/hacloud/deploy.env` | `/etc/childnotes/deploy.env` |

PostgreSQL 实例共用(5432),数据库独立,互不影响。

## 故障排查

### 健康检查失败

```bash
# 看服务日志
sudo journalctl -u childnotes-api -n 200 --no-pager

# 看应用日志
sudo tail -n 100 /var/log/childnotes/*.log

# 手动测试
curl -v http://127.0.0.1:8080/api/auth/me
```

### 数据库连不上

```bash
# 测试连接
PGPASSWORD=childnotes123 psql -U childnotes -h 127.0.0.1 -d child_notes -c "\q"

# 看配置
sudo cat /opt/childnotes/shared/appsettings.Production.json
```

### GitHub Release 拉不下来

```bash
# 测试 token 是否有效
curl -H "Authorization: Bearer 你的token" \
     -H "X-GitHub-Api-Version: 2022-11-28" \
     https://api.github.com/repos/KleinPan/ChildNotes/releases/latest
```
