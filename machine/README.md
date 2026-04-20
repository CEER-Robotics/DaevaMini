## Build
```bash
dotnet publish -c Release -r linux-arm64 --self-contained true
```

## Environment Variables

- `DAEVA_BACKEND_URL`
  Backend base URL used by the machine sync service.
  Default: `http://127.0.0.1:8000`

- `DAEVA_MACHINE_ID`
  Overrides the machine id sent to the backend.
  Defaults:
  `daeva-max-01` for `appsettings.max.yaml`
  `daeva-mini-01` for `appsettings.mini.yaml`

PowerShell example:

```powershell
$env:DAEVA_BACKEND_URL="http://192.168.1.50:8000"
$env:DAEVA_MACHINE_ID="daeva-max-lab"
dotnet run --project machine/Daeva.csproj
```

## Copy to raspberry pi
```bash
rsync -av ./bin/Release/net9.0/linux-arm64/ daeva-mini@192.168.1.117:/home/daeva-mini/release/
```
