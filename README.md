## Build
```bash
dotnet publish -c Release -r linux-arm64 --self-contained true
```
## Copy to raspberry pi
```bash
rsync -av ./bin/Release/net9.0/linux-arm64/ daeva-mini@192.168.1.117:/home/daeva-mini/release/
```
