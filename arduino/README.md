# Daeva MAX Arduino Sketchbook

The Arduino Due and Teensy 4.1 MAX targets share the firmware in
`libraries/DaevaMax`. Their sketches contain only Arduino `setup()` and `loop()`
entry points; hardware differences live under `libraries/DaevaMax/src/boards`.

## Arduino IDE

Set the Arduino IDE sketchbook location to this `arduino` directory. The IDE
will then discover `libraries/DaevaMax` automatically. Install the Adafruit
NeoPixel dependency through Library Manager and open either:

- `main/main.ino` for Arduino Due
- `teensy_max/teensy_max.ino` for Teensy 4.1

## Arduino CLI

Pass the repository library directory explicitly:

```powershell
arduino-cli compile --libraries arduino/libraries --fqbn arduino:sam:arduino_due_x arduino/main

arduino-cli compile --libraries arduino/libraries `
  --fqbn "teensy:avr:teensy41:usb=serial,speed=600,opt=o2std,keys=en-us" `
  arduino/teensy_max
```

## Uploading to the Teensy

Flash over USB (the GPIO 14/15 UART is the host link, not a programming
interface — see `teensy_max/README.md`):

```powershell
arduino-cli compile --upload -p "Port_#0009.Hub_#0002" --protocol teensy `
  --libraries arduino/libraries `
  --fqbn "teensy:avr:teensy41:usb=serial,speed=600,opt=o2std,keys=en-us" `
  arduino/teensy_max
```

Two things that make an upload fail even though the sketch compiles:

- **The Teensy Loader has to be running.** `teensy_post_compile` talks to it over
  localhost, and without it the upload dies with *"Unable find Teensy Loader. Is
  the Teensy Loader application running?"*. Start
  `%LOCALAPPDATA%\Arduino15\packages\teensy\tools\teensy-tools\<ver>\teensy.exe`
  first; the Arduino IDE does this for you.
- **Use the `teensy` protocol port, not the COM port.** `-p COM6` gets you
  *"Teensy should be selected from teensy ports rather than Serial ports"* and
  the upload silently does nothing while still exiting 0. Get the right port name
  from `arduino-cli board list` — the row whose protocol is `teensy`.

## Board Configuration

- Due: `libraries/DaevaMax/src/boards/ArduinoDue.h`
- Teensy 4.1: `libraries/DaevaMax/src/boards/Teensy41.h`

Shared timings, colors, strip lengths, and animation settings remain in
`libraries/DaevaMax/src/ProjectConfig.h`.
