# Daeva MAX Serial Command Protocol

This document is the reference for anyone writing software that communicates with this firmware over USB serial.

## 1. Connection

- Arduino Due port: native USB (`SerialUSB`)
- Teensy 4.1 port: USB (`Serial`, with USB Type set to `Serial`)
- Baud rate: `115200`
- Data bits / parity / stop bits: `8N1`
- Flow control: none
- Line ending expected by firmware: newline `\n`

Notes:
- `\r` is ignored by firmware.
- Each command must be sent as one full line.

## 2. Startup Behavior

On boot, the board prints:

```text
Ready. ACTIVE, BASE:ORANGE, P1:700, P3:1000
```

This is only a banner/example, not an acknowledgement for a command.

## 3. Supported Commands

Commands are case-sensitive unless noted.

## 3.1 `READY`

```text
READY
```

Meaning:
- If state is `TOXIC` or `MANUTENZIONE`: transition to `WAIT`
- If state is `WAIT`: refresh inactivity timeout

Possible response:
- `OK READY`

If sent in other states (`STARTUP`, `ACTIVE`, `ENDING`), it is ignored with no response.

## 3.2 `TOXIC`

```text
TOXIC
```

Meaning:
- Enter `TOXIC` state
- Turn all pumps off

Possible response:
- `OK TOXIC`

If sent in `STARTUP`, it is ignored with no response.

## 3.3 `MANUTENZIONE`

```text
MANUTENZIONE
```

Meaning:
- Enter maintenance state (`MANUTENZIONE`)
- Turn all pumps off

Possible response:
- `OK MANUTENZIONE`

If sent in `STARTUP`, it is ignored with no response.

## 3.4 `ACTIVE`

General format:

```text
ACTIVE, BASE:<COLOR_NAME>, P1:<ms>, P2:<ms>, ...
```

`COLOR` can be used instead of `BASE`. You can also pass an RGB triple instead of a color name:

```text
ACTIVE, RGB:<r>,<g>,<b>, P1:<ms>, ...
```

Example: `ACTIVE, RGB:255,128,0, P1:1000` (orange). Each of `r`, `g`, `b` must be 0–255.

Valid color names (when using `BASE:` or `COLOR:`):
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

Color parsing details:
- Case-insensitive for user input (`orange` works).
- Spaces and `-` in color names are normalized to `_` (`warm white`, `warm-white`, `WARM_WHITE` all work).

Pump parsing details:
- Valid pump IDs are `P1` to `P17`.
- Lowercase `p` is accepted (`p3:1000`).
- Duration unit is milliseconds.
- `P<n>:0` explicitly turns that pump off.
- At least one pump must have duration `> 0` for command acceptance.

Possible responses:
- `OK ACTIVE`
- `ERR ACTIVE COLOR` (missing/invalid color field)
- `ERR ACTIVE PARAMS` (no valid non-zero pump duration found)
- `IGNORED ACTIVE` (command is syntactically valid but board is not in `WAIT`)

## 3.5 `TINT`

```text
TINT, RGB:<r>,<g>,<b>
TINT, BASE:<COLOR_NAME>
TINT OFF
```

Meaning:
- Recolors the idle (`WAIT`) strips, crossfading over ~600 ms.
- `TINT OFF` fades back to the default idle color.
- Pumps are not touched and no state change occurs: the board stays in `WAIT`,
  so `ACTIVE` keeps working while a tint is held. This is what makes it usable
  for the settings screens, which still have to run fills, cleans and flow
  calibration while showing their own color.

Intended use is telling the machine which screen the host is on: default idle
while browsing, orange while the settings screens are open.

Color fields follow exactly the same rules as `ACTIVE` (`RGB:` triple, or `BASE:`/
`COLOR:` with a preset name, case-insensitive, `-`/space normalized to `_`).

Possible responses:
- `OK TINT`
- `ERR TINT COLOR` (missing/invalid color field)
- `IGNORED TINT` (board is not in `WAIT`)

Note `TINT OFF` is matched as an exact line, so it takes no other fields.

A held tint survives a pour: `ENDING` fades back to the tint colour rather than to
the default, so running a fill from the settings screens returns to the settings
colour. The host owns the tint and is responsible for clearing it; it is reset
only by a board reboot.

### Attract mode

After `Animation::kIdleAttractDelayMs` (5 minutes) with no pour and no tint change,
the idle strips start beating twice per cycle to draw attention. Any `ACTIVE` or
`TINT` resets the timer. A held tint suppresses it entirely, on the grounds that a
tinted machine is one somebody is currently using.

## 3.6 `TAP`

```text
TAP, RGB:<r>,<g>,<b>, P<n>
TAP OFF
```

For drinks drawn rather than measured: a normally-closed solenoid held open while the
guest fills their own glass.

**`TAP` carries no duration on purpose.** One command opens the valve for
`Timing::kTapKeepAliveWindowMs` (3 s) only, and the host must keep repeating it to keep
the valve open. Stop repeating - because the host crashed, the app was killed, or the
cable came out - and the valve closes on its own. A normally-closed valve already fails
safe if the *board* dies; the keep-alive is what covers the *host* dying while the board
is still happily holding the valve open.

Repeating `TAP` while it is already open is a keep-alive, not a second pour: it re-arms
the same channel and pushes the deadline out. `Timing::kTapMaxOpenMs` (120 s) caps one
session however healthy the host claims to be.

The LED animation is the normal pour animation, running for as long as the valve is
open. `TAP OFF` closes the valve and runs the usual end-of-pour sequence, so `DONE` and
`OK READY` follow exactly as they do for `ACTIVE`.

Possible responses:
- `OK TAP` (opened, or kept alive)
- `OK TAP OFF` (closed on request)
- `ERR TAP COLOR` (missing/invalid color field)
- `ERR TAP PARAMS` (no valid `P<n>` in the line)
- `ERR TAP TIMEOUT` (the 120 s ceiling was reached; the valve is now closed)
- `IGNORED TAP` (opening while the board is not in `WAIT`, or closing when nothing is open)

Note the first `TAP` is gated on `WAIT` exactly like `ACTIVE`; keep-alives are not,
since by then the board is in `ACTIVE` by definition.

## 4. State Constraints

State machine summary:
- `STARTUP -> WAIT` after ~10 s
- `WAIT -> ACTIVE` on valid `ACTIVE`
- `ACTIVE -> ENDING` when scheduled pumps complete
- `ENDING -> WAIT` after the ending hold and fade
- `TINT` causes no transition: it only recolors `WAIT`
- `WAIT -> TOXIC` after inactivity timeout only if `ProjectConfig::Timing::kToxicTimeoutMs > 0` (and no `READY`)
- `TOXIC -> WAIT` on `READY`
- `MANUTENZIONE -> WAIT` on `READY`

Important:
- `ACTIVE` and `TINT` are accepted only in `WAIT`.
- Some invalid-state commands are silently ignored (no serial response).
- If `ProjectConfig::Timing::kToxicTimeoutMs = -1`, automatic `WAIT -> TOXIC` is disabled and `TOXIC` is entered only via serial command.

## 5. Firmware Responses

Possible lines emitted by firmware:

- `OK ACTIVE`
- `OK READY`
- `OK TOXIC`
- `OK MANUTENZIONE`
- `OK TINT`
- `OK TAP`
- `OK TAP OFF`
- `ERR ACTIVE COLOR`
- `ERR ACTIVE PARAMS`
- `ERR TINT COLOR`
- `ERR TAP PARAMS`
- `ERR TAP TIMEOUT`
- `IGNORED ACTIVE`
- `IGNORED TINT`
- `IGNORED TAP`
- `DONE`

`DONE` is emitted when `ACTIVE` ends and transitions to `ENDING`.
`OK READY` is emitted whenever firmware enters `WAIT` from another state (for example `ENDING -> WAIT`, `TOXIC -> WAIT`, `MANUTENZIONE -> WAIT`).

## 6. Formatting and Parsing Rules

- Commands `READY`, `TOXIC`, `MANUTENZIONE`:
  - Compared as exact uppercase words after trimming leading/trailing spaces.
  - Examples that work: `READY`, `  READY  `
  - Example that does not work: `ready`

- `ACTIVE`:
  - First CSV token must be exactly `ACTIVE` (uppercase).
  - Field separator is comma `,`.
  - Color field key must be `BASE` or `COLOR` (uppercase).
  - Pump segments can appear anywhere in the line and are scanned as `P<number>:<digits>`.

- Buffer limit:
  - RX line buffer is 256 bytes including terminator.
  - Keep full command lines well under 255 characters.

## 7. Host Implementation Recommendations

- Open port at `115200`, set read timeout (for example 200-1000 ms).
- After opening, optionally wait for the startup banner line.
- Send exactly one command per line, always ending with `\n`.
- Read one response line and match against known responses.
- If no response arrives, treat as possible silent ignore due to invalid state.
- On long-running operations, monitor for asynchronous `DONE`.

## 8. Test Commands

```text
READY
TOXIC
MANUTENZIONE
ACTIVE, BASE:ORANGE, P1:700, P3:1000
ACTIVE, COLOR:warm-white, p2:500
ACTIVE, BASE:BLUE, P2:0, P5:1200
TAP, RGB:255,200,40, P13
TAP OFF
TINT, RGB:255,120,0
TINT, BASE:purple
TINT OFF
```
