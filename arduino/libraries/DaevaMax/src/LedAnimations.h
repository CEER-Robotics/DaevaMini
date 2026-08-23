#pragma once

#include <Arduino.h>

namespace LedAnimations {

void begin();
void setStartupColor(uint8_t r, uint8_t g, uint8_t b);
void startActive(uint32_t activeUntilMs);
bool startActiveWithColor(uint32_t activeUntilMs, uint32_t color);
// Tap mode: extend a pour already running, or end it now. Neither re-runs the
// colour fade-in, so the animation carries on undisturbed.
bool extendActive(uint32_t activeUntilMs);
bool finishActiveNow();
// Recolors the idle strips (for example orange while the settings screens are
// open) and drops back to the default idle color. Both only apply in WAIT and
// return false anywhere else.
bool setIdleTint(uint32_t color);
bool clearIdleTint();
bool handleReadyCommand();
bool handleMaintenanceCommand();
bool handleToxicCommand();
bool parsePresetColor(const char* name, uint32_t& outColor);
bool update();
bool isWaitingForCommand();

}  // namespace LedAnimations
