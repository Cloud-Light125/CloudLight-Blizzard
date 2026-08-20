from __future__ import annotations

from typing import Any, TypedDict, TYPE_CHECKING

from yarl import URL

from utils import json_load, json_save
from constants import SETTINGS_PATH, DEFAULT_LANG, PriorityMode

if TYPE_CHECKING:
    from main import ParsedArgs


class SettingsFile(TypedDict):
    proxy: URL
    language: str
    dark_mode: bool
    exclude: set[str]
    priority: list[str]
    autostart_tray: bool
    connection_quality: int
    tray_notifications: bool
    enable_badges_emotes: bool
    available_drops_check: bool
    # TDM_CLAIM_TOGGLE_PATCH_BEGIN: settings-schema
    auto_claim_drops: bool
    # TDM_CLAIM_TOGGLE_PATCH_END: settings-schema
    priority_mode: PriorityMode


default_settings: SettingsFile = {
    "proxy": URL(),
    "priority": [],
    "exclude": set(),
    "dark_mode": False,
    "autostart_tray": False,
    "connection_quality": 1,
    "language": DEFAULT_LANG,
    "tray_notifications": True,
    "enable_badges_emotes": False,
    "available_drops_check": False,
    # TDM_CLAIM_TOGGLE_PATCH_BEGIN: settings-default
    "auto_claim_drops": True,
    # TDM_CLAIM_TOGGLE_PATCH_END: settings-default
    "priority_mode": PriorityMode.PRIORITY_ONLY,
}


class Settings:
    # from args
    log: bool
    tray: bool
    dump: bool
    # args properties
    debug_ws: int
    debug_gql: int
    logging_level: int
    # from settings file
    proxy: URL
    language: str
    dark_mode: bool
    exclude: set[str]
    priority: list[str]
    autostart_tray: bool
    connection_quality: int
    tray_notifications: bool
    enable_badges_emotes: bool
    available_drops_check: bool
    # TDM_CLAIM_TOGGLE_PATCH_BEGIN: settings-model
    auto_claim_drops: bool
    # TDM_CLAIM_TOGGLE_PATCH_END: settings-model
    priority_mode: PriorityMode

    PASSTHROUGH = ("_settings", "_args", "_altered")

    def __init__(self, args: ParsedArgs):
        # TDM_CLAIM_TOGGLE_PATCH_BEGIN: settings-migration
        _loaded_settings: dict[str, Any] = json_load(SETTINGS_PATH, default_settings, merge=False)
        for _setting_name, _default_value in default_settings.items():
            if (
                _setting_name not in _loaded_settings
                or type(_loaded_settings[_setting_name]) is not type(_default_value)
            ):
                _loaded_settings[_setting_name] = _default_value
        self._settings: SettingsFile = _loaded_settings  # type: ignore[assignment]
        # TDM_CLAIM_TOGGLE_PATCH_END: settings-migration
        self._args: ParsedArgs = args
        self._altered: bool = False

    # default logic of reading settings is to check args first, then the settings file
    def __getattr__(self, name: str, /) -> Any:
        if name == "proxy":
            try:
                return object.__getattribute__(self, "_cloudlight_global_proxy")
            except AttributeError:
                pass
        if name in self.PASSTHROUGH:
            # passthrough
            return getattr(super(), name)
        elif hasattr(self._args, name):
            return getattr(self._args, name)
        elif name in self._settings:
            return self._settings[name]  # type: ignore[literal-required]
        return getattr(super(), name)

    def __setattr__(self, name: str, value: Any, /) -> None:
        if name in self.PASSTHROUGH:
            # passthrough
            return super().__setattr__(name, value)
        elif name in self._settings:
            self._settings[name] = value  # type: ignore[literal-required]
            self._altered = True
            return
        raise TypeError(f"{name} is missing a custom setter")

    def __delattr__(self, name: str, /) -> None:
        raise RuntimeError("settings can't be deleted")

    def alter(self) -> None:
        self._altered = True

    def save(self, *, force: bool = False) -> None:
        if self._altered or force:
            json_save(SETTINGS_PATH, self._settings, sort=True)
