#pragma once

#include <Arduino.h>

#include "BoardConfig.h"

namespace ProjectConfig {

namespace Serial {
// USB serial communication speed (bps).
constexpr uint32_t kBaudRate = 115200;
// Max length (bytes) of one received serial command line.
constexpr size_t kRxBufferSize = 256;
}  // namespace Serial

namespace Pump {
// Number of pump channels supported by firmware.
constexpr uint8_t kCount = 17;
// Default PWM duty for pump activation (0-255).
constexpr uint8_t kDefaultPwm = 255;
}  // namespace Pump

namespace Strips {
// Total LED count of strip 1.
constexpr uint16_t kStrip1Len = 31;
// Total LED count of strip 2.
constexpr uint16_t kStrip2Len = 44;

// Pixels hidden by the chassis at each end of a strip. These are never lit in
// any state: the visible window is the strip minus a margin at both ends.
//
// Both margins are pinned to 0, but note this is a default rather than a proven
// requirement: a non-zero margin was suspected of causing the strip flicker
// seen on the bench and that turned out to be wrong (the real cause is the
// unshifted data line described below, and strip 1 flickered with its margin
// already at 0). Margins have not been re-tested since. Set one if you need it,
// but verify on hardware.
constexpr uint16_t kStrip1Margin = 0;
constexpr uint16_t kStrip2Margin = 0;

// A margin only applies when the strip is long enough to keep at least one
// visible pixel; otherwise it collapses to the whole strip. Without this guard
// the subtractions below wrap (they are unsigned) and index far out of bounds.
constexpr bool kStrip1MarginFits = kStrip1Len > 2 * kStrip1Margin;
constexpr bool kStrip2MarginFits = kStrip2Len > 2 * kStrip2Margin;

// First LED index used on strip 1.
constexpr uint16_t kStrip1Start = kStrip1MarginFits ? kStrip1Margin : 0;
// Last LED index used on strip 1 (inclusive).
constexpr uint16_t kStrip1End =
    kStrip1Len == 0 ? 0
                    : (kStrip1MarginFits ? (uint16_t)(kStrip1Len - 1 - kStrip1Margin)
                                         : (uint16_t)(kStrip1Len - 1));
// First LED index used on strip 2 (skip initial pixels).
constexpr uint16_t kStrip2Start = kStrip2MarginFits ? kStrip2Margin : 0;
// Last LED index used on strip 2 (inclusive, keep tail margin).
constexpr uint16_t kStrip2End =
    kStrip2Len == 0 ? 0
                    : (kStrip2MarginFits ? (uint16_t)(kStrip2Len - 1 - kStrip2Margin)
                                         : (uint16_t)(kStrip2Len - 1));

static_assert(kStrip1Len == 0 || kStrip1End < kStrip1Len,
              "strip 1 window runs past the end of the strip");
static_assert(kStrip2Len == 0 || kStrip2End < kStrip2Len,
              "strip 2 window runs past the end of the strip");
static_assert(kStrip1Len == 0 || kStrip1Start <= kStrip1End,
              "strip 1 window is inverted");
static_assert(kStrip2Len == 0 || kStrip2Start <= kStrip2End,
              "strip 2 window is inverted");
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
constexpr uint8_t kGlobalBrightness = 102;
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
// Frame interval (ms) between animation updates for each state.
// Lower value = smoother/faster motion but higher CPU load.
// Higher value = slower/choppier motion but lower CPU load.
// Approx FPS = 1000 / interval_ms.
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
  // Red channel (0-255).
  uint8_t r;
  // Green channel (0-255).
  uint8_t g;
  // Blue channel (0-255).
  uint8_t b;
};

struct NamedColor {
  // Canonical preset name accepted by ACTIVE BASE/COLOR parser.
  const char* name;
  // RGB value associated with preset name.
  Rgb rgb;
};

// General cyan color utility.
constexpr Rgb kCyan = {0, 255, 255};
// General yellow color utility.
constexpr Rgb kYellow = {255, 255, 0};
// Default WAIT/base color.
constexpr Rgb kBluDaeva = {202, 240, 248};
// STARTUP animation default color.
constexpr Rgb kStartupDefault = {255, 120, 0};
// TOXIC strobe color.
constexpr Rgb kToxicRed = {255, 0, 0};
// TOXIC chase color.
constexpr Rgb kToxicOrange = {255, 120, 0};
// TOXIC alternate pattern primary color.
constexpr Rgb kToxicMagenta = {255, 0, 255};
// Maintenance mode color.
constexpr Rgb kMaintenanceOrange = {255, 120, 0};

// Named colors accepted by ACTIVE, BASE:<name> (or COLOR:<name>).
constexpr std::array<NamedColor, 10> kPresetColors = {{
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
}};

}  // namespace Colors

}  // namespace ProjectConfig
