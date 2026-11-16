"""ChildNotes 喂养记录传感器实体。

实体清单（均挂到同一设备 "ChildNotes 喂养" 下）：
- 今日喂养次数     sensor.*_feed_count
- 今日喂养总量     sensor.*_feed_amount   （mL）
- 最近喂养时间     sensor.*_last_feed     （timestamp）
- 预计下次喂养     sensor.*_next_feed     （timestamp，按今日平均间隔估算）
- 喂养记录明细     sensor.*_feed_records  （完整当日记录在 attributes）

同步异常策略：接口临时不可用时保留上一次成功数据（覆盖 available=True），
并通过 last_sync_success / last_error / last_sync_time 属性标记异常。
"""

from __future__ import annotations

from datetime import datetime
from typing import Any

from homeassistant.components.sensor import (
    SensorDeviceClass,
    SensorEntity,
    SensorStateClass,
)
from homeassistant.config_entries import ConfigEntry
from homeassistant.core import HomeAssistant
from homeassistant.helpers.device_registry import DeviceInfo
from homeassistant.helpers.entity_platform import AddEntitiesCallback
from homeassistant.helpers.update_coordinator import CoordinatorEntity

from .const import DOMAIN
from .coordinator import ChildNotesDataUpdateCoordinator, FeedingData


async def async_setup_entry(
    hass: HomeAssistant,
    entry: ConfigEntry,
    async_add_entities: AddEntitiesCallback,
) -> None:
    """根据配置条目创建全部传感器。"""
    coordinator: ChildNotesDataUpdateCoordinator = hass.data[DOMAIN][entry.entry_id]
    async_add_entities(
        [
            FeedCountSensor(coordinator, entry),
            FeedAmountSensor(coordinator, entry),
            LastFeedSensor(coordinator, entry),
            NextFeedSensor(coordinator, entry),
            FeedRecordsSensor(coordinator, entry),
        ]
    )


class ChildNotesSensorBase(CoordinatorEntity[ChildNotesDataUpdateCoordinator], SensorEntity):
    """公共基类：设备归属、唯一 ID、同步状态属性。

    刻意覆盖 available 恒为 True：接口临时不可用时保留最后一次成功数据继续展示，
    异常状态通过 last_sync_success / last_error 属性暴露，而不是让实体整体变不可用。
    """

    _attr_has_entity_name = True
    _attr_should_poll = False

    def __init__(
        self, coordinator: ChildNotesDataUpdateCoordinator, entry: ConfigEntry, key: str
    ) -> None:
        super().__init__(coordinator)
        self._entry = entry
        self._attr_unique_id = f"{entry.entry_id}_{key}"
        self._attr_device_info = DeviceInfo(
            identifiers={(DOMAIN, entry.entry_id)},
            name="ChildNotes 喂养",
            manufacturer="ChildNotes",
            model="aiji 后台喂养数据",
        )

    @property
    def available(self) -> bool:
        """恒可用：拉取失败时保留旧值，异常见 extra_state_attributes。"""
        return True

    @property
    def _data(self) -> FeedingData | None:
        return self.coordinator.data

    @property
    def extra_state_attributes(self) -> dict[str, Any]:
        data = self._data
        return {
            "date": data.date if data else None,
            "baby": data.baby_name if data else None,
            "last_sync_success": self.coordinator.last_update_success,
            "last_sync_time": (
                datetime.now().astimezone().isoformat(timespec="seconds")
            ),
            "last_error": None if self.coordinator.last_update_success
            else "最近一次同步失败，显示的是上一次成功数据",
        }


class FeedCountSensor(ChildNotesSensorBase):
    """今日喂养次数。"""

    _attr_translation_key = "feed_count"
    _attr_icon = "mdi:baby-bottle-outline"
    _attr_state_class = SensorStateClass.TOTAL

    @property
    def native_value(self) -> int | None:
        return self._data.feed_count if self._data else None


class FeedAmountSensor(ChildNotesSensorBase):
    """今日喂养总量（mL）。"""

    _attr_translation_key = "feed_amount"
    _attr_icon = "mdi:water"
    _attr_native_unit_of_measurement = "mL"
    _attr_state_class = SensorStateClass.TOTAL

    @property
    def native_value(self) -> int | None:
        return self._data.total_ml if self._data else None


class LastFeedSensor(ChildNotesSensorBase):
    """最近一次喂养时间。"""

    _attr_translation_key = "last_feed"
    _attr_device_class = SensorDeviceClass.TIMESTAMP

    @property
    def native_value(self) -> datetime | None:
        if not self._data or not self._data.records:
            return None
        return self._data.records[-1].time

    @property
    def extra_state_attributes(self) -> dict[str, Any]:
        attrs = super().extra_state_attributes
        if self._data and self._data.records:
            last = self._data.records[-1]
            attrs.update(
                {
                    "type": last.type_label,
                    "side": last.side,
                    "amount_ml": last.amount_ml,
                    "duration_min": last.duration_min,
                    "note": last.note,
                }
            )
        return attrs


class NextFeedSensor(ChildNotesSensorBase):
    """预计下次喂养时间（按今日平均喂养间隔估算）。"""

    _attr_translation_key = "next_feed"
    _attr_device_class = SensorDeviceClass.TIMESTAMP
    _attr_icon = "mdi:clock-outline"

    @property
    def native_value(self) -> datetime | None:
        return self._data.next_feed if self._data else None

    @property
    def extra_state_attributes(self) -> dict[str, Any]:
        attrs = super().extra_state_attributes
        if self._data:
            attrs["avg_interval_min"] = self._data.avg_interval_min
            if self._data.next_feed is not None:
                attrs["estimate_basis"] = "今日相邻喂养间隔均值 + 最近一次喂养时间"
        return attrs


class FeedRecordsSensor(ChildNotesSensorBase):
    """当日喂养记录明细（完整记录列表放在 attributes）。"""

    _attr_translation_key = "feed_records"
    _attr_icon = "mdi:format-list-bulleted"

    @property
    def native_value(self) -> int | None:
        return len(self._data.records) if self._data else None

    @property
    def extra_state_attributes(self) -> dict[str, Any]:
        attrs = super().extra_state_attributes
        if self._data:
            attrs["records"] = [r.as_dict() for r in self._data.records]
            attrs["summary"] = self._data.summary
        return attrs
