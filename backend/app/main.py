from __future__ import annotations

from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware

from .schemas import (
    HealthResponse,
    MachineConfigAppliedRequest,
    MachineConfigResponse,
    MachineConfigUpdate,
    MachineDetail,
    MachineHeartbeatRequest,
    MachineHeartbeatResponse,
    MachineSummary,
    MachineTelemetryIngestRequest,
    TelemetryIngestResponse,
)
from .store import repository

app = FastAPI(title="Daeva Control API", version="0.2.0")

app.add_middleware(
    CORSMiddleware,
    allow_origins=["http://127.0.0.1:5173", "http://localhost:5173"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)


@app.get("/health", response_model=HealthResponse)
def health() -> HealthResponse:
    return HealthResponse(status="ok")


@app.get("/api/machines", response_model=list[MachineSummary])
def list_machines() -> list[MachineSummary]:
    return repository.list_machines()


@app.get("/api/machines/{machine_id}", response_model=MachineDetail)
def get_machine(machine_id: str) -> MachineDetail:
    machine = repository.get_machine(machine_id)
    if machine is None:
        raise HTTPException(status_code=404, detail="Machine not found")
    return machine


@app.get("/api/machines/{machine_id}/config", response_model=MachineConfigResponse)
def get_machine_config(machine_id: str) -> MachineConfigResponse:
    machine = repository.get_machine(machine_id)
    if machine is None:
        raise HTTPException(status_code=404, detail="Machine not found")
    return MachineConfigResponse(machine=_to_summary(machine), config=machine.config)


@app.get("/api/machines/{machine_id}/desired-config", response_model=MachineConfigResponse)
def get_desired_machine_config(machine_id: str) -> MachineConfigResponse:
    machine = repository.get_machine(machine_id)
    if machine is None:
        raise HTTPException(status_code=404, detail="Machine not found")
    return MachineConfigResponse(machine=_to_summary(machine), config=machine.config)


@app.put("/api/machines/{machine_id}/config", response_model=MachineConfigResponse)
def update_machine_config(machine_id: str, payload: MachineConfigUpdate) -> MachineConfigResponse:
    machine = repository.update_config(machine_id, payload.config)
    if machine is None:
        raise HTTPException(status_code=404, detail="Machine not found")
    return MachineConfigResponse(machine=_to_summary(machine), config=machine.config)


@app.post("/api/machines/{machine_id}/heartbeat", response_model=MachineHeartbeatResponse)
def machine_heartbeat(
    machine_id: str, payload: MachineHeartbeatRequest
) -> MachineHeartbeatResponse:
    machine = repository.record_heartbeat(machine_id, payload)
    if machine is None:
        raise HTTPException(status_code=404, detail="Machine not found")
    return MachineHeartbeatResponse(
        machine=machine,
        shouldRefreshConfig=repository.should_refresh_config(machine_id),
        pollIntervalSeconds=15,
    )


@app.post("/api/machines/{machine_id}/config-applied", response_model=MachineHeartbeatResponse)
def config_applied(
    machine_id: str, payload: MachineConfigAppliedRequest
) -> MachineHeartbeatResponse:
    machine = repository.record_config_applied(machine_id, payload)
    if machine is None:
        raise HTTPException(status_code=404, detail="Machine not found")
    return MachineHeartbeatResponse(
        machine=machine,
        shouldRefreshConfig=repository.should_refresh_config(machine_id),
        pollIntervalSeconds=15,
    )


@app.post("/api/machines/{machine_id}/telemetry", response_model=TelemetryIngestResponse)
def ingest_machine_telemetry(
    machine_id: str, payload: MachineTelemetryIngestRequest
) -> TelemetryIngestResponse:
    machine = repository.ingest_telemetry(machine_id, payload)
    if machine is None:
        raise HTTPException(status_code=404, detail="Machine not found")
    return TelemetryIngestResponse(
        acceptedEvents=len(payload.events),
        machine=machine,
    )


def _to_summary(machine: MachineDetail) -> MachineSummary:
    return MachineSummary.model_validate(machine.model_dump(by_alias=True))
