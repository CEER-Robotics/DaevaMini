#pragma once

#define DAEVA_SERIAL SerialUSB

namespace BoardConfig {

namespace Pump {
constexpr std::array<uint8_t, 17> kPins = {
    6,  7,  8,  9,  10, 11, 12, 13, 22,
    24, 26, 28, 30, 32, 34, 36, 38};

inline void configurePwm() {}
}  // namespace Pump

namespace Strips {
constexpr uint8_t kStrip1Pin = 2;
constexpr uint8_t kStrip2Pin = 3;
}  // namespace Strips

namespace Random {
constexpr uint8_t kAnalogSeedPin = A0;
}  // namespace Random

}  // namespace BoardConfig
