# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Repository layout

Three deliverables that together make up a cocktail-dispensing machine. They are developed in one repo because the host app and the firmware share a serial contract that must stay in lockstep.

| Dir | What it is | Language / stack |
|-----|-----------|------------------|
| `machine/` | The **host UI app** that runs on a Raspberry Pi and drives the machine. Also talks to the backend "gestionale". | Avalonia UI, .NET 9 (C#) |
| `arduino/` | **Firmware** that receives serial commands from the host and drives pumps + WS2812 LED strips. | C++ (Arduino) |
| `pump-tester/` | A **standalone bench tool** (single HTML file, Web Serial API) to exercise pumps directly from a Chromium browser without the host app. | HTML/JS |

`machine/` has its own `CLAUDE.md` with deeper UI/MVVM internals — read it when working inside that project. This root file covers the whole system and the parts that span more than one subsystem.

## The serial contract binds `machine/` and `arduino/`

This is the single most important cross-cutting fact. The C# `ArduinoProtocolHelper` (`machine/ArduinoProtocolHelper.cs`) **builds** exactly the command lines that the firmware's `SerialCommandHandler` (`arduino/libraries/DaevaMax/src/`) **parses**. Change one side and you must change the other.

- Canonical spec: `arduino/libraries/DaevaMax/SERIAL_PROTOCOL.md`. Treat it as the source of truth and keep it updated.
- Connection: `115200` baud, `8N1`, newline-terminated lines, no flow control.
- Command shape: `ACTIVE, RGB:r,g,b, P1:ms, P3:ms` or `ACTIVE, BASE:COLOR_NAME, ...`; plus `READY`, `TOXIC`, `MANUTENZIONE`.
- Firmware state machine: `STARTUP → WAIT → ACTIVE → ENDING → WAIT`. `ACTIVE` is accepted **only in `WAIT`**; otherwise the firmware replies `IGNORED ACTIVE`.
- Pump durations are computed host-side as `milliliters × FlowRate.MillisecondsPerMilliliter`, per liquid, mapped to a pump channel by position in `LiquidAssignments`.

**ACTIVE is sent with retry and is safe to retry.** `ArduinoSerialManager.TrySendActive` resends up to 3× when no ack arrives, because the firmware drops inbound bytes while its LED driver has interrupts disabled. This can't double-pour: once dispensing, the board is no longer in `WAIT` and answers `IGNORED ACTIVE` without scheduling a pump.

## Mini vs Max — two hardware variants, mostly shared code

The host app selects the variant from a command-line arg at startup (`App.axaml.cs`): `--Mini` → Mini, otherwise Max. `Views/Mini/` and `Views/Max/` are the only hardware-specific UI; services, models, and config are shared.

| | Mini | Max |
|--|------|-----|
| Pumps | 4 | 17 |
| Config file | `appsettings.mini.yaml` | `appsettings.max.yaml` |
| Repository | `MiniCocktailRepository` | `MaxCocktailRepository` |
| Window / entry | `MiniMainWindow` → `LockScreen` | `MainWindow` → `SplashPage` |
| Modes | 3 modes (each with own liquids + cocktails) | none |
| Backend sync | **off** | **on** (started in `App.OnFrameworkInitializationCompleted`) |
| Firmware | `arduino/mini/` (standalone sketch) | `arduino/main/` (Due) or `arduino/teensy_max/` (Teensy 4.1), both share `libraries/DaevaMax` |

## Backend ("gestionale") integration — offline-first telemetry

Max machines report to a remote backend. This layer is entirely in `machine/Services/` and is **not covered by `machine/CLAUDE.md`** yet.

- **`LocalMachineStore`** — SQLite (`daeva-machine.db` next to the binary, WAL mode). Holds three tables: `config_documents`, `machine_events` (with an `uploaded_at_utc` upload-queue column), and `machine_sync_state`. This is the durable buffer: events are recorded locally first and survive restarts / network outages.
- **`MachineTelemetryService`** — fire-and-forget recorder. `RecordDispenseEvent`, `RecordMaintenanceEvent` (fill/clean), `RecordConfigEvent`. Every pour and maintenance action writes a row here with `completed`/`failed` status.
- **`MachineSyncService`** — background loop (25 s). POSTs a heartbeat to `/api/machine-status` and uploads unsent pour events to `/api/machine-events`, marking them uploaded on success. Sends a final "offline" heartbeat on shutdown. Only pour events (`operation`/`dispense`/`completed`) are forwarded; other event types are marked uploaded and skipped.
- **`MachineRuntimeState`** — process-wide busy tracker. Pour/fill/clean handlers wrap their work in `using var op = MachineRuntimeState.Instance.BeginOperation("dispense")`; the heartbeat reports `GREEN`/`YELLOW` and the current operation from this.
- **`MachineProfileHelper`** — resolves machine id / secret / backend URL from env vars (see below).

Auth: requests carry the `x-machine-secret` header when `DAEVA_MACHINE_SECRET` is set.

## Configuration

`appsettings.max.yaml` / `appsettings.mini.yaml` are copied to the output dir and drive runtime behavior. **They are seeds, not the live store.** On load, `AppConfigService` imports each YAML into the SQLite `config_documents` table (`INSERT OR IGNORE`) and thereafter reads config from SQLite; saves are versioned in SQLite and mirrored back out to the YAML file. Profile key = the config file name.

Key fields: `FlowRate.MillisecondsPerMilliliter` (pump calibration), `LiquidAssignments` (pump position → liquid name), `FillDurationMs` / `CleanDurationMs`, `SettingsPin`. Mini adds `Modes[]`, each with its own `LiquidAssignments`, `LedColor`, and `Cocktails`.

### Environment variables (machine app)

- `DAEVA_BACKEND_URL` — backend base URL. Default: `https://demoapp-production-e677.up.railway.app`.
- `DAEVA_MACHINE_ID` — overrides id sent to backend (defaults `daeva-max-01` / `daeva-mini-01`).
- `DAEVA_MACHINE_SECRET` — per-machine secret; required for `/api/machine-events` and `/api/machine-status`.
- `DAEVA_SERIAL_PORT` — force a specific serial port (e.g. `/dev/ttyAMA0` for the Pi 5 GPIO UART). Unset = auto-detect, which prefers `USB`/`ACM`/`COM` names and is unreliable on the Pi's onboard UART.

## Common commands

### Machine (host app)

```powershell
# Run locally (Debug = windowed; Release = fullscreen, no decorations)
dotnet run --project machine/Daeva.csproj            # Max
dotnet run --project machine/Daeva.csproj -- --Mini  # Mini

# With backend wired up (PowerShell)
$env:DAEVA_MACHINE_SECRET="<SECRET>"; dotnet run --project machine/Daeva.csproj

# Publish + deploy to the Raspberry Pi
dotnet publish -c Release -r linux-arm64 --self-contained true
rsync -av ./bin/Release/net9.0/linux-arm64/ daeva-mini@192.168.1.117:/home/daeva-mini/release/
```

There is no test project. In `DEBUG` builds, if no Arduino is connected the app **simulates** the dispense (still records a `completed` telemetry event) instead of failing — useful for UI work on a dev box.

### Firmware

⚠️ Per `arduino/main/AGENTS.md`: **do not compile and do not flash** — the board is generally not connected. Edit firmware and update docs, but leave build/upload to the human. When they do build, the commands (from `arduino/README.md`) are:

```powershell
# Set the Arduino IDE sketchbook to the arduino/ dir so libraries/DaevaMax is found.
arduino-cli compile --libraries arduino/libraries --fqbn arduino:sam:arduino_due_x arduino/main
arduino-cli compile --libraries arduino/libraries --fqbn "teensy:avr:teensy41:usb=serial,speed=600,opt=o2std,keys=en-us" arduino/teensy_max
```

Teensy upload has two non-obvious failure modes (the Teensy Loader app must be running; use the `teensy`-protocol port from `arduino-cli board list`, not the COM port) — details in `arduino/teensy_max/README.md`.

### Pump tester

`pump-tester/index.html` uses the Web Serial API — open it in Chrome/Edge over `localhost` or `https` (not `file://`). It connects directly to the board (USB vendor `0x16c0`, PJRC/Teensy) and speaks the same protocol; use it to bench-test pumps without the host app.

## Firmware source structure

- `arduino/libraries/DaevaMax/` — shared MAX firmware (Due + Teensy 4.1). `SerialCommandHandler`, `PumpControl`, `LedStateMachine`/`LedAnimations`, `DaevaMaxFirmware`.
  - `src/boards/ArduinoDue.h`, `src/boards/Teensy41.h` — per-board pin maps and serial setup (Due uses `SerialUSB`; Teensy uses `Serial1` on pins 0/1 for the Pi GPIO UART link).
  - `src/ProjectConfig.h` — shared tunables: pin counts, strip lengths, colors, animation params, timeouts (e.g. `Timing::kToxicTimeoutMs`, `Animation::kToxicAnim`).
- `arduino/main/main.ino`, `arduino/teensy_max/teensy_max.ino` — thin sketches that just call the shared library.
- `arduino/mini/` — the Mini variant is a **separate, self-contained sketch** (does not use `libraries/DaevaMax`).

Dependency: Adafruit NeoPixel (install via Library Manager).
