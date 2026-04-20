# Backend

Minimal FastAPI backend for the Daeva machine/webapp control plane.

## Run

```bash
uv sync
uv run uvicorn app.main:app --reload
```

If you prefer the existing virtualenv:

```bash
.venv\Scripts\activate
pip install -e .
uvicorn app.main:app --reload
```

The API starts on `http://127.0.0.1:8000`.

## Environment Variables

- `DAEVA_OFFLINE_TIMEOUT_SECONDS`
  Controls how long the backend waits without heartbeats before marking a machine as `offline`.
  Default: `35`

PowerShell example:

```powershell
$env:DAEVA_OFFLINE_TIMEOUT_SECONDS="20"
uv run uvicorn app.main:app --reload
```

## Current scope

- health endpoint
- list machines
- read/update desired machine config
- machine heartbeat endpoint
- machine apply-status endpoint
- telemetry ingestion endpoint
- SQLite persistence seeded from `machine/appsettings.max.yaml` and `machine/appsettings.mini.yaml`

The backend stores data in `backend/data/control-plane.db`.
