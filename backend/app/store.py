from __future__ import annotations

import json
import os
import sqlite3
from datetime import datetime, timedelta, timezone
from pathlib import Path
from typing import Any

import yaml

from .schemas import (
    MachineConfig,
    MachineConfigAppliedRequest,
    MachineDetail,
    MachineHeartbeatRequest,
    MachineStats,
    MachineSummary,
    MachineTelemetryIngestRequest,
    utc_now,
)

PROJECT_ROOT = Path(__file__).resolve().parents[2]
MACHINE_DIR = PROJECT_ROOT / "machine"
DATA_DIR = PROJECT_ROOT / "backend" / "data"
DB_PATH = DATA_DIR / "control-plane.db"
OFFLINE_TIMEOUT = timedelta(
    seconds=max(10, int(os.getenv("DAEVA_OFFLINE_TIMEOUT_SECONDS", "35")))
)


class MachineRepository:
    def __init__(self, db_path: Path = DB_PATH) -> None:
        self._db_path = db_path
        self._db_path.parent.mkdir(parents=True, exist_ok=True)
        self._initialize()

    def list_machines(self) -> list[MachineSummary]:
        with self._connect() as connection:
            rows = connection.execute(
                """
                SELECT
                    id,
                    name,
                    variant,
                    status,
                    desired_config_version,
                    applied_config_version,
                    last_seen_at
                FROM machines
                ORDER BY name;
                """
            ).fetchall()
            return [self._build_summary(connection, row) for row in rows]

    def get_machine(self, machine_id: str) -> MachineDetail | None:
        with self._connect() as connection:
            row = self._get_machine_row(connection, machine_id)
            if row is None:
                return None

            summary = self._build_summary(connection, row)
            config = MachineConfig.model_validate(json.loads(row["desired_config_json"]))
            return MachineDetail.model_validate(
                {
                    **summary.model_dump(by_alias=True),
                    "config": config.model_dump(by_alias=True),
                }
            )

    def update_config(self, machine_id: str, config: MachineConfig, source: str = "webapp") -> MachineDetail | None:
        with self._connect() as connection:
            row = self._get_machine_row(connection, machine_id)
            if row is None:
                return None

            next_version = row["desired_config_version"] + 1
            now = utc_now().isoformat()
            payload = json.dumps(config.model_dump(by_alias=True))

            connection.execute(
                """
                UPDATE machines
                SET desired_config_json = ?,
                    desired_config_version = ?,
                    desired_config_updated_at = ?,
                    updated_at = ?
                WHERE id = ?;
                """,
                (payload, next_version, now, now, machine_id),
            )
            connection.execute(
                """
                INSERT INTO config_history (
                    machine_id,
                    version,
                    config_json,
                    source,
                    created_at
                ) VALUES (?, ?, ?, ?, ?);
                """,
                (machine_id, next_version, payload, source, now),
            )
            connection.commit()

        return self.get_machine(machine_id)

    def record_heartbeat(
        self, machine_id: str, heartbeat: MachineHeartbeatRequest
    ) -> MachineSummary | None:
        with self._connect() as connection:
            row = self._get_machine_row(connection, machine_id)
            if row is None:
                return None

            now = utc_now().isoformat()
            applied_version = (
                heartbeat.applied_config_version
                if heartbeat.applied_config_version is not None
                else row["applied_config_version"]
            )
            connection.execute(
                """
                UPDATE machines
                SET status = ?,
                    applied_config_version = ?,
                    last_seen_at = ?,
                    updated_at = ?
                WHERE id = ?;
                """,
                (heartbeat.status, applied_version, now, now, machine_id),
            )
            connection.commit()
            updated = self._get_machine_row(connection, machine_id)
            return self._build_summary(connection, updated)

    def record_config_applied(
        self, machine_id: str, payload: MachineConfigAppliedRequest
    ) -> MachineSummary | None:
        with self._connect() as connection:
            row = self._get_machine_row(connection, machine_id)
            if row is None:
                return None

            now = utc_now().isoformat()
            connection.execute(
                """
                UPDATE machines
                SET applied_config_version = ?,
                    status = ?,
                    last_seen_at = ?,
                    updated_at = ?
                WHERE id = ?;
                """,
                (payload.applied_config_version, payload.status, now, now, machine_id),
            )
            connection.execute(
                """
                INSERT INTO telemetry_events (
                    machine_id,
                    occurred_at,
                    category,
                    operation_type,
                    status,
                    payload_json
                ) VALUES (?, ?, ?, ?, ?, ?);
                """,
                (
                    machine_id,
                    now,
                    "config",
                    "config-apply",
                    payload.status,
                    json.dumps(
                        {
                            "appliedConfigVersion": payload.applied_config_version,
                            "details": payload.details,
                        }
                    ),
                ),
            )
            connection.commit()
            updated = self._get_machine_row(connection, machine_id)
            return self._build_summary(connection, updated)

    def ingest_telemetry(
        self, machine_id: str, payload: MachineTelemetryIngestRequest
    ) -> MachineSummary | None:
        with self._connect() as connection:
            row = self._get_machine_row(connection, machine_id)
            if row is None:
                return None

            now = utc_now().isoformat()
            for event in payload.events:
                connection.execute(
                    """
                    INSERT INTO telemetry_events (
                        machine_id,
                        occurred_at,
                        category,
                        operation_type,
                        status,
                        cocktail_id,
                        cocktail_name,
                        mode_name,
                        total_milliliters,
                        total_duration_ms,
                        payload_json
                    ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?);
                    """,
                    (
                        machine_id,
                        event.occurred_at.isoformat(),
                        event.category,
                        event.operation_type,
                        event.status,
                        event.cocktail_id,
                        event.cocktail_name,
                        event.mode_name,
                        event.total_milliliters,
                        event.total_duration_ms,
                        json.dumps(event.payload),
                    ),
                )

            connection.execute(
                """
                UPDATE machines
                SET last_seen_at = ?,
                    updated_at = ?
                WHERE id = ?;
                """,
                (now, now, machine_id),
            )
            connection.commit()
            updated = self._get_machine_row(connection, machine_id)
            return self._build_summary(connection, updated)

    def should_refresh_config(self, machine_id: str) -> bool:
        with self._connect() as connection:
            row = self._get_machine_row(connection, machine_id)
            if row is None:
                return False
            return row["desired_config_version"] > row["applied_config_version"]

    def _initialize(self) -> None:
        with self._connect() as connection:
            connection.executescript(
                """
                PRAGMA journal_mode = WAL;

                CREATE TABLE IF NOT EXISTS machines (
                    id TEXT PRIMARY KEY,
                    name TEXT NOT NULL,
                    variant TEXT NOT NULL,
                    status TEXT NOT NULL,
                    desired_config_version INTEGER NOT NULL,
                    applied_config_version INTEGER NOT NULL,
                    desired_config_json TEXT NOT NULL,
                    desired_config_updated_at TEXT NOT NULL,
                    last_seen_at TEXT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS config_history (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    machine_id TEXT NOT NULL,
                    version INTEGER NOT NULL,
                    config_json TEXT NOT NULL,
                    source TEXT NOT NULL,
                    created_at TEXT NOT NULL,
                    UNIQUE(machine_id, version)
                );

                CREATE TABLE IF NOT EXISTS telemetry_events (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    machine_id TEXT NOT NULL,
                    occurred_at TEXT NOT NULL,
                    category TEXT NOT NULL,
                    operation_type TEXT NOT NULL,
                    status TEXT NOT NULL,
                    cocktail_id TEXT NULL,
                    cocktail_name TEXT NULL,
                    mode_name TEXT NULL,
                    total_milliliters INTEGER NULL,
                    total_duration_ms INTEGER NULL,
                    payload_json TEXT NULL
                );

                CREATE INDEX IF NOT EXISTS idx_telemetry_machine
                    ON telemetry_events(machine_id, occurred_at);
                """
            )
            self._seed_bootstrap_data(connection)
            connection.commit()

    def _seed_bootstrap_data(self, connection: sqlite3.Connection) -> None:
        count = connection.execute("SELECT COUNT(*) AS value FROM machines;").fetchone()["value"]
        if count:
            return

        seeded = [
            {
                "id": "daeva-max-01",
                "name": "Daeva Max",
                "variant": "max",
                "status": "idle",
                "config": self._load_config_from_yaml(MACHINE_DIR / "appsettings.max.yaml"),
                "stats": {"dispense": 0, "clean": 0, "fill": 0},
            },
            {
                "id": "daeva-mini-01",
                "name": "Daeva Mini",
                "variant": "mini",
                "status": "online",
                "config": self._load_config_from_yaml(MACHINE_DIR / "appsettings.mini.yaml"),
                "stats": {"dispense": 0, "clean": 0, "fill": 0},
            },
        ]

        now = utc_now().isoformat()
        for machine in seeded:
            payload = json.dumps(machine["config"].model_dump(by_alias=True))
            connection.execute(
                """
                INSERT INTO machines (
                    id,
                    name,
                    variant,
                    status,
                    desired_config_version,
                    applied_config_version,
                    desired_config_json,
                    desired_config_updated_at,
                    last_seen_at,
                    created_at,
                    updated_at
                ) VALUES (?, ?, ?, ?, 1, 1, ?, ?, ?, ?, ?);
                """,
                (
                    machine["id"],
                    machine["name"],
                    machine["variant"],
                    machine["status"],
                    payload,
                    now,
                    now,
                    now,
                    now,
                ),
            )
            connection.execute(
                """
                INSERT INTO config_history (
                    machine_id,
                    version,
                    config_json,
                    source,
                    created_at
                ) VALUES (?, 1, ?, ?, ?);
                """,
                (machine["id"], payload, "bootstrap", now),
            )
            self._seed_demo_telemetry(connection, machine["id"], machine["stats"])

    @staticmethod
    def _seed_demo_telemetry(
        connection: sqlite3.Connection, machine_id: str, stats: dict[str, int]
    ) -> None:
        now = utc_now().isoformat()
        if stats["dispense"]:
            connection.execute(
                """
                INSERT INTO telemetry_events (
                    machine_id,
                    occurred_at,
                    category,
                    operation_type,
                    status,
                    payload_json
                ) VALUES (?, ?, 'operation', 'dispense', 'completed', ?);
                """,
                (machine_id, now, json.dumps({"count": stats["dispense"]})),
            )
        if stats["clean"]:
            connection.execute(
                """
                INSERT INTO telemetry_events (
                    machine_id,
                    occurred_at,
                    category,
                    operation_type,
                    status,
                    payload_json
                ) VALUES (?, ?, 'operation', 'clean', 'completed', ?);
                """,
                (machine_id, now, json.dumps({"count": stats["clean"]})),
            )
        if stats["fill"]:
            connection.execute(
                """
                INSERT INTO telemetry_events (
                    machine_id,
                    occurred_at,
                    category,
                    operation_type,
                    status,
                    payload_json
                ) VALUES (?, ?, 'operation', 'fill', 'completed', ?);
                """,
                (machine_id, now, json.dumps({"count": stats["fill"]})),
            )

    @staticmethod
    def _load_config_from_yaml(path: Path) -> MachineConfig:
        raw = yaml.safe_load(path.read_text(encoding="utf-8")) or {}

        def ingredient(item: dict[str, Any]) -> dict[str, Any]:
            return {
                "name": item.get("Name", ""),
                "milliliters": item.get("Milliliters", 0),
            }

        def cocktail(item: dict[str, Any]) -> dict[str, Any]:
            return {
                "id": item.get("Id", ""),
                "name": item.get("Name", ""),
                "subtitle": item.get("Subtitle", ""),
                "image": item.get("Image", ""),
                "theme": item.get("Theme", "Burgundy"),
                "isActive": item.get("IsActive", True),
                "ledR": item.get("LedR"),
                "ledG": item.get("LedG"),
                "ledB": item.get("LedB"),
                "ingredients": [ingredient(entry) for entry in item.get("Ingredients", [])],
            }

        def mode(item: dict[str, Any]) -> dict[str, Any]:
            return {
                "name": item.get("Name", ""),
                "color": item.get("Color", "#000000"),
                "ledColor": item.get("LedColor", "ORANGE"),
                "liquidAssignments": item.get("LiquidAssignments", []),
                "cocktails": [cocktail(entry) for entry in item.get("Cocktails", [])],
            }

        normalized = {
            "flowRate": {
                "millisecondsPerMilliliter": raw.get("FlowRate", {}).get(
                    "MillisecondsPerMilliliter", 10
                )
            },
            "fillDurationMs": raw.get("FillDurationMs", 5000),
            "cleanDurationMs": raw.get("CleanDurationMs", 10000),
            "liquidAssignments": raw.get("LiquidAssignments", []),
            "modes": [mode(entry) for entry in raw.get("Modes", [])],
            "cocktails": [cocktail(entry) for entry in raw.get("Cocktails", [])],
        }
        return MachineConfig.model_validate(normalized)

    def _build_summary(
        self, connection: sqlite3.Connection, row: sqlite3.Row
    ) -> MachineSummary:
        stats = self._get_stats(connection, row["id"], row["last_seen_at"])
        return MachineSummary.model_validate(
            {
                "id": row["id"],
                "name": row["name"],
                "variant": row["variant"],
                "status": self._effective_status(row["status"], row["last_seen_at"]),
                "desiredConfigVersion": row["desired_config_version"],
                "appliedConfigVersion": row["applied_config_version"],
                "stats": stats.model_dump(by_alias=True),
            }
        )

    @staticmethod
    def _effective_status(status: str, last_seen_at: str | None) -> str:
        if not last_seen_at:
            return "offline"

        seen_at = datetime.fromisoformat(last_seen_at)
        if utc_now() - seen_at > OFFLINE_TIMEOUT:
            return "offline"
        return status

    def _get_stats(
        self, connection: sqlite3.Connection, machine_id: str, last_seen_at: str | None
    ) -> MachineStats:
        counts = {"dispense": 0, "clean": 0, "fill": 0}
        rows = connection.execute(
            """
            SELECT operation_type, payload_json
            FROM telemetry_events
            WHERE machine_id = ?
              AND status = 'completed'
              AND operation_type IN ('dispense', 'clean', 'fill');
            """,
            (machine_id,),
        ).fetchall()

        for row in rows:
            payload = json.loads(row["payload_json"]) if row["payload_json"] else {}
            increment = int(payload.get("count", 1))
            counts[row["operation_type"]] += increment

        return MachineStats.model_validate(
            {
                "cocktailsDispensed": counts["dispense"],
                "cleanCycles": counts["clean"],
                "fillCycles": counts["fill"],
                "lastSeenAt": last_seen_at,
            }
        )

    def _get_machine_row(
        self, connection: sqlite3.Connection, machine_id: str
    ) -> sqlite3.Row | None:
        return connection.execute(
            """
            SELECT *
            FROM machines
            WHERE id = ?
            LIMIT 1;
            """,
            (machine_id,),
        ).fetchone()

    def _connect(self) -> sqlite3.Connection:
        connection = sqlite3.connect(self._db_path)
        connection.row_factory = sqlite3.Row
        return connection


repository = MachineRepository()
