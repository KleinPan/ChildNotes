"""ChildNotes 喂养记录集成常量。"""

from __future__ import annotations

from datetime import timedelta

DOMAIN = "childnotes"

CONF_SCAN_INTERVAL_MINUTES = "scan_interval_minutes"
DEFAULT_SCAN_INTERVAL_MINUTES = 10
DEFAULT_TIMEOUT_SECONDS = 30
DEFAULT_NAME = "ChildNotes"

# 后端统一响应信封：{state, msg, data}，state == "000000" 表示成功
API_STATE_SUCCESS = "000000"
