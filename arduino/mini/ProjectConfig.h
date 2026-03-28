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

namespace Strips {
// Data pin for WS2812 strip 1.
constexpr uint8_t kStrip1Pin = 10;
// Data pin for WS2812 strip 2.
constexpr uint8_t kStrip2Pin = 11;
// Total LED count of strip 1.
constexpr uint16_t kStrip1Len = 13;
// Total LED count of strip 2.
constexpr uint16_t kStrip2Len = 18;

// First LED index used on strip 1.
constexpr uint16_t kStrip1Start = 0;
// Last LED index used on strip 1 (inclusive).
constexpr uint16_t kStrip1End = kStrip1Len - 1;
// First LED index used on strip 2.
constexpr uint16_t kStrip2Start = 0;
// Last LED index used on strip 2 (inclusive).
constexpr uint16_t kStrip2End = kStrip2Len - 1;
}  // namespace Strips

namespace Timing {
// Duration of STARTUP state before entering WAIT.
constexpr uint32_t kStartupDurationMs = 10000;
// Inactivity timeout in WAIT before auto-entering TOXIC.
// Set to -1 to disable automatic TOXIC and allow only serial command activation.
constexpr int32_t kToxicTimeoutMs = -1;

// Number of flashes shown in ENDING state.
constexpr uint8_t kEndFlashCount = 5;
// Duration of one ENDING flash cycle (on+off).
constexpr uint32_t kEndFlashPeriodMs = 500;
// ON ratio numerator for ENDING flash duty cycle.
constexpr uint8_t kEndFlashOnPart = 60;
// OFF ratio numerator for ENDING flash duty cycle.
constexpr uint8_t kEndFlashOffPart = 40;

// How long each TOXIC sub-pattern runs before switching.
constexpr uint32_t kToxicPatternDurationMs = 1800;
}  // namespace Timing

namespace Animation {
// Active animation selector: 1 chase, 2 dual-breathe, 3 bounce.
constexpr uint8_t kActiveAnim = 3;
// Global strip brightness applied at LED driver level (0-255).
constexpr uint8_t kGlobalBrightness = 255;
// Accent brightness used by active animation 1 (0-255).
constexpr uint8_t kActiveAccentBrightness = 120;
// Brightness step per frame for WAIT breathing effect.
constexpr int16_t kWaitBreatheStep = 10;
// Brightness step per frame for maintenance breathing effect.
constexpr int16_t kMaintenanceBreatheStep = 10;
// Half-period of bounce motion in active animation 3.
constexpr uint32_t kBounceHalfPeriodMs = 1000;
// Half-period for TOXIC strobe blink.
constexpr uint32_t kToxicStrobeHalfPeriodMs = 120;
// Half-period for TOXIC alternate checker pattern.
constexpr uint32_t kToxicAlternateHalfPeriodMs = 180;
// TOXIC animation mode: 1 = random toxic patterns, 2 = slow breathe in WAIT color.
constexpr uint8_t kToxicAnim = 2;
// Brightness step per TOXIC frame when kToxicAnim == 2 (lower = slower).
constexpr int16_t kToxicBreatheStep = 5;
// STARTUP update interval (~20 FPS).
constexpr uint32_t kFrameStartupMs = 50;
// WAIT update interval (~25 FPS).
constexpr uint32_t kFrameWaitMs = 40;
// TOXIC update interval (~20 FPS).
constexpr uint32_t kFrameToxicMs = 50;
// MAINTENANCE update interval (~22 FPS).
constexpr uint32_t kFrameMaintenanceMs = 45;
// ACTIVE update interval (~30 FPS).
constexpr uint32_t kFrameActiveMs = 33;
// ENDING update interval (~50 FPS).
constexpr uint32_t kFrameEndingMs = 20;
}  // namespace Animation

namespace Colors {

struct Rgb {
  uint8_t r;
  uint8_t g;
  uint8_t b;
};

struct NamedColor {
  const char* name;
  Rgb rgb;
};

constexpr Rgb kCyan = {0, 255, 255};
constexpr Rgb kYellow = {255, 255, 0};
constexpr Rgb kBluDaeva = {202, 240, 248};
constexpr Rgb kStartupDefault = {255, 120, 0};
constexpr Rgb kToxicRed = {255, 0, 0};
constexpr Rgb kToxicOrange = {255, 120, 0};
constexpr Rgb kToxicMagenta = {255, 0, 255};
constexpr Rgb kMaintenanceOrange = {255, 120, 0};

constexpr uint8_t kPresetColorCount = 10;
constexpr NamedColor kPresetColors[kPresetColorCount] = {
    {"ORANGE", {120, 255, 0}},
    {"RED", {255, 0, 0}},
    {"GREEN", {0, 255, 0}},
    {"BLUE", {0, 0, 255}},
    {"CYAN", {0, 255, 255}},
    {"MAGENTA", {255, 0, 255}},
    {"YELLOW", {255, 255, 0}},
    {"WHITE", {255, 255, 255}},
    {"WARM_WHITE", {255, 180, 110}},
    {"PURPLE", {128, 0, 255}},
};

}  // namespace Colors

}  // namespace ProjectConfig
