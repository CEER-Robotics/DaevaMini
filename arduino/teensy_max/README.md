# Teensy 4.1 Daeva MAX Firmware

This thin Teensy 4.1 sketch uses the same `DaevaMax` library as the Arduino Due
target. Only the board profile differs.

## Arduino IDE setup

- Board: `Teensy 4.1`
- USB Type: `Serial`
- Dependency: `Adafruit NeoPixel`
- Sketch: `teensy_max.ino`

See `../README.md` for local library setup.

## Host link

The host is a Raspberry Pi 5 talking over its **GPIO 14/15 UART** (`/dev/ttyAMA0`),
not USB, so the firmware uses `Serial1` — pins `0` (RX1) and `1` (TX1). Wire it
crossed, with a common ground:

| Raspberry Pi 5 | Teensy 4.1 |
| --- | --- |
| GPIO 14 / TXD0 (header pin 8) | pin `0` — RX1 |
| GPIO 15 / RXD0 (header pin 10) | pin `1` — TX1 |
| GND (header pin 6) | GND |

Both sides are 3.3 V logic, so no level shifting is needed on this link.

115200 baud now matters on both ends: unlike USB CDC, a hardware UART really
does run at the configured rate, and a mismatch shows up as garbage bytes
rather than a clean failure. The Pi side is set up by `rpi/setup-uart.sh` — the
UART is off by default on a Pi 5 and the OS puts a serial console on it, both of
which have to be dealt with before the board can be reached. `USB Type: Serial`
in the IDE is still fine; it just becomes the programming/debug path instead of
the host link.

### Known issue: RX bytes are lost during LED refreshes

`Adafruit_NeoPixel::show()` disables interrupts while it bit-bangs the strips
(~1.7 ms per strip, every animation frame). The LPUART FIFO holds 4 bytes — about
350 µs at 115200 — so a host command that is still arriving when a refresh starts
loses bytes. Measured on the bench: 0/12 commands lost at 27 bytes, but 4/12 at
76 bytes and 5/12 at 132 bytes. The corrupted line then fails `isActiveCommand`
and `handleSerialLine` returns **without replying**, so the host cannot tell a
dropped command from a dead link.

This never appeared over USB, where the CDC endpoint buffers in hardware. The
host currently works around it by resending unacknowledged `ACTIVE` commands
(safe: the `WAIT` gate means a resend answers `IGNORED ACTIVE` and schedules
nothing). Fixing it properly here means not blocking interrupts — `WS2812Serial`
or FastLED's Teensy 4 DMA driver — or hardware flow control. Note that lowering
the baud rate makes it worse, not better: the blackout is fixed-length, so
slower bytes spend longer exposed to it.

Worth doing regardless: reply with an error for unparseable lines, so a dropped
command is visible instead of silent.

## Pinout

The firmware uses the pump-controller board mapping:

| Function | Teensy 4.1 pins |
| --- | --- |
| Pumps P1-P17 | `6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 18, 19, 22, 23, 24, 25, 28` |
| WS2812 strip 1 / LED1 | `2` |
| WS2812 strip 2 / LED2 | `3` |
| LED3, currently unused | `4` |
| LED_ROUND, currently unused | `5` |
| Serial1 RX/TX | `0`, `1` |
| Unconnected RNG input | `A12` / pin `26` |

The board-specific mapping is centralized in
`../libraries/DaevaMax/src/boards/Teensy41.h`.

Pump PWM is explicitly configured for 8-bit resolution so the existing
`kDefaultPwm` range of 0-255 behaves as intended on Teensy.

## Electrical constraints

Teensy 4.1 GPIO uses 3.3 V logic and is not 5 V tolerant. Pump outputs must
drive suitable external switching hardware rather than pump loads directly. Use
a 3.3 V-compatible level shifter for 5 V WS2812 data when required by the LED
wiring and power arrangement.

See `../libraries/DaevaMax/SERIAL_PROTOCOL.md` for the host command and response
contract.
