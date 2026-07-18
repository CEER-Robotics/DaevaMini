#pragma once

#include <Arduino.h>
#include <array>

#if defined(ARDUINO_SAM_DUE)
#include "boards/ArduinoDue.h"
#elif defined(ARDUINO_TEENSY41)
#include "boards/Teensy41.h"
#else
#error "Daeva MAX supports only Arduino Due and Teensy 4.1."
#endif
