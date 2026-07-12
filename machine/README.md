## Build
```bash
dotnet publish -c Release -r linux-arm64 --self-contained true
```

## Environment Variables

- `DAEVA_BACKEND_URL`
  Backend base URL used by the machine sync service.
  Default: `https://demoapp-production-e677.up.railway.app`

- `DAEVA_MACHINE_ID`
  Overrides the machine id sent to the backend.
  Defaults:
  `daeva-max-01` for `appsettings.max.yaml`
  `daeva-mini-01` for `appsettings.mini.yaml`

- `DAEVA_MACHINE_SECRET`
  Per-machine secret issued by the gestionale during provisioning. Required for
  `/api/machine-events` and `/api/machine-status`.

PowerShell example:

```powershell
$env:DAEVA_BACKEND_URL="https://demoapp-production-e677.up.railway.app"
$env:DAEVA_MACHINE_ID="daeva-max-lab"
$env:DAEVA_MACHINE_SECRET="<SECRET>"
dotnet run --project machine/Daeva.csproj
```

## Copy to raspberry pi
```bash
rsync -av ./bin/Release/net9.0/linux-arm64/ daeva-mini@192.168.1.117:/home/daeva-mini/release/
```
