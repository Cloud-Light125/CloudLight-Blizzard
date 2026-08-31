from __future__ import annotations

from typing import Any

from bilibili_drops_miner.client_parts.models import (
    TaskCheckpointProgress,
    TaskProgress,
)
from bilibili_drops_miner.client_parts.task_parsing import (
    coerce_task_int,
    coerce_task_number,
    extract_checkpoint_progress_values,
    extract_task_indicator_values,
    first_present_value,
    normalize_checkpoint_candidates,
)


def normalize_task_ids(task_ids: list[str]) -> list[str]:
    return [task_id.strip() for task_id in task_ids if task_id.strip()]


def resolve_reward_task_ids(
    task_ids: list[str],
    progresses: list[TaskProgress],
) -> list[str]:
    """Resolve aggregate task IDs to the concrete checkpoint IDs used for claims."""
    progress_by_id = {
        progress.task_id.strip(): progress
        for progress in progresses
        if progress.task_id.strip()
    }
    resolved_ids: list[str] = []
    seen: set[str] = set()

    for task_id in normalize_task_ids(task_ids):
        progress = progress_by_id.get(task_id)
        checkpoint_ids = (
            [point.sid.strip() for point in progress.check_points if point.sid.strip()]
            if progress is not None
            else []
        )
        candidates = checkpoint_ids or [task_id]
        for candidate in candidates:
            if candidate in seen:
                continue
            seen.add(candidate)
            resolved_ids.append(candidate)

    return resolved_ids


def parse_task_checkpoints(check_points: Any) -> list[TaskCheckpointProgress]:
    checkpoints: list[TaskCheckpointProgress] = []
    for item in normalize_checkpoint_candidates(check_points):
        sid = str(
            first_present_value(item, ("sid", "task_id", "id", "checkpoint_id"))
            or ""
        )
        alias = str(
            first_present_value(item, ("alias", "task_name", "name", "title"))
            or sid
        )
        status = coerce_task_int(first_present_value(item, ("status", "task_status")))
        cur_value, limit_value = extract_checkpoint_progress_values(item)
        award_name = str(
            first_present_value(item, ("award_name", "reward_name")) or ""
        )
        award_count = coerce_task_number(
            first_present_value(item, ("count", "award_count", "num"))
        )
        checkpoints.append(
            TaskCheckpointProgress(
                sid=sid,
                alias=alias,
                status=status,
                cur_value=cur_value,
                limit_value=limit_value,
                award_name=award_name,
                award_count=award_count,
            )
        )
    return checkpoints


def parse_task_progress_payload(payload: dict[str, Any]) -> list[TaskProgress]:
    if payload.get("code") != 0:
        raise ValueError(f"查询任务进度失败: {payload.get('message')}")

    data = payload.get("data") or {}
    if isinstance(data, list):
        task_list = data
    elif isinstance(data, dict):
        task_list = data.get("list") or data.get("tasks") or []
    else:
        task_list = []
    progresses: list[TaskProgress] = []
    for item in task_list:
        if not isinstance(item, dict):
            continue
        task_id = str(first_present_value(item, ("task_id", "sid", "id")) or "")
        task_name = str(
            first_present_value(item, ("task_name", "name", "title", "alias"))
            or task_id
        )
        status = coerce_task_int(
            first_present_value(item, ("task_status", "status"))
        )
        cur_value, limit_value = extract_task_indicator_values(item.get("indicators"))
        check_points = parse_task_checkpoints(item.get("check_points"))
        if not check_points:
            check_points = parse_task_checkpoints(item.get("accumulative_check_points"))
        if limit_value <= 0:
            limit_value = coerce_task_number(
                first_present_value(item, ("limit", "target", "total"))
            )
        if cur_value <= 0:
            cur_value = coerce_task_number(
                first_present_value(item, ("cur_value", "cur", "progress"))
            )
        if limit_value <= 0 and check_points:
            best_checkpoint = max(check_points, key=lambda point: point.limit_value)
            cur_value = best_checkpoint.cur_value
            limit_value = best_checkpoint.limit_value
        progresses.append(
            TaskProgress(
                task_id=task_id,
                task_name=task_name,
                status=status,
                cur_value=cur_value,
                limit_value=limit_value,
                check_points=check_points,
                task_type=coerce_task_int(item.get("task_type")),
                statistic_type=coerce_task_int(item.get("statistic_type")),
                period_type=coerce_task_int(item.get("period_type")),
                award_type=coerce_task_int(item.get("award_type")),
                can_edit=coerce_task_int(item.get("can_edit")),
                is_need_polling=coerce_task_int(item.get("is_need_polling")),
            )
        )
    return progresses
