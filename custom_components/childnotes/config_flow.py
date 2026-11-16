"""ChildNotes 配置流：在 HA 界面配置接口地址、API 密钥与拉取间隔。"""

from __future__ import annotations

import asyncio
import logging
from typing import Any

import aiohttp
import voluptuous as vol
from homeassistant.config_entries import ConfigFlow
from homeassistant.const import CONF_API_KEY, CONF_URL
from homeassistant.core import HomeAssistant
from homeassistant.exceptions import HomeAssistantError
from homeassistant.helpers.aiohttp_client import async_get_clientsession

from .const import (
    API_STATE_SUCCESS,
    CONF_SCAN_INTERVAL_MINUTES,
    DEFAULT_SCAN_INTERVAL_MINUTES,
    DEFAULT_TIMEOUT_SECONDS,
    DOMAIN,
)

_LOGGER = logging.getLogger(__name__)

STEP_USER_DATA_SCHEMA = vol.Schema(
    {
        vol.Required(CONF_URL): str,
        vol.Required(CONF_API_KEY): str,
        vol.Required(
            CONF_SCAN_INTERVAL_MINUTES, default=DEFAULT_SCAN_INTERVAL_MINUTES
        ): vol.All(vol.Coerce(int), vol.Range(min=1, max=1440)),
    }
)


def normalize_url(url: str) -> str:
    """去掉首尾空白与末尾斜杠，保证拼接路径不出错。"""
    return url.strip().rstrip("/")


async def validate_input(hass: HomeAssistant, data: dict[str, Any]) -> None:
    """调用一次 /api/ha/feeding 验证地址与 API Key 可用。失败抛对应异常。"""
    session = async_get_clientsession(hass)
    url = f"{normalize_url(data[CONF_URL])}/api/ha/feeding"

    try:
        async with asyncio.timeout(DEFAULT_TIMEOUT_SECONDS):
            resp = await session.get(
                url, headers={"X-Api-Key": data[CONF_API_KEY]}
            )
    except (TimeoutError, asyncio.TimeoutError) as err:
        raise CannotConnect("连接超时") from err
    except aiohttp.ClientError as err:
        raise CannotConnect(f"无法连接: {err}") from err

    if resp.status == 401:
        raise InvalidAuth("API 密钥无效")
    if resp.status != 200:
        raise CannotConnect(f"接口返回 HTTP {resp.status}")

    try:
        payload = await resp.json()
    except (ValueError, aiohttp.ContentTypeError) as err:
        raise CannotConnect("返回内容不是有效 JSON") from err

    if not isinstance(payload, dict) or payload.get("state") != API_STATE_SUCCESS:
        msg = payload.get("msg") if isinstance(payload, dict) else None
        raise CannotConnect(f"接口返回错误: {msg or '未知错误'}")


class ChildNotesConfigFlow(ConfigFlow, domain=DOMAIN):
    """处理 ChildNotes 集成的配置流。"""

    VERSION = 1

    async def async_step_user(self, user_input: dict[str, Any] | None = None):
        """第一步：用户输入接口地址 / API Key / 拉取间隔。"""
        errors: dict[str, str] = {}
        if user_input is not None:
            user_input[CONF_URL] = normalize_url(user_input[CONF_URL])
            # 同一后端地址只允许配置一个条目
            await self.async_set_unique_id(user_input[CONF_URL])
            self._abort_if_unique_id_configured()
            try:
                await validate_input(self.hass, user_input)
            except CannotConnect as err:
                errors["base"] = "cannot_connect"
                _LOGGER.warning("ChildNotes 连接验证失败: %s", err)
            except InvalidAuth:
                errors["base"] = "invalid_auth"
            except Exception:  # noqa: BLE001 - 配置流兜底
                _LOGGER.exception("ChildNotes 配置验证出现意外错误")
                errors["base"] = "unknown"
            else:
                return self.async_create_entry(
                    title="ChildNotes 喂养记录", data=user_input
                )

        return self.async_show_form(
            step_id="user", data_schema=STEP_USER_DATA_SCHEMA, errors=errors
        )


class CannotConnect(HomeAssistantError):
    """无法连接到后端。"""


class InvalidAuth(HomeAssistantError):
    """API 密钥无效。"""
