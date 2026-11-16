"""ChildNotes 喂养记录集成入口。

数据流：HA（DataUpdateCoordinator 定时拉取，默认 10 分钟）
  → aiji 后台 GET /api/ha/feeding（X-Api-Key 鉴权）
  → sensor 平台生成喂养统计实体。
"""

from __future__ import annotations

from datetime import timedelta
import logging

from homeassistant.config_entries import ConfigEntry
from homeassistant.const import Platform
from homeassistant.core import HomeAssistant

from .const import CONF_SCAN_INTERVAL_MINUTES, DEFAULT_SCAN_INTERVAL_MINUTES, DOMAIN
from .coordinator import ChildNotesDataUpdateCoordinator

_LOGGER = logging.getLogger(__name__)

PLATFORMS: list[Platform] = [Platform.SENSOR]


async def async_setup_entry(hass: HomeAssistant, entry: ConfigEntry) -> bool:
    """首次加载：创建协调器并转发到 sensor 平台。"""
    hass.data.setdefault(DOMAIN, {})
    interval = timedelta(
        minutes=entry.data.get(CONF_SCAN_INTERVAL_MINUTES, DEFAULT_SCAN_INTERVAL_MINUTES)
    )
    coordinator = ChildNotesDataUpdateCoordinator(hass, entry, interval)
    # 首次刷新失败会抛 ConfigEntryNotReady / ConfigEntryAuthFailed，由 HA 负责重试/提示重新配置
    await coordinator.async_config_entry_first_refresh()
    hass.data[DOMAIN][entry.entry_id] = coordinator

    await hass.config_entries.async_forward_entry_setups(entry, PLATFORMS)
    entry.async_on_unload(entry.add_update_listener(async_reload_entry))
    return True


async def async_unload_entry(hass: HomeAssistant, entry: ConfigEntry) -> bool:
    """卸载：清理协调器并卸载平台。"""
    unloaded = await hass.config_entries.async_unload_platforms(entry, PLATFORMS)
    if unloaded:
        hass.data[DOMAIN].pop(entry.entry_id, None)
    return unloaded


async def async_reload_entry(hass: HomeAssistant, entry: ConfigEntry) -> None:
    """配置更新（如修改拉取间隔）后重载。"""
    await hass.config_entries.async_reload(entry.entry_id)
