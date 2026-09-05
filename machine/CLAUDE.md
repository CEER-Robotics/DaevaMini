# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

DaevaMini is an Avalonia (.NET 9) application for controlling Arduino-based cocktail dispensing machines. It runs on a **Raspberry Pi (linux-arm64)** and communicates with an Arduino over serial. The codebase is shared but targets two distinct hardware configurations selected at runtime.

## Build & Deploy

```bash
# Build for Raspberry Pi
dotnet publish -c Release -r linux-arm64 --self-contained true

# Deploy to Raspberry Pi (Mini machine)
rsync -av ./bin/Release/net9.0/linux-arm64/ daeva-mini@192.168.1.117:/home/daeva-mini/release/
```

Run locally for development (Debug mode opens a normal window instead of fullscreen):
```bash
dotnet run                    # Max mode
dotnet run -- --Mini          # Mini mode
```

## Mini vs Max Architecture

The app detects `--Mini` in args at startup (`App.axaml.cs`) and routes to either `MiniMainWindow` or `MainWindow`. **Everything in `Views/Mini/` and `Views/Max/` is hardware-specific.** Services, models, and config are shared.

| Aspect | Mini | Max |
|--------|------|-----|
| Pumps | 4 | 17 |
| UI entry point | `LockScreen` | `SplashPage` |
| Repository | `MiniCocktailRepository` | `MaxCocktailRepository` |
| Arduino firmware | `arduino/mini/` | `arduino/main/` |
| Mode system | 3 modes (Gin/Vodka/OG) via `appsettings.yaml` | No modes |

## Configuration

`appsettings.yaml` drives runtime behavior and is loaded via `AppConfigService` (singleton). Key sections:

- `FlowRate.MillisecondsPerMilliliter` — calibration value for pump timing
- `LiquidAssignments` — maps container positions (1–10) to liquid names
- `Modes[]` — Mini-only: each mode has its own liquid assignments and cocktail definitions
- `FillDurationMs`, `CleanDurationMs` — maintenance operation durations

## Arduino Serial Protocol

`ArduinoSerialManager` auto-discovers the Arduino port and connects at 115200 baud. Commands are built by `ArduinoProtocolHelper`:

```
ACTIVE, RGB:r,g,b, P1:ms, P2:ms, P3:ms, P4:ms
ACTIVE, BASE:COLOR_NAME, P1:ms, ...
READY
```

Pump durations are calculated from ingredient amounts × `MillisecondsPerMilliliter`. Color presets: `ORANGE`, `RED`, `GREEN`, `BLUE`, `CYAN`, `MAGENTA`, `YELLOW`, `WHITE`, `WARM_WHITE`, `PURPLE`.

The Arduino state machine: `STARTUP → WAIT → ACTIVE → ENDING → WAIT`. `TOXIC` and `MANUTENZIONE` are idle/maintenance states returning to `WAIT` on `READY`.

See `arduino/main/SERIAL_PROTOCOL.md` for the full protocol reference.

## Arduino Firmware

**Do not attempt to compile or upload Arduino firmware** — see `arduino/main/AGENTS.md`. Hardware pin assignments and animation parameters are all in `ProjectConfig.h` for each variant.

## Key Patterns

- **Singleton services**: `AppConfigService`, `ArduinoSerialManager` — both use double-checked locking
- **Repository pattern**: `ICocktailRepository` implemented by `Mini/MaxCocktailRepository`
- **MVVM**: ViewModels use `INotifyPropertyChanged`; `RelayCommand` for button bindings
- **Routed events**: Pages communicate back/settings/fill/clean actions upward via Avalonia routed events; the main window subscribes and handles navigation
- **Debug vs Release**: Debug opens a fixed-size window; Release runs fullscreen without decorations

## Wi-Fi setup

`Services/WifiService.cs` scans and connects to networks by shelling out to `nmcli`
(`System.Diagnostics.Process`, arguments passed via `ArgumentList` so nothing needs
manual shell-quoting) - that is what Raspberry Pi OS (Bookworm+) uses for networking
out of the box, so nothing new has to be installed on the machine. `Views/Max/
WifiSetupPage` drives it: scan, tap a network, on-screen keyboard for the password.

`nmcli` does not exist on this Windows dev box. `WifiService.IsSimulated` flips on the
first failed process launch and every call then answers from a small canned network
list instead - the same fallback shape as "no Arduino connected simulates the pour".
A short-enough password (or an open network) simulates "wrong password"; everything
else simulates success. Don't chase real nmcli behavior on this box - test the actual
flow on the Pi.

## UI Theming

Cocktails have a `CocktailTheme` (Burgundy, Teal, Orange) that drives card styling. `CocktailThemeHelper` maps themes to color brushes. Global style resources are in `Styles/AppStyles.axaml`.
