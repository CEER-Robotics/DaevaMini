# Teensy 4.1 Daeva MAX Firmware

This thin Teensy 4.1 sketch uses the same `DaevaMax` library as the Arduino Due
target. Only the board profile differs.

## Arduino IDE setup

- Board: `Teensy 4.1`
- USB Type: `Serial`
- Dependency: `Adafruit NeoPixel`
- Sketch: `teensy_max.ino`

See `../README.md` for local library setup.

The host protocol remains at 115200 baud. Teensy USB serial ignores the physical
baud rate, but the value is retained for compatibility with the host settings.

## Provisional pinout

The current mapping is intentionally temporary:

| Function | Teensy 4.1 pins |
| --- | --- |
| Pumps P1-P17 | `0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 14, 15, 18, 19` |
| WS2812 strip 1 | `20` |
| WS2812 strip 2 | `21` |
| Unconnected RNG input | `A10` / pin `24` |

Change only `../libraries/DaevaMax/src/boards/Teensy41.h` when the final pinout
is available. Pump pins must support hardware PWM and no pin may be assigned to
more than one function.

Pump PWM is explicitly configured for 8-bit resolution so the existing
`kDefaultPwm` range of 0-255 behaves as intended on Teensy.

## Electrical constraints

Teensy 4.1 GPIO uses 3.3 V logic and is not 5 V tolerant. Pump outputs must
drive suitable external switching hardware rather than pump loads directly. Use
a 3.3 V-compatible level shifter for 5 V WS2812 data when required by the LED
wiring and power arrangement.

See `../libraries/DaevaMax/SERIAL_PROTOCOL.md` for the host command and response
contract.
