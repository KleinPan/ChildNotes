#!/usr/bin/env bash
# ChildNotes NAS 一键更新脚本
#
# 用途：在 NAS 上执行，从 GHCR 拉取最新镜像并重启容器。
# 前提：NAS 已安装 Docker + docker compose 插件，已完成首次配置（见 README.md）。
#
# 使用：
#   sudo ./nas-update.sh            # 拉取 latest 标签
#   sudo ./nas-update.sh v0.5.4     # 拉取指定版本
#
# 退出码：
#   0  成功
#   1  参数错误 / 环境不满足
#   2  docker compose 命令失败
#   3  健康检查失败

set -euo pipefail

# ---- 配置 ----
COMPOSE_FILE="${COMPOSE_FILE:-docker-compose.yml}"
HEALTH_URL="${HEALTH_URL:-http://127.0.0.1:8080/health}"
HEALTH_TIMEOUT_SEC="${HEALTH_TIMEOUT_SEC:-60}"

# ---- 颜色 ----
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m'

log() { echo -e "${CYAN}[nas-update]${NC} $*"; }
err() { echo -e "${RED}[nas-update] ERROR:${NC} $*" >&2; }
ok()  { echo -e "${GREEN}[nas-update]${NC} $*"; }
warn(){ echo -e "${YELLOW}[nas-update] WARNING:${NC} $*"; }

# ---- 前置检查 ----
if ! command -v docker >/dev/null 2>&1; then
    err "未找到 docker 命令，请先安装 Docker"
    exit 1
fi

if [ ! -f "$COMPOSE_FILE" ]; then
    err "compose 文件不存在: $COMPOSE_FILE"
    err "请先把 docker-compose.yml 和 .env 放到当前目录"
    exit 1
fi

# ---- 版本参数 ----
TAG="${1:-latest}"
if [ -n "${1:-}" ]; then
    # 镜像 tag 使用纯版本号（去掉 v 前缀），与 docker-publish.yml 的 semver 标签一致
    TAG="${1#v}"
fi
export IMAGE_TAG="$TAG"

log "目标镜像版本: ${TAG}"
log "compose 文件: ${COMPOSE_FILE}"
log ""

# ---- 记录当前版本（用于回滚提示） ----
CURRENT_IMAGE=$(docker images --format '{{.Repository}}:{{.Tag}}' \
    | grep 'childnotes/api' | head -1 || true)
if [ -n "$CURRENT_IMAGE" ]; then
    log "当前镜像: ${CURRENT_IMAGE}"
else
    warn "未找到现有 childnotes/api 镜像（首次部署？）"
fi
log ""

# ---- 拉取镜像 ----
log "拉取最新镜像..."
if ! docker compose -f "$COMPOSE_FILE" pull; then
    err "docker compose pull 失败"
    err "可能原因：1) GHCR 私有镜像未登录（docker login ghcr.io）"
    err "           2) 镜像 tag 不存在（检查 GitHub Actions 是否构建成功）"
    exit 2
fi

# ---- 重启容器 ----
log "重启容器..."
if ! docker compose -f "$COMPOSE_FILE" up -d; then
    err "docker compose up -d 失败"
    exit 2
fi

# ---- 清理旧镜像 ----
log "清理悬挂镜像..."
docker image prune -f >/dev/null 2>&1 || warn "image prune 失败（不阻塞）"

# ---- 健康检查 ----
log "健康检查 (最长等待 ${HEALTH_TIMEOUT_SEC}s)..."
START_TS=$(date +%s)
while true; do
    ELAPSED=$(( $(date +%s) - START_TS ))
    if [ "$ELAPSED" -ge "$HEALTH_TIMEOUT_SEC" ]; then
        err "健康检查超时（${HEALTH_TIMEOUT_SEC}s）"
        err "查看日志：docker compose -f $COMPOSE_FILE logs --tail 200 api"
        exit 3
    fi

    if curl -fsS "$HEALTH_URL" >/dev/null 2>&1; then
        ok "健康检查通过 (${ELAPSED}s)"
        break
    fi
    sleep 2
done

# ---- 完成 ----
echo ""
ok "===== 部署完成 ====="
ok "镜像版本: ${TAG}"
ok "compose 文件: ${COMPOSE_FILE}"
ok "健康端点: ${HEALTH_URL}"
echo ""
if [ -n "$CURRENT_IMAGE" ]; then
    log "如需回滚，执行：IMAGE_TAG=${CURRENT_IMAGE##*:} ./nas-update.sh"
    log "（或手动编辑 .env 后重启）"
fi
