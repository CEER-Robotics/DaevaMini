# Arduino Mega — Serial-Controlled LEDs & WS2812 State-Machine Animations

This project controls:

- **17 discrete LEDs** (LED1…LED17) on an **Arduino Mega**
- **2 WS2812 / NeoPixel LED strips** (pins **2** and **3**)
- A **non-blocking, time-driven state machine** for LED strip animations

The system listens for **serial commands** that specify which LEDs to turn ON and for how long.  
The LED strips visually reflect the system state (waiting, active, ending).

---

## ✨ Features

- Fully **non-blocking** (`millis()`-based, no `delay()`)
- Robust serial parsing (tolerant, not strict JSON)
- Deterministic **state machine** for LED strips
- Parametric animations (speed, colors, duration)
- Clear separation between:
  - Discrete LED timing
  - WS2812 animation logic
  - Serial communication

---

## 🧠 System Overview

### Discrete LEDs
- Each LED can be switched ON for a specified duration
- Multiple LEDs can run **in parallel**
- Each LED has its own timer

### WS2812 LED Strips
- Driven by a **3-state finite state machine**
- Animations are frame-based and time-scheduled

---

## 🔁 LED Strip State Machine

### States

| State | Description |
|-----|-------------|
| `ST_START_WAIT` | Waiting for a new serial command (breathing animation) |
| `ST_ACTIVE` | Active animation while LEDs from the last command are ON |
| `ST_ENDING` | End-status animation after all LEDs turn OFF |

**State transitions**

- START_WAIT → ACTIVE: serial command received
- ACTIVE → ENDING: time ≥ longest LED duration
- ENDING → START_WAIT: time ≥ EndStatusMs

New serial commands are accepted only in ST_START_WAIT.

---

## 🔌 Hardware Setup

### Discrete LEDs (LED1…LED17)

| LED | Pin | PWM |
|----:|----:|:---:|
| LED1 | 6  | ✔ |
| LED2 | 7  | ✔ |
| LED3 | 8  | ✔ |
| LED4 | 9  | ✔ |
| LED5 | 10 | ✔ |
| LED6 | 11 | ✔ |
| LED7 | 12 | ✔ |
| LED8 | 13 | ✔ |
| LED9 | 22 | ✖ |
| LED10 | 24 | ✖ |
| LED11 | 26 | ✖ |
| LED12 | 28 | ✖ |
| LED13 | 30 | ✖ |
| LED14 | 32 | ✖ |
| LED15 | 34 | ✖ |
| LED16 | 36 | ✖ |
| LED17 | 38 | ✖ |

⚠️ **Arduino Mega PWM pins:** `2–13`, `44–46`  
Non-PWM pins behave as **ON/OFF only** with `analogWrite()`.

---

### WS2812 (NeoPixel) Strips

| Strip | Data Pin | Length |
|------:|---------:|-------:|
| Strip 1 | 2 | 16 LEDs |
| Strip 2 | 3 | 16 LEDs |

**Power notes**
- Use an external **5V supply** if brightness is high
- Connect **GND ↔ GND** (Arduino ↔ strips)
- Recommended:
  - 330–470 Ω resistor on data line
  - ≥1000 µF capacitor on 5V rail

---

## 📦 Dependencies

- **Arduino IDE** or **PlatformIO**
- **Adafruit NeoPixel** library  
  (Arduino IDE → Library Manager → search *Adafruit NeoPixel*)

---

## Serial message format

Send one line over the serial port with one or more commands in this form:

```
 P<n>:<time_ms>
```
n = LED number (1–17)

time_ms = how long the LED stays ON (milliseconds)

**Example**
```
{"P1:700","P3:1000","P5:500"}
```


This means:

LED1 ON for 700 ms

LED3 ON for 1000 ms

LED5 ON for 500 ms

 **Notes**

- You can include multiple ` P<n>:<time_ms> ` commands in the same line.

- Order does not matter.

- Extra characters like `{ } , " ` are ignored.

- The line must end with newline (Enter in Serial Monitor).
