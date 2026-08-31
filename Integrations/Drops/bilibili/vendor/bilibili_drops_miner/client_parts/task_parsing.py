from __future__ import annotations

from typing import Any


def first_present_value(mapping: dict[str, Any], keys: tuple[str, ...]) -> Any:
    if not isinstance(mapping, dict):
        return None
    for key in keys:
        value = mapping.get(key)
        if value is not None and value != "":
            return value
    return None


def coerce_task_number(value: Any) -> int | float:
    if value is None:
        return 0
    if isinstance(value, (int, float)):
        return value
    text = str(value).strip()
    if not text:
        return 0
    try:
        number = float(text)
    except ValueError:
        return 0
    if number.is_integer():
        return int(number)
    return number


def coerce_task_int(value: Any) -> int:
    number = coerce_task_number(value)
    try:
        return int(float(number))
    except (TypeError, ValueError):
        return 0


def normalize_progress_candidates(values: Any) -> list[dict[str, Any]]:
    if isinstance(values, dict):
        return [values]
    if isinstance(values, list):
        return [item for item in values if isinstance(item, dict)]
    return []


def normalize_checkpoint_candidates(values: Any) -> list[dict[str, Any]]:
    if isinstance(values, list):
        return [item for item in values if isinstance(item, dict)]
    if not isinstance(values, dict):
        return []

    for key in ("check_points", "checkpoints", "items", "nodes"):
        nested = values.get(key)
        if isinstance(nested, list):
            return [item for item in nested if isinstance(item, dict)]
    return [values]


def extract_task_indicator_values(
    indicators: Any,
) -> tuple[int | float, int | float]:
    candidates = normalize_progress_candidates(indicators)
    if not candidates:
        return 0, 0

    watch_types = {"watch_time", "watch", "live_watch", "duration", "live_time"}
    best_cur: int | float = 0
    best_limit: int | float = 0
    watch_cur: int | float = 0
    watch_limit: int | float = 0

    for indicator in candidates:
        indicator_type = str(
            first_present_value(
                indicator,
                ("type", "indicator_type", "indicator_id"),
            )
            or ""
        ).lower()
        cur_value = coerce_task_number(
            first_present_value(
                indicator,
                ("cur_value", "cur", "current", "progress"),
            )
        )
        limit_value = coerce_task_number(
            first_present_value(
                indicator,
                ("limit", "target", "total", "max", "max_value"),
            )
        )
        if indicator_type in watch_types:
            if limit_value >= watch_limit:
                watch_cur = cur_value
                watch_limit = limit_value
        elif limit_value >= best_limit:
            best_cur = cur_value
            best_limit = limit_value

    if watch_limit > 0:
        return watch_cur, watch_limit
    return best_cur, best_limit


def extract_checkpoint_progress_values(
    checkpoint: dict[str, Any],
) -> tuple[int | float, int | float]:
    for key in ("list", "indicators", "indicator"):
        cur_value, limit_value = extract_task_indicator_values(checkpoint.get(key))
        if limit_value > 0:
            return cur_value, limit_value

    cur_value = coerce_task_number(
        first_present_value(
            checkpoint,
            ("cur_value", "cur", "current", "progress"),
        )
    )
    limit_value = coerce_task_number(
        first_present_value(
            checkpoint,
            ("limit", "target", "total", "max", "max_value"),
        )
    )
    return cur_value, limit_value
