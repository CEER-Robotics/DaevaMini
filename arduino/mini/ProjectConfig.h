#pragma once

#include <Arduino.h>

namespace ProjectConfig {

namespace Serial {
constexpr uint32_t kBaudRate = 115200;
constexpr size_t kRxBufferSize = 256;
}  // namespace Serial

namespace Pump {
constexpr uint8_t kCount = 4;
constexpr uint8_t kDefaultPwm = 110;
// Arduino Nano PWM pins mapped to pumps P1..P4.
constexpr uint8_t kPins[kCount] = {3, 5, 6, 9};
}  // namespace Pump

}  // namespace ProjectConfig
