#!/usr/bin/env bash
# ChildNotes 一键部署脚本
# 从 GitHub Release 下载 self-contained 后端产物并部署,无需 clone 源码
#
# 用法:
#   sudo childnotes-deploy                  拉取最新 release
#   sudo childnotes-deploy v0.5.4           拉取指定 tag
#   sudo childnotes-deploy v0.5.4 --first   首次部署 (健康检查失败不回滚,保留现场)
#   sudo childnotes-deploy v0.5.4 --force   强制重装当前版本
#   sudo childnotes-deploy rollback         回滚到上一个 release
set -euo pipefail

# ---- 配置 ----
DEPLOY_ENV="${DEPLOY_ENV:-/etc/childnotes/deploy.env}"
if [[ ! -f "${DEPLOY_ENV}" ]]; then
  echo "缺少配置 ${DEPLOY_ENV},请先跑 bootstrap.sh" >&2
  exit 1
fi
# shellcheck disable=SC1090
source "${DEPLOY_ENV}"

: "${GH_TOKEN:?GH_TOKEN 未设置 (编辑 ${DEPLOY_ENV})}"
: "${REPO:?REPO 未设置}"
KEEP_RELEASES="${KEEP_RELEASES:-3}"
HEALTH_URL="${HEALTH_URL:-http://127.0.0.1:8080/api/auth/me}"

ROOT="/opt/childnotes"
RELEASES="${ROOT}/releases"
SHARED="${ROOT}/shared"
CURRENT="${ROOT}/current"
DEPLOY_USER="childnotes"

API="https://api.github.com/repos/${REPO}"
AUTH_HDR=(-H "Authorization: Bearer ${GH_TOKEN}" -H "X-GitHub-Api-Version: 2022-11-28")

log() { printf '\033[36m[deploy]\033[0m %s\n' "$*"; }
err() { printf '\033[31m[error]\033[0m %s\n' "$*" >&2; }

if [[ $EUID -ne 0 ]]; then
  err "请用 sudo 运行"
  exit 1
fi

# ---- 回滚分支 ----
if [[ "${1:-}" == "rollback" ]]; then
  PREV="$(ls -1t "${RELEASES}" | sed -n '2p' || true)"
  [[ -z "${PREV}" ]] && { err "没有可回滚的历史版本"; exit 1; }
  log "回滚到 ${PREV}"
  ln -sfn "${RELEASES}/${PREV}" "${CURRENT}"
  systemctl restart childnotes-api
  log "完成"
  exit 0
fi

# ---- 解析目标版本 ----
TARGET="${1:-}"
if [[ -z "${TARGET}" ]]; then
  log "查询最新 release..."
  TARGET="$(curl -fsSL "${AUTH_HDR[@]}" "${API}/releases/latest" | jq -r '.tag_name')"
  [[ "${TARGET}" == "null" || -z "${TARGET}" ]] && { err "未找到任何 release"; exit 1; }
fi
log "目标版本: ${TARGET}"

# 解析标志位 (--force / --first)
FORCE=0
FIRST=0
for arg in "${@:2}"; do
  case "${arg}" in
    --force) FORCE=1 ;;
    --first) FIRST=1 ;;
  esac
done

# 已经是当前版本就跳过（除非 --force）
if [[ -L "${CURRENT}" && "$(basename "$(readlink -f "${CURRENT}")")" == "${TARGET}" && ${FORCE} -eq 0 ]]; then
  log "已经是 ${TARGET},无需部署 (加 --force 强制重装)"
  exit 0
fi

# ---- 查 release & asset id ----
log "查询 release 资源..."
RELEASE_JSON="$(curl -fsSL "${AUTH_HDR[@]}" "${API}/releases/tags/${TARGET}")"
ASSET_NAME="ChildNotes-backend-${TARGET}.tar.gz"
ASSET_ID="$(echo "${RELEASE_JSON}" | jq -r ".assets[] | select(.name==\"${ASSET_NAME}\") | .id")"
[[ -z "${ASSET_ID}" || "${ASSET_ID}" == "null" ]] && { err "${TARGET} 中找不到 ${ASSET_NAME}"; exit 1; }

# ---- 下载到临时文件 ----
TMP="$(mktemp -d)"
trap 'rm -rf "${TMP}"' EXIT
TARBALL="${TMP}/${ASSET_NAME}"
log "下载 ${ASSET_NAME}..."
curl -fSL --progress-bar \
  -H "Authorization: Bearer ${GH_TOKEN}" \
  -H "Accept: application/octet-stream" \
  "${API}/releases/assets/${ASSET_ID}" \
  -o "${TARBALL}"

# ---- 解压到 releases/<version>/ ----
DEST="${RELEASES}/${TARGET}"
log "解压到 ${DEST}"
rm -rf "${DEST}"
mkdir -p "${DEST}"
tar -xzf "${TARBALL}" -C "${DEST}"

# ---- 软链共享配置和目录 ----
# 生产配置 (跨版本共享,bootstrap.sh 已生成)
if [[ ! -f "${SHARED}/appsettings.Production.json" ]]; then
  err "${SHARED}/appsettings.Production.json 不存在,请先跑 bootstrap.sh"
  exit 1
fi
ln -sfn "${SHARED}/appsettings.Production.json" "${DEST}/appsettings.Production.json"

# uploads 目录 (用户上传文件,跨版本共享)
ln -sfn "${ROOT}/uploads" "${DEST}/uploads"

# logs 目录
ln -sfn "/var/log/childnotes" "${DEST}/logs"

# 标记版本号 (release 包里已有 version.txt,如无则补一个)
if [[ ! -f "${DEST}/version.txt" ]]; then
  echo -n "${TARGET}" > "${DEST}/version.txt"
fi

# 设置属主
chown -R --no-dereference "${DEPLOY_USER}:${DEPLOY_USER}" "${DEST}"

# ---- 原子切换 current ----
PREVIOUS=""
if [[ -L "${CURRENT}" ]]; then
  PREVIOUS="$(readlink -f "${CURRENT}")"
fi
log "切换 current -> ${TARGET}"
ln -sfn "${DEST}" "${CURRENT}"

# ---- 重启服务 ----
log "重启 childnotes-api.service"
systemctl restart childnotes-api

# ---- 健康检查 (最多 60 秒,首次 CodeFirst 建表会拖慢) ----
log "健康检查 ${HEALTH_URL} ..."
OK=0
for i in {1..30}; do
  sleep 2
  # /api/auth/me 返回 401 也算服务正常 (未登录响应)
  CODE="$(curl -s -o /dev/null -w '%{http_code}' --max-time 3 "${HEALTH_URL}" || true)"
  if [[ "${CODE}" == "401" || "${CODE}" == "200" ]]; then
    OK=1
    break
  fi
  echo -n "."
done
echo

if [[ ${OK} -ne 1 ]]; then
  err "健康检查失败"
  if [[ ${FIRST} -eq 1 ]]; then
    err "--first 模式:保留现场不回滚"
    err "排查:"
    err "  journalctl -u childnotes-api -n 200 --no-pager"
    err "  tail -n 100 /var/log/childnotes/*.log"
    err "确认 ${SHARED}/appsettings.Production.json 中数据库/JWT/Admin 配置已填好"
  elif [[ -n "${PREVIOUS}" && "${PREVIOUS}" != "${DEST}" ]]; then
    err "自动回滚到 $(basename "${PREVIOUS}")"
    ln -sfn "${PREVIOUS}" "${CURRENT}"
    systemctl restart childnotes-api
  else
    err "无可用历史版本,无法回滚 (首次部署?用 --first 标志可跳过此回滚)"
    err "排查: journalctl -u childnotes-api -n 200 --no-pager"
  fi
  exit 1
fi

# ---- 清理老版本 ----
log "清理旧 release (保留最近 ${KEEP_RELEASES} 个)"
# shellcheck disable=SC2012
ls -1t "${RELEASES}" | tail -n +$((KEEP_RELEASES + 1)) | while read -r old; do
  log "  删除 ${old}"
  rm -rf "${RELEASES:?}/${old}"
done

log "✅ 部署完成: ${TARGET}"
log "   版本号:   $(cat ${CURRENT}/version.txt)"
log "   日志:     journalctl -u childnotes-api -f"
log "   健康检查: curl ${HEALTH_URL}"
log "   外网访问: https://childnotes.hacloud.asia/api/auth/me"
