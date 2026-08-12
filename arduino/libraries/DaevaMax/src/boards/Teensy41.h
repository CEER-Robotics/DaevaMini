#pragma once

// Host link: the Pi's UART on GPIO 14/15 (TXD0/RXD0) is wired to Serial1, on
// pins 0 (RX1) and 1 (TX1) — the pins the pump mapping deliberately leaves
// free. `Serial` would be the USB CDC device, which nothing connects to in the
// assembled machine, so commands would be sent and never read.
#define DAEVA_SERIAL Serial1

namespace BoardConfig {

namespace Pump {
// Pump channels P1..P17 on the Teensy 4.1 pump controller board.
constexpr std::array<uint8_t, 17> kPins = {
    6,  7,  8,  9,  10, 11, 12, 13, 14,
    15, 18, 19, 22, 23, 24, 25, 28};

inline void configurePwm() {
  analogWriteResolution(8);
}
}  // namespace Pump

namespace Strips {
constexpr uint8_t kStrip1Pin = 2;  // LED1
constexpr uint8_t kStrip2Pin = 3;  // LED2

// Present on the controller board but not driven by the current firmware.
constexpr uint8_t kLed3Pin = 4;
constexpr uint8_t kRoundLedPin = 5;
}  // namespace Strips

namespace Random {
// Must remain separate from all configured pump and LED output pins.
constexpr uint8_t kAnalogSeedPin = A12;  // Unconnected pin 26
}  // namespace Random

}  // namespace BoardConfig
