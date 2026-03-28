#pragma once

#include <Arduino.h>

namespace LedAnimations {

void begin();
void setStartupColor(uint8_t r, uint8_t g, uint8_t b);
void startActive(uint32_t activeUntilMs);
bool startActiveWithColor(uint32_t activeUntilMs, uint32_t color);
bool handleReadyCommand();
bool handleMaintenanceCommand();
bool handleToxicCommand();
bool parsePresetColor(const char* name, uint32_t& outColor);
bool update();
bool isWaitingForCommand();

}  // namespace LedAnimations
