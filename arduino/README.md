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

## Board Configuration

- Due: `libraries/DaevaMax/src/boards/ArduinoDue.h`
- Teensy 4.1: `libraries/DaevaMax/src/boards/Teensy41.h`

Shared timings, colors, strip lengths, and animation settings remain in
`libraries/DaevaMax/src/ProjectConfig.h`.
