# Arduino Due Firmware (Pumps + WS2812 Animations)

Firmware for an Arduino Due that controls:

- 17 pump outputs (time-scheduled, non-blocking)
- 2 WS2812 strips (state-based animations)
- A serial command interface for runtime control

The code is split into focused modules:

- `main.ino`: top-level setup/loop orchestration
- `SerialCommandHandler.*`: serial RX, command parsing, and dispatch
- `PumpControl.*`: pump pin control and ON-duration scheduling
- `LedStateMachine.*`: finite state machine and transitions
- `LedAnimations.*`: animation rendering per state
- `ProjectConfig.h`: centralized pins, timings, animation parameters, and colors

## Features

- Fully non-blocking (`millis()` driven)
- Explicit state machine (`STARTUP`, `WAIT`, `ACTIVE`, `ENDING`, `TOXIC`, `MANUTENZIONE`)
- Pump scheduling from one serial line with multiple `P<n>:<ms>` segments
- Active animation color selection via named presets
- Status/error acknowledgements over serial

### Toxic animation mode selection

In `ProjectConfig.h`:

- `Animation::kToxicAnim = 1`: current toxic random patterns (strobe/chase/alternate)
- `Animation::kToxicAnim = 2`: slow breathing animation using the WAIT color (`kBluDaeva`)

## Hardware Mapping

### Pump outputs (17)

Pins:

`6, 7, 8, 9, 10, 11, 12, 13, 22, 24, 26, 28, 30, 32, 34, 36, 38`

### WS2812 strips

- Strip 1: pin `2`, length `24`
- Strip 2: pin `3`, length `34`

## Serial Protocol

Baud rate: `115200` on `SerialUSB`.

Send one command line terminated by `\n`.

For a full host-integration reference (parser behavior, edge cases, and implementation notes), see `SERIAL_PROTOCOL.md`.

### 1. ACTIVE command

Format:

```text
ACTIVE, BASE:<COLOR_NAME>, P1:<ms>, P2:<ms>, ...
```

Example:

```text
ACTIVE, BASE:ORANGE, P1:700, P3:1000
```

Behavior:

- Accepted only in `WAIT`
- Sets active LED animation color from `BASE` (or `COLOR`)
- Schedules pumps with provided durations
- Enters `ACTIVE` state when at least one valid pump duration is provided

### 2. READY command

```text
READY
```

Behavior:

- If in `TOXIC` or `MANUTENZIONE`, returns to `WAIT`
- If already in `WAIT`, refreshes inactivity timeout

### 3. TOXIC command

```text
TOXIC
```

Behavior:

- Enters `TOXIC` state
- Turns all pumps off

### 4. MANUTENZIONE command

```text
MANUTENZIONE
```

Behavior:

- Enters maintenance state
- Turns all pumps off

## Firmware Responses

Possible serial responses:

- `OK ACTIVE`
- `OK READY`
- `OK TOXIC`
- `OK MANUTENZIONE`
- `ERR ACTIVE COLOR`
- `ERR ACTIVE PARAMS`
- `IGNORED ACTIVE`
- `DONE` (emitted when `ACTIVE` finishes and transitions to `ENDING`)

### Startup and ending status

- On boot, firmware prints one banner line:
  `Ready. ACTIVE, BASE:ORANGE, P1:700, P3:1000`
- There is no dedicated `OK STARTUP` response.
- For ending, firmware emits `DONE` at `ACTIVE -> ENDING`.
- There is no dedicated `OK ENDING` response.

## State Machine

### States

- `ST_STARTUP`: startup animation
- `ST_WAIT`: idle/waiting for valid commands
- `ST_ACTIVE`: active run with pumps scheduled
- `ST_ENDING`: final flashing animation
- `ST_TOXIC`: toxic alarm mode
- `ST_MANUTENZIONE`: maintenance mode

### Main transitions

- `STARTUP -> WAIT` after startup timeout (`10000 ms`)
- `WAIT -> ACTIVE` on valid `ACTIVE,...` command
- `ACTIVE -> ENDING` when the latest pump schedule expires
- `ENDING -> WAIT` after end animation duration
- `WAIT -> TOXIC` after inactivity timeout (if `Timing::kToxicTimeoutMs > 0` and no `READY`)
- `TOXIC -> WAIT` on `READY`
- `MANUTENZIONE -> WAIT` on `READY`

`Timing::kToxicTimeoutMs = -1` disables automatic `WAIT -> TOXIC`; in that case `TOXIC` is entered only by serial command.

## Color Presets for ACTIVE

Accepted preset names:

- `ORANGE`
- `RED`
- `GREEN`
- `BLUE`
- `CYAN`
- `MAGENTA`
- `YELLOW`
- `WHITE`
- `WARM_WHITE`
- `PURPLE`

All state colors (startup, wait/base, toxic, maintenance, ending fallback) and preset color entries are defined in `ProjectConfig.h`.

## Dependency

- `Adafruit NeoPixel` library
