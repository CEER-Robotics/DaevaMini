#pragma once

#include <Arduino.h>

namespace PumpControl {

void begin();
void allOff();
void update();
uint32_t scheduleFromLine(const char* line);

}  // namespace PumpControl
