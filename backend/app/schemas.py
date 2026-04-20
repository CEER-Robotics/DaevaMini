from __future__ import annotations

from datetime import datetime, timezone
from typing import Any, Literal

from pydantic import BaseModel, ConfigDict, Field


def utc_now() -> datetime:
    return datetime.now(timezone.utc)


class ApiModel(BaseModel):
    model_config = ConfigDict(populate_by_name=True)


class FlowRateConfig(ApiModel):
    milliseconds_per_milliliter: int = Field(..., alias="millisecondsPerMilliliter")


class IngredientConfig(ApiModel):
    name: str
    milliliters: int


class CocktailConfig(ApiModel):
    id: str
    name: str
    subtitle: str
    image: str
    theme: str
    is_active: bool = Field(..., alias="isActive")
    led_r: int | None = Field(None, alias="ledR")
    led_g: int | None = Field(None, alias="ledG")
    led_b: int | None = Field(None, alias="ledB")
    ingredients: list[IngredientConfig]


class ModeConfig(ApiModel):
    name: str
    color: str
    led_color: str = Field(..., alias="ledColor")
    liquid_assignments: list[str] = Field(default_factory=list, alias="liquidAssignments")
    cocktails: list[CocktailConfig] = Field(default_factory=list)


class MachineConfig(ApiModel):
    flow_rate: FlowRateConfig = Field(..., alias="flowRate")
    fill_duration_ms: int = Field(..., alias="fillDurationMs")
    clean_duration_ms: int = Field(..., alias="cleanDurationMs")
    liquid_assignments: list[str] = Field(default_factory=list, alias="liquidAssignments")
    modes: list[ModeConfig] = Field(default_factory=list)
    cocktails: list[CocktailConfig] = Field(default_factory=list)


class MachineStats(ApiModel):
    cocktails_dispensed: int = Field(..., alias="cocktailsDispensed")
    clean_cycles: int = Field(..., alias="cleanCycles")
    fill_cycles: int = Field(..., alias="fillCycles")
    last_seen_at: datetime | None = Field(None, alias="lastSeenAt")


class MachineSummary(ApiModel):
    id: str
    name: str
    variant: Literal["mini", "max"]
    status: Literal["online", "offline", "idle", "busy"]
    desired_config_version: int = Field(..., alias="desiredConfigVersion")
    applied_config_version: int = Field(..., alias="appliedConfigVersion")
    stats: MachineStats


class MachineDetail(MachineSummary):
    config: MachineConfig


class MachineConfigUpdate(ApiModel):
    config: MachineConfig


class MachineConfigResponse(ApiModel):
    machine: MachineSummary
    config: MachineConfig
    updated_at: datetime = Field(default_factory=utc_now, alias="updatedAt")


class MachineHeartbeatRequest(ApiModel):
    status: Literal["online", "offline", "idle", "busy"] = "online"
    applied_config_version: int | None = Field(None, alias="appliedConfigVersion")
    current_operation: str | None = Field(None, alias="currentOperation")


class MachineHeartbeatResponse(ApiModel):
    machine: MachineSummary
    should_refresh_config: bool = Field(..., alias="shouldRefreshConfig")
    poll_interval_seconds: int = Field(15, alias="pollIntervalSeconds")


class MachineConfigAppliedRequest(ApiModel):
    applied_config_version: int = Field(..., alias="appliedConfigVersion")
    status: Literal["online", "offline", "idle", "busy"] = "idle"
    details: dict[str, Any] = Field(default_factory=dict)


class MachineTelemetryEvent(ApiModel):
    category: str = "operation"
    operation_type: str = Field(..., alias="operationType")
    status: str
    occurred_at: datetime = Field(default_factory=utc_now, alias="occurredAt")
    cocktail_id: str | None = Field(None, alias="cocktailId")
    cocktail_name: str | None = Field(None, alias="cocktailName")
    mode_name: str | None = Field(None, alias="modeName")
    total_milliliters: int | None = Field(None, alias="totalMilliliters")
    total_duration_ms: int | None = Field(None, alias="totalDurationMs")
    payload: dict[str, Any] = Field(default_factory=dict)


class MachineTelemetryIngestRequest(ApiModel):
    events: list[MachineTelemetryEvent] = Field(default_factory=list)


class TelemetryIngestResponse(ApiModel):
    accepted_events: int = Field(..., alias="acceptedEvents")
    machine: MachineSummary


class HealthResponse(ApiModel):
    status: str
    now: datetime = Field(default_factory=utc_now)
