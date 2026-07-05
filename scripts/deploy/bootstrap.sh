#!/usr/bin/env bash
# ChildNotes 后端服务器首次初始化脚本
# 用法: sudo bash bootstrap.sh
# 完成: 建目录 / 建用户 / 装 PostgreSQL / 写 systemd / 准备配置占位
set -euo pipefail

if [[ $EUID -ne 0 ]]; then
  echo "请用 sudo 运行" >&2
  exit 1
fi

DEPLOY_USER="childnotes"
ROOT="/opt/childnotes"
LOG_DIR="/var/log/childnotes"
DB_NAME="child_notes"
DB_USER="childnotes"
DB_PASSWORD="childnotes123"

echo "==> 1. 安装依赖"
apt-get update -y
apt-get install -y curl jq tar postgresql postgresql-contrib || true

# 启动 PostgreSQL（若未启动）
if ! systemctl is-active --quiet postgresql; then
  systemctl start postgresql
  systemctl enable postgresql
fi

echo "==> 2. 创建系统用户 ${DEPLOY_USER}"
if ! id "${DEPLOY_USER}" >/dev/null 2>&1; then
  useradd --system --home-dir "${ROOT}" --shell /usr/sbin/nologin "${DEPLOY_USER}"
fi

echo "==> 3. 创建目录结构"
mkdir -p "${ROOT}/releases" "${ROOT}/shared" "${LOG_DIR}" "${ROOT}/uploads"
chown -R "${DEPLOY_USER}:${DEPLOY_USER}" "${ROOT}" "${LOG_DIR}"

echo "==> 4. 建数据库 ${DB_NAME} (若不存在)"
if ! sudo -u postgres psql -tAc "SELECT 1 FROM pg_roles WHERE rolname='${DB_USER}'" | grep -q 1; then
  sudo -u postgres psql <<SQL
CREATE USER ${DB_USER} WITH PASSWORD '${DB_PASSWORD}';
CREATE DATABASE ${DB_NAME} OWNER ${DB_USER};
GRANT ALL PRIVILEGES ON DATABASE ${DB_NAME} TO ${DB_USER};
\c ${DB_NAME}
GRANT ALL ON SCHEMA public TO ${DB_USER};
SQL
  echo "   数据库创建完成"
else
  echo "   用户 ${DB_USER} 已存在,跳过"
fi

echo "==> 5. 准备生产配置"
PROD_CFG="${ROOT}/shared/appsettings.Production.json"
if [[ ! -f "${PROD_CFG}" ]]; then
  JWT_SECRET="$(openssl rand -base64 48)"
  ADMIN_PASSWORD="$(openssl rand -base64 18 | tr -d '/+=' | head -c 16)"
  cat > "${PROD_CFG}" <<JSONEOF
{
  "ConnectionStrings": {
    "Default": "Host=127.0.0.1;Port=5432;Database=${DB_NAME};Username=${DB_USER};Password=${DB_PASSWORD};"
  },
  "Jwt": {
    "Secret": "${JWT_SECRET}",
    "ExpireDays": 30
  },
  "DeepSeek": {
    "BaseUrl": "https://api.deepseek.com",
    "ApiKey": "",
    "Model": "deepseek-chat"
  },
  "RateLimit": {
    "Enabled": true,
    "MaxRequestsPerSecond": 5,
    "TrustProxyHeaders": true
  },
  "Upload": {
    "LocalRoot": "/opt/childnotes/uploads",
    "LocalBaseUrl": "/uploads",
    "MaxFileSizeBytes": 20971520
  },
  "Admin": {
    "InitUsername": "admin",
    "InitPassword": "${ADMIN_PASSWORD}",
    "InitDisplayName": "Administrator",
    "TokenExpireHours": 12
  }
}
JSONEOF
  chmod 600 "${PROD_CFG}"
  chown "${DEPLOY_USER}:${DEPLOY_USER}" "${PROD_CFG}"
  echo "   已创建 ${PROD_CFG}"
  echo ""
  echo "   ================================================"
  echo "   生成的密码 (请保存):"
  echo "     Admin 初始密码: ${ADMIN_PASSWORD}"
  echo "     JWT Secret: ${JWT_SECRET}"
  echo "     数据库密码: ${DB_PASSWORD}"
  echo "   配置文件: ${PROD_CFG}"
  echo "   ================================================"
else
  echo "   已存在,跳过"
fi

echo "==> 6. 准备 deploy 配置占位"
DEPLOY_ENV="/etc/childnotes/deploy.env"
mkdir -p /etc/childnotes
if [[ ! -f "${DEPLOY_ENV}" ]]; then
  cat > "${DEPLOY_ENV}" <<'EOF'
# GitHub Personal Access Token (fine-grained, 给目标仓库 Contents: Read 权限即可)
# 生成: https://github.com/settings/tokens?type=beta
GH_TOKEN=ghp_xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx

# 仓库 owner/repo
REPO=KleinPan/ChildNotes

# 保留几个历史版本（用于回滚）
KEEP_RELEASES=3

# 健康检查 URL（部署后会 curl 这个地址，非 2xx/3xx 就自动回滚）
HEALTH_URL=http://127.0.0.1:8080/api/auth/me
EOF
  chmod 600 "${DEPLOY_ENV}"
  echo "   已创建 ${DEPLOY_ENV}，请填入真实 GH_TOKEN"
fi

echo "==> 7. 安装 systemd unit"
cat > /etc/systemd/system/childnotes-api.service <<EOF
[Unit]
Description=ChildNotes API (.NET 10)
Documentation=https://github.com/KleinPan/ChildNotes
After=network-online.target postgresql.service
Wants=network-online.target

[Service]
Type=simple
User=${DEPLOY_USER}
Group=${DEPLOY_USER}
WorkingDirectory=${ROOT}/current
ExecStart=${ROOT}/current/ChildNotes.Api
Restart=always
RestartSec=5
KillSignal=SIGINT
SyslogIdentifier=childnotes-api
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://127.0.0.1:8080
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false
Environment=DOTNET_CLI_TELEMETRY_OPTOUT=1
Environment=DOTNET_NOLOGO=1
NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=full
ProtectHome=true
ReadWritePaths=${ROOT} /var/log/childnotes

[Install]
WantedBy=multi-user.target
EOF
systemctl daemon-reload
systemctl enable childnotes-api.service

echo "==> 8. 安装 deploy.sh"
install -m 755 "$(dirname "$0")/deploy.sh" /usr/local/bin/childnotes-deploy

echo ""
echo "================================================================"
echo " 初始化完成!下一步:"
echo "   1) 编辑 ${DEPLOY_ENV} (填 GH_TOKEN)"
echo "   2) 编辑 ${PROD_CFG} (确认数据库密码/JWT/Admin 密码)"
echo "   3) 首次部署:   sudo childnotes-deploy --first"
echo "      指定版本:   sudo childnotes-deploy v0.5.4 --first"
echo "   4) 看日志:     journalctl -u childnotes-api -f"
echo ""
echo "   配置 Caddy 反代 (如已有 Caddy,在 Caddyfile 末尾加):"
echo "     childnotes.hacloud.asia {"
echo "         reverse_proxy 127.0.0.1:8080"
echo "     }"
echo "   然后: sudo systemctl reload caddy"
echo "================================================================"
