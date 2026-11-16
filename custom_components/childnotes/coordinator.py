"""ChildNotes 数据协调器：定时拉取当日喂养记录。

异常策略：
- 网络错误 / 超时 / 非 200 / 非 JSON / 业务错误信封 → UpdateFailed，
  HA 记录 last_update_success=False，实体保留上一次成功数据并通过属性标记异常；
- 401 → ConfigEntryAuthFailed，HA 提示用户重新配置 API Key。
"""

from __future__ import annotations

import asyncio
from dataclasses import dataclass, field
from datetime import datetime, timedelta
import logging
from typing import Any

import aiohttp
from homeassistant.config_entries import ConfigEntry
from homeassistant.const import CONF_API_KEY, CONF_URL
from homeassistant.exceptions import ConfigEntryAuthFailed
from homeassistant.helpers.aiohttp_client import async_get_clientsession
from homeassistant.helpers.update_coordinator import DataUpdateCoordinator, UpdateFailed

from .const import API_STATE_SUCCESS, DEFAULT_TIMEOUT_SECONDS, DOMAIN

_LOGGER = logging.getLogger(__name__)

TYPE_LABELS = {
    "breast": "母乳亲喂",
    "bottle": "奶瓶",
    "expressed": "吸出母乳",
}


@dataclass
class FeedingRecord:
    """单条喂养记录（已解析为本地结构）。"""

    id: str
    time: datetime | None
    type: str
    type_label: str
    side: str | None
    duration_min: int | None
    amount_ml: int | None
    note: str | None

    def as_dict(self) -> dict[str, Any]:
        """转为可放进实体 attribute 的字典。"""
        return {
            "time": self.time.isoformat() if self.time else None,
            "type": self.type_label,
            "side": self.side,
            "duration_min": self.duration_min,
            "amount_ml": self.amount_ml,
            "note": self.note,
        }


@dataclass
class FeedingData:
    """一次成功拉取的完整当日喂养数据。"""

    date: str = ""
    baby_name: str | None = None
    records: list[FeedingRecord] = field(default_factory=list)
    summary: dict[str, Any] = field(default_factory=dict)
    server_time: datetime | None = None
    # 本地推算：按今日喂养间隔均值预估下一次喂养时间
    next_feed: datetime | None = None
    avg_interval_min: float | None = None

    @property
    def feed_count(self) -> int:
        return int(self.summary.get("count") or len(self.records))

    @property
    def total_ml(self) -> int:
        return int(self.summary.get("totalMl") or 0)

    def compute_next_feed(self, now: datetime) -> None:
        """根据今日相邻喂养间隔的均值估算下一次喂养时间（至少 2 条记录才有依据）。"""
        times = [r.time for r in self.records if r.time is not None]
        if len(times) < 2:
            self.next_feed = None
            self.avg_interval_min = None
            return
        gaps = [
            (b - a).total_seconds() / 60.0
            for a, b in zip(times, times[1:])
            if b > a
        ]
        gaps = [g for g in gaps if g > 0]
        if not gaps:
            self.next_feed = None
            self.avg_interval_min = None
            return
        self.avg_interval_min = round(sum(gaps) / len(gaps), 1)
        self.next_feed = times[-1] + timedelta(minutes=self.avg_interval_min)


def parse_record(raw: dict[str, Any]) -> FeedingRecord:
    """解析后端返回的单条记录，容忍个别字段异常（脏数据不阻断整体）。"""
    feed_type = str(raw.get("type") or "")
    try:
        time_val = datetime.fromisoformat(raw["time"]) if raw.get("time") else None
    except (ValueError, TypeError):
        time_val = None
    return FeedingRecord(
        id=str(raw.get("id") or ""),
        time=time_val,
        type=feed_type,
        type_label=TYPE_LABELS.get(feed_type, feed_type or "未知"),
        side=raw.get("side"),
        duration_min=_opt_int(raw.get("durationMin")),
        amount_ml=_opt_int(raw.get("amountMl")),
        note=raw.get("note"),
    )


def _opt_int(value: Any) -> int | None:
    try:
        return int(value) if value is not None else None
    except (ValueError, TypeError):
        return None


class ChildNotesDataUpdateCoordinator(DataUpdateCoordinator[FeedingData]):
    """定时从 aiji 后台拉取当日喂养记录。"""

    def __init__(
        self,
        hass,
        entry: ConfigEntry,
        update_interval: timedelta,
    ) -> None:
        super().__init__(
            hass,
            _LOGGER,
            config_entry=entry,
            name=f"{DOMAIN}_{entry.entry_id}",
            update_interval=update_interval,
        )
        self._base_url = entry.data[CONF_URL].rstrip("/")
        self._api_key = entry.data[CONF_API_KEY]

    async def _async_update_data(self) -> FeedingData:
        session = async_get_clientsession(self.hass)
        url = f"{self._base_url}/api/ha/feeding"
        headers = {"X-Api-Key": self._api_key}

        try:
            # 请求与读响应体都在超时保护内，防止服务器慢响应导致协程长期挂起
            async with asyncio.timeout(DEFAULT_TIMEOUT_SECONDS):
                resp = await session.get(url, headers=headers)
                if resp.status == 401:
                    raise ConfigEntryAuthFailed("API 密钥无效或已被禁用")
                if resp.status != 200:
                    raise UpdateFailed(f"接口返回 HTTP {resp.status}")
                try:
                    payload = await resp.json()
                except (ValueError, aiohttp.ContentTypeError) as err:
                    raise UpdateFailed("接口返回内容不是有效 JSON") from err
        except (TimeoutError, asyncio.TimeoutError) as err:
            raise UpdateFailed(f"请求超时（{DEFAULT_TIMEOUT_SECONDS}s）: {url}") from err
        except aiohttp.ClientError as err:
            raise UpdateFailed(f"网络错误: {err}") from err

        if not isinstance(payload, dict) or payload.get("state") != API_STATE_SUCCESS:
            msg = payload.get("msg") if isinstance(payload, dict) else None
            raise UpdateFailed(f"接口返回错误: {msg or payload}")

        data = self._parse_data(payload.get("data") or {})
        data.compute_next_feed(datetime.now().astimezone())
        return data

    def _parse_data(self, data: dict[str, Any]) -> FeedingData:
        raw_records = data.get("records")
        if not isinstance(raw_records, list):
            raise UpdateFailed("接口返回数据格式异常：records 不是列表")

        records: list[FeedingRecord] = []
        for raw in raw_records:
            if isinstance(raw, dict):
                records.append(parse_record(raw))

        try:
            server_time = (
                datetime.fromisoformat(data["serverTime"]) if data.get("serverTime") else None
            )
        except (ValueError, TypeError):
            server_time = None

        summary = data.get("summary") if isinstance(data.get("summary"), dict) else {}
        return FeedingData(
            date=str(data.get("date") or ""),
            baby_name=data.get("babyName"),
            records=records,
            summary=summary,
            server_time=server_time,
        )
