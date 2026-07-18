#pragma once

#define DAEVA_SERIAL Serial

namespace BoardConfig {

namespace Pump {
// Provisional mapping; replace this array when the final pinout is available.
constexpr std::array<uint8_t, 17> kPins = {
    0, 1, 2, 3, 4, 5, 6, 7, 8,
    9, 10, 11, 12, 14, 15, 18, 19};

inline void configurePwm() {
  analogWriteResolution(8);
}
}  // namespace Pump

namespace Strips {
// Provisional mapping; replace these values with the final LED data pins.
constexpr uint8_t kStrip1Pin = 20;
constexpr uint8_t kStrip2Pin = 21;
}  // namespace Strips

namespace Random {
// Must remain separate from all configured pump and LED output pins.
constexpr uint8_t kAnalogSeedPin = A10;
}  // namespace Random

}  // namespace BoardConfig
