#include "LedAnimations.h"
#include "ProjectConfig.h"
#include "LedStateMachine.h"

#include <Adafruit_NeoPixel.h>
#include <array>
#include <ctype.h>
#include <string.h>

namespace {

Adafruit_NeoPixel strip1(ProjectConfig::Strips::kStrip1Len,
                         BoardConfig::Strips::kStrip1Pin,
                         NEO_GRB + NEO_KHZ800);
Adafruit_NeoPixel strip2(ProjectConfig::Strips::kStrip2Len,
                         BoardConfig::Strips::kStrip2Pin,
                         NEO_GRB + NEO_KHZ800);

// The visible span of a strip. Pixels outside it are hidden by the chassis, so
// no animation may light them: every effect addresses a window, never the raw
// strip. Indices are absolute (they index the driver directly) and `len` is 0
// when the strip is empty, which every caller has to tolerate.
struct Window {
  uint16_t start;
  uint16_t end;  // inclusive
  uint16_t len;
};

// Clamps the configured window to the pixels the driver actually allocated, so
// a mis-set length in ProjectConfig.h cannot make us write out of bounds.
static Window makeWindow(uint16_t start, uint16_t end, uint16_t physicalLen) {
  if (physicalLen == 0 || start >= physicalLen || end < start) {
    return Window{0, 0, 0};
  }
  const uint16_t last = (end < physicalLen) ? end : (uint16_t)(physicalLen - 1);
  return Window{start, last, (uint16_t)(last - start + 1)};
}

const Window win1 = makeWindow(ProjectConfig::Strips::kStrip1Start,
                               ProjectConfig::Strips::kStrip1End,
                               ProjectConfig::Strips::kStrip1Len);
const Window win2 = makeWindow(ProjectConfig::Strips::kStrip2Start,
                               ProjectConfig::Strips::kStrip2End,
                               ProjectConfig::Strips::kStrip2Len);

// Lights every pixel of the window and leaves the hidden margins dark.
static void fillWindow(Adafruit_NeoPixel& s, const Window& w, uint32_t color) {
  s.clear();
  for (uint16_t i = 0; i < w.len; i++) {
    s.setPixelColor((uint16_t)(w.start + i), color);
  }
}

// Maps a free-running counter onto the window, so chases wrap inside the
// visible span instead of running through the hidden margins.
static uint16_t windowIndex(const Window& w, uint32_t pos) {
  if (w.len == 0) {
    return 0;
  }
  return (uint16_t)(w.start + (uint16_t)(pos % w.len));
}

uint32_t CYAN = 0;
uint32_t YELL = 0;
uint32_t BLU_DAEVA = 0;
uint32_t STARTUP_COLOR = 0;
uint32_t TOXIC_RED = 0;
uint32_t TOXIC_ORANGE = 0;
uint32_t TOXIC_MAGENTA = 0;
uint32_t MAINT_ORANGE = 0;

static uint32_t activeColor = 0;
static bool activeColorValid = false;
static uint32_t endingColor = 0;

static uint32_t nextFrameAt = 0;

static uint16_t chasePos = 0;

static int16_t breathePhase = 0;
static int16_t breatheStep = ProjectConfig::Animation::kWaitBreatheStep;

static int16_t maintPhase = 0;
static int16_t maintStep = ProjectConfig::Animation::kMaintenanceBreatheStep;

static uint32_t bounceStartMs = 0;
static uint32_t bounceDurationMs = 0;
static uint32_t bounceHalfPeriodMs = ProjectConfig::Animation::kBounceHalfPeriodMs;

static uint16_t upA = 0;
static uint16_t upB = ProjectConfig::Strips::kStrip1End;
static uint16_t dnA = ProjectConfig::Strips::kStrip2Start;
static uint16_t dnB = ProjectConfig::Strips::kStrip2End;
static uint32_t upCol = 0;
static uint32_t dnCol = 0;
static uint32_t startupAnimStartMs = 0;
static uint32_t endAnimStartMs = 0;

// WAIT is normally one flat color, but the host can tint it with the color of a
// drink the guest is about to confirm. Both ends of the crossfade are kept so a
// second preview arriving mid-fade starts from what is actually on the strips.
static uint32_t waitColorFrom = 0;
static uint32_t waitColorTarget = 0;
static uint32_t waitFadeStartMs = 0;
static bool waitFading = false;
// True while the host is holding a non-default idle color. The attract beat
// stays out of the way then: a tinted machine is a machine somebody is using.
static bool idleTinted = false;
// Last time the machine did something on purpose. Drives the attract delay.
static uint32_t lastActivityMs = 0;

// Start of the crossfade from the idle color into the drink's color, and the
// color it started from.
static uint32_t activeFadeStartMs = 0;
static uint32_t activeFadeFromColor = 0;
static uint32_t activePulseStartMs = 0;

enum ToxicPattern : uint8_t {
  TOXIC_STROBE = 0,
  TOXIC_CHASE = 1,
  TOXIC_ALTERNATE = 2
};
static ToxicPattern toxicPattern = TOXIC_STROBE;
static uint32_t toxicPatternStartMs = 0;
static uint16_t toxicChasePos = 0;
static int16_t toxicBreathePhase = 0;
static int16_t toxicBreatheStep = ProjectConfig::Animation::kToxicBreatheStep;

static uint32_t endStatusDurationMs() {
  const uint32_t total = ProjectConfig::Timing::kEndHoldDurationMs +
                         ProjectConfig::Timing::kEndFadeDurationMs;
  return (total == 0) ? 1 : total;
}

static uint32_t scaleColor(Adafruit_NeoPixel& s, uint32_t c, uint8_t b) {
  uint8_t r = (uint8_t)(c >> 16);
  uint8_t g = (uint8_t)(c >> 8);
  uint8_t bl = (uint8_t)(c >> 0);
  r = (uint8_t)((uint16_t)r * b / 255);
  g = (uint8_t)((uint16_t)g * b / 255);
  bl = (uint8_t)((uint16_t)bl * b / 255);
  return s.Color(r, g, bl);
}

static uint8_t breatheUpdateGetBrightness() {
  breathePhase += breatheStep;

  if (breathePhase >= 255) {
    breathePhase = 255;
    breatheStep = -abs(breatheStep);
  } else if (breathePhase <= 0) {
    breathePhase = 0;
    breatheStep = abs(breatheStep);
  }
  return (uint8_t)breathePhase;
}

static uint8_t maintenanceUpdateBrightness() {
  maintPhase += maintStep;

  if (maintPhase >= 255) {
    maintPhase = 255;
    maintStep = -abs(maintStep);
  } else if (maintPhase <= 0) {
    maintPhase = 0;
    maintStep = abs(maintStep);
  }
  return (uint8_t)maintPhase;
}

// Last frame pushed by staticFill, and whether it is still what the strips are
// showing. Any animation that writes pixels itself must invalidate this.
static uint32_t lastStaticColor = 0;
static bool staticFrameValid = false;

static void invalidateStaticFrame() { staticFrameValid = false; }

// Re-sending a frame the strips are already displaying achieves nothing, and on
// a data line driven at 3.3 V (below the WS2812 0.7 x VDD threshold, no level
// shifter fitted) every refresh is another chance to latch a corrupted bit and
// flicker a pixel. Static states therefore transmit only when the image really
// changes, which for WAIT means once on entry rather than 25 times a second.
static void staticFill(uint32_t color) {
  if (staticFrameValid && color == lastStaticColor) {
    return;
  }

  fillWindow(strip1, win1, color);
  fillWindow(strip2, win2, color);

  strip1.show();
  strip2.show();

  lastStaticColor = color;
  staticFrameValid = true;
}

// Mixes two packed colors channel by channel. t is 0..255, 0 = a, 255 = b.
static uint32_t lerpColor(Adafruit_NeoPixel& s, uint32_t a, uint32_t b, uint8_t t) {
  const uint16_t inv = (uint16_t)(255 - t);
  const uint8_t r = (uint8_t)(((uint16_t)(uint8_t)(a >> 16) * inv +
                               (uint16_t)(uint8_t)(b >> 16) * t) / 255);
  const uint8_t g = (uint8_t)(((uint16_t)(uint8_t)(a >> 8) * inv +
                               (uint16_t)(uint8_t)(b >> 8) * t) / 255);
  const uint8_t bl = (uint8_t)(((uint16_t)(uint8_t)a * inv +
                                (uint16_t)(uint8_t)b * t) / 255);
  return s.Color(r, g, bl);
}

// Brightness of the attract beat: two quick thumps and then a long rest, so it
// reads as a heartbeat rather than as a blink or an alarm. Returns full
// brightness outside the thumps, which lets the resting part of the cycle go
// through the frame cache untouched.
static uint8_t attractBrightness(uint32_t phaseMs) {
  const uint8_t lo = ProjectConfig::Animation::kAttractMinBrightness;
  const uint8_t hi = ProjectConfig::Animation::kAttractMaxBrightness;
  if (hi <= lo) {
    return hi;
  }

  const uint32_t thumpMs = (ProjectConfig::Animation::kAttractThumpMs == 0)
                               ? 1
                               : ProjectConfig::Animation::kAttractThumpMs;
  const uint32_t secondAt = thumpMs + ProjectConfig::Animation::kAttractGapMs;

  // Where we are inside whichever thump we are in, if any.
  uint32_t into = 0;
  if (phaseMs < thumpMs) {
    into = phaseMs;
  } else if (phaseMs >= secondAt && phaseMs < secondAt + thumpMs) {
    into = phaseMs - secondAt;
  } else {
    return hi;  // resting between beats
  }

  // A thump dips away from full brightness and comes straight back, so the
  // strips look like they are pulsing rather than flickering off.
  const float x = (float)into / (float)thumpMs;
  const float dip = 1.0f - (4.0f * x * (1.0f - x));
  return (uint8_t)(lo + (uint8_t)((float)(hi - lo) * dip + 0.5f));
}

// Renders WAIT, easing into a new color whenever the host changes it. Once the
// fade lands this settles back to a single staticFill, which the frame cache
// then stops re-transmitting.
static void waitAnimStep(uint32_t now) {
  if (!waitFading) {
    // Untouched for long enough, and not tinted: start beating to be noticed.
    const bool attract =
        !idleTinted &&
        (uint32_t)(now - lastActivityMs) >=
            ProjectConfig::Animation::kIdleAttractDelayMs;
    if (attract) {
      const uint32_t cycleMs = (ProjectConfig::Animation::kAttractCycleMs == 0)
                                   ? 1
                                   : ProjectConfig::Animation::kAttractCycleMs;
      const uint8_t b = attractBrightness((now - lastActivityMs) % cycleMs);
      staticFill(scaleColor(strip1, waitColorTarget, b));
      return;
    }

    staticFill(waitColorTarget);
    return;
  }

  const uint32_t fadeMs = (ProjectConfig::Animation::kIdleTintFadeMs == 0)
                              ? 1
                              : ProjectConfig::Animation::kIdleTintFadeMs;
  uint32_t elapsed = now - waitFadeStartMs;
  if (elapsed >= fadeMs) {
    waitFading = false;
    waitColorFrom = waitColorTarget;
    staticFill(waitColorTarget);
    return;
  }

  const float x = (float)elapsed / (float)fadeMs;
  const float eased = x * x * (3.0f - 2.0f * x);
  const uint8_t t = (uint8_t)(eased * 255.0f + 0.5f);
  staticFill(lerpColor(strip1, waitColorFrom, waitColorTarget, t));
}

// The color WAIT is showing right now, fade included: the starting point for
// any new crossfade.
static uint32_t currentWaitColor(uint32_t now) {
  if (!waitFading) {
    return waitColorTarget;
  }
  const uint32_t fadeMs = (ProjectConfig::Animation::kIdleTintFadeMs == 0)
                              ? 1
                              : ProjectConfig::Animation::kIdleTintFadeMs;
  uint32_t elapsed = now - waitFadeStartMs;
  if (elapsed >= fadeMs) {
    return waitColorTarget;
  }
  const float x = (float)elapsed / (float)fadeMs;
  const float eased = x * x * (3.0f - 2.0f * x);
  return lerpColor(strip1, waitColorFrom, waitColorTarget,
                   (uint8_t)(eased * 255.0f + 0.5f));
}

static void beginWaitFadeTo(uint32_t color) {
  const uint32_t now = millis();
  lastActivityMs = now;
  if (!waitFading && color == waitColorTarget) {
    return;
  }
  waitColorFrom = currentWaitColor(now);
  waitColorTarget = color;
  waitFadeStartMs = now;
  waitFading = true;
}

static uint32_t currentActiveColor() {
  return activeColorValid ? activeColor : BLU_DAEVA;
}

static void activeAnim1Step(uint32_t color) {
  invalidateStaticFrame();
  strip1.clear();
  strip2.clear();

  const uint32_t accent = scaleColor(
      strip1, color, ProjectConfig::Animation::kActiveAccentBrightness);

  if (win1.len > 0) {
    strip1.setPixelColor(windowIndex(win1, chasePos), color);
    strip1.setPixelColor(windowIndex(win1, chasePos + win1.len / 2), accent);
  }
  if (win2.len > 0) {
    strip2.setPixelColor(windowIndex(win2, chasePos), accent);
    strip2.setPixelColor(windowIndex(win2, chasePos + win2.len / 3), color);
  }

  strip1.show();
  strip2.show();

  chasePos++;
}

static void activeAnim2Step(uint32_t color) {
  invalidateStaticFrame();
  const uint8_t up = breatheUpdateGetBrightness();
  const uint8_t dn = (uint8_t)(255 - breathePhase);

  const uint32_t c1 = scaleColor(strip1, color, up);
  const uint32_t c2 = scaleColor(strip2, color, dn);

  fillWindow(strip1, win1, c1);
  fillWindow(strip2, win2, c2);

  strip1.show();
  strip2.show();
}

// Slow breath over both strips in the drink color. Driven by wall time rather
// than by a per-frame counter, so the cadence stays the same whatever
// kFrameActiveMs is set to and a dropped frame does not stretch the breath.
static uint8_t activePulseBrightness(uint32_t now) {
  const uint32_t periodMs = (ProjectConfig::Animation::kActivePulsePeriodMs == 0)
                                ? 1
                                : ProjectConfig::Animation::kActivePulsePeriodMs;
  const uint32_t phaseMs = (now - activePulseStartMs) % periodMs;
  const uint32_t halfMs = (periodMs / 2 == 0) ? 1 : (periodMs / 2);

  // Triangle 0..1..0 across the period, then smoothstep so the turnaround at
  // full and at minimum is soft instead of a visible corner.
  const float tri = (phaseMs < halfMs)
                        ? ((float)phaseMs / (float)halfMs)
                        : ((float)(periodMs - phaseMs) / (float)(periodMs - halfMs));
  const float eased = tri * tri * (3.0f - 2.0f * tri);

  const uint8_t lo = ProjectConfig::Animation::kActivePulseMinBrightness;
  const uint8_t hi = ProjectConfig::Animation::kActivePulseMaxBrightness;
  if (hi <= lo) {
    return hi;
  }
  return (uint8_t)(lo + (uint8_t)(((float)(hi - lo) * eased) + 0.5f));
}

// Goes through staticFill rather than painting pixels itself, so it inherits
// the frame cache: near the top and bottom of the breath consecutive frames
// scale to the same color and are not re-transmitted, which is exactly where
// the unshifted data line is most likely to latch a corrupted bit.
static void activeAnim4Step(uint32_t color, uint32_t now) {
  const uint32_t fadeMs = ProjectConfig::Animation::kActiveFadeInMs;
  const uint32_t elapsed = now - activeFadeStartMs;
  if (elapsed < fadeMs) {
    // Carry the idle color over into the drink's color at full brightness. The
    // breathing only starts once the color has arrived.
    const float x = (float)elapsed / (float)fadeMs;
    const float eased = x * x * (3.0f - 2.0f * x);
    staticFill(lerpColor(strip1, activeFadeFromColor, color,
                         (uint8_t)(eased * 255.0f + 0.5f)));
    return;
  }

  staticFill(scaleColor(strip1, color, activePulseBrightness(now)));
}

// Holds the drink's color steady, then eases it into the WAIT color. The fade
// is timed to finish exactly when ENDING expires, so entering WAIT paints the
// color the strips are already showing and the transition is seamless.
// Going through staticFill means the steady part transmits once, not every
// frame, and the fade only transmits when the mixed color actually changes.
static void endHoldStep(uint32_t now) {
  const uint32_t elapsed = now - endAnimStartMs;
  const uint32_t holdMs = ProjectConfig::Timing::kEndHoldDurationMs;
  if (elapsed < holdMs) {
    staticFill(endingColor);
    return;
  }

  const uint32_t fadeMs = (ProjectConfig::Timing::kEndFadeDurationMs == 0)
                              ? 1
                              : ProjectConfig::Timing::kEndFadeDurationMs;
  uint32_t into = elapsed - holdMs;
  if (into > fadeMs) {
    into = fadeMs;
  }

  // Smoothstep so the fade leaves the drink color and settles into white
  // gently at both ends, instead of starting and stopping abruptly.
  const float x = (float)into / (float)fadeMs;
  const float eased = x * x * (3.0f - 2.0f * x);
  const uint8_t t = (uint8_t)(eased * 255.0f + 0.5f);
  staticFill(lerpColor(strip1, endingColor, waitColorTarget, t));
}

static uint16_t clampU16(int32_t v, uint16_t lo, uint16_t hi) {
  if (v < (int32_t)lo) {
    return lo;
  }
  if (v > (int32_t)hi) {
    return hi;
  }
  return (uint16_t)v;
}

static uint16_t lerpIndexU16(uint16_t a, uint16_t b, float t) {
  float v = (float)a + ((float)b - (float)a) * t;
  int32_t vi = (int32_t)(v + 0.5f);
  uint16_t lo = (a < b) ? a : b;
  uint16_t hi = (a > b) ? a : b;
  return clampU16(vi, lo, hi);
}

static void bounceAnimBegin(uint32_t durationMs,
                            uint32_t halfPeriodMs,
                            uint16_t strip1Start,
                            uint16_t strip1End,
                            uint32_t strip1Color,
                            uint16_t strip2Start,
                            uint16_t strip2End,
                            uint32_t strip2Color) {
  invalidateStaticFrame();
  bounceStartMs = millis();
  bounceDurationMs = (durationMs == 0) ? 1 : durationMs;
  bounceHalfPeriodMs = halfPeriodMs;

  // Clamp the requested span into the visible window rather than wrapping it:
  // a modulo here would land the bounce endpoints inside the hidden margins.
  upA = clampU16(strip1Start, win1.start, win1.end);
  upB = clampU16(strip1End, win1.start, win1.end);
  dnA = clampU16(strip2Start, win2.start, win2.end);
  dnB = clampU16(strip2End, win2.start, win2.end);

  upCol = strip1Color;
  dnCol = strip2Color;

  strip1.clear();
  strip1.show();
  strip2.clear();
  strip2.show();
}

static void bounceAnimStep() {
  invalidateStaticFrame();
  const uint32_t now = millis();
  uint32_t dt = now - bounceStartMs;
  if (dt > bounceDurationMs) {
    dt = bounceDurationMs;
  }

  const uint32_t cycleMs = 2u * bounceHalfPeriodMs;
  const uint32_t ph = (cycleMs > 0) ? (dt % cycleMs) : 0;
  const float t = (bounceHalfPeriodMs > 0)
                      ? ((float)(ph % bounceHalfPeriodMs) / (float)bounceHalfPeriodMs)
                      : 1.0f;

  const bool forward = (ph < bounceHalfPeriodMs);
  const uint16_t pUp =
      forward ? lerpIndexU16(upA, upB, t) : lerpIndexU16(upB, upA, t);
  const uint16_t pDn =
      forward ? lerpIndexU16(dnA, dnB, t) : lerpIndexU16(dnB, dnA, t);

  strip1.clear();
  strip2.clear();

  if (win1.len > 0) {
    strip1.setPixelColor(pUp, upCol);
  }
  if (win2.len > 0) {
    strip2.setPixelColor(pDn, dnCol);
  }

  strip1.show();
  strip2.show();
}

static void startupAnimStep() {
  invalidateStaticFrame();
  const uint32_t now = millis();
  uint32_t elapsed = now - startupAnimStartMs;
  const uint32_t durationMs = (ProjectConfig::Timing::kStartupDurationMs == 0)
                                  ? 1
                                  : ProjectConfig::Timing::kStartupDurationMs;
  if (elapsed > durationMs) {
    elapsed = durationMs;
  }

  const uint16_t strip1Len = win1.len;
  const uint16_t strip2Len = win2.len;
  const uint16_t strip1Steps = (uint16_t)((strip1Len + 1) / 2);
  const uint16_t strip2Steps = (uint16_t)((strip2Len + 1) / 2);
  const uint16_t totalSteps = (strip1Steps > strip2Steps) ? strip1Steps : strip2Steps;

  uint16_t litLevel = 0;
  if (totalSteps > 0 && elapsed > 0) {
    litLevel =
        (uint16_t)(((uint64_t)elapsed * totalSteps + durationMs - 1) / durationMs);
    if (litLevel > totalSteps) {
      litLevel = totalSteps;
    }
  }

  // Guarded against an empty window: `strip1Len - 1` is unsigned and would wrap.
  const uint16_t strip1MidLeft =
      (strip1Len > 0) ? (uint16_t)(win1.start + ((strip1Len - 1) / 2)) : 0;
  const uint16_t strip1MidRight =
      (strip1Len > 0) ? (uint16_t)(win1.start + (strip1Len / 2)) : 0;
  const uint16_t strip2MidLeft =
      (strip2Len > 0) ? (uint16_t)(win2.start + ((strip2Len - 1) / 2)) : 0;
  const uint16_t strip2MidRight =
      (strip2Len > 0) ? (uint16_t)(win2.start + (strip2Len / 2)) : 0;

  strip1.clear();
  strip2.clear();

  const uint16_t strip1Lit = (litLevel > strip1Steps) ? strip1Steps : litLevel;
  const uint16_t strip2Lit = (litLevel > strip2Steps) ? strip2Steps : litLevel;

  for (uint16_t i = 0; i < strip1Lit; i++) {
    const int16_t left = (int16_t)strip1MidLeft - (int16_t)i;
    const int16_t right = (int16_t)strip1MidRight + (int16_t)i;
    if (left >= (int16_t)win1.start && left <= (int16_t)win1.end) {
      strip1.setPixelColor((uint16_t)left, STARTUP_COLOR);
    }
    if (right <= (int16_t)win1.end && right >= (int16_t)win1.start &&
        right != left) {
      strip1.setPixelColor((uint16_t)right, STARTUP_COLOR);
    }
  }

  for (uint16_t i = 0; i < strip2Lit; i++) {
    const int16_t left = (int16_t)strip2MidLeft - (int16_t)i;
    const int16_t right = (int16_t)strip2MidRight + (int16_t)i;
    if (left >= (int16_t)win2.start && left <= (int16_t)win2.end) {
      strip2.setPixelColor((uint16_t)left, STARTUP_COLOR);
    }
    if (right <= (int16_t)win2.end && right >= (int16_t)win2.start &&
        right != left) {
      strip2.setPixelColor((uint16_t)right, STARTUP_COLOR);
    }
  }

  strip1.show();
  strip2.show();
}

static void toxicPickPattern() {
  const uint8_t prev = (uint8_t)toxicPattern;
  uint8_t next = (uint8_t)random(0, 3);
  if (next == prev) {
    next = (uint8_t)((next + 1) % 3);
  }
  toxicPattern = (ToxicPattern)next;
  toxicPatternStartMs = millis();
  toxicChasePos = 0;
}

static uint8_t toxicBreatheUpdateBrightness() {
  toxicBreathePhase += toxicBreatheStep;

  if (toxicBreathePhase >= 255) {
    toxicBreathePhase = 255;
    toxicBreatheStep = -abs(toxicBreatheStep);
  } else if (toxicBreathePhase <= 0) {
    toxicBreathePhase = 0;
    toxicBreatheStep = abs(toxicBreatheStep);
  }
  return (uint8_t)toxicBreathePhase;
}

static void toxicAnimSlowBreatheStep() {
  const uint8_t b = toxicBreatheUpdateBrightness();
  const uint32_t c = scaleColor(strip1, BLU_DAEVA, b);
  staticFill(c);
}

static void toxicAnimPatternStep(uint32_t now) {
  if ((int32_t)(now - (toxicPatternStartMs +
                       ProjectConfig::Timing::kToxicPatternDurationMs)) >= 0) {
    toxicPickPattern();
  }

  if (toxicPattern == TOXIC_STROBE) {
    const uint32_t halfPeriodMs =
        (ProjectConfig::Animation::kToxicStrobeHalfPeriodMs == 0)
            ? 1
            : ProjectConfig::Animation::kToxicStrobeHalfPeriodMs;
    const bool on = (((now / halfPeriodMs) % 2) == 0);
    staticFill(on ? TOXIC_RED : 0);
    return;
  }

  if (toxicPattern == TOXIC_CHASE) {
    invalidateStaticFrame();
    strip1.clear();
    strip2.clear();

    if (win1.len > 0) {
      strip1.setPixelColor(windowIndex(win1, toxicChasePos), TOXIC_ORANGE);
    }
    if (win2.len > 0) {
      // Strip 2 runs the chase backwards, mirroring strip 1.
      const uint16_t offset = (uint16_t)(toxicChasePos % win2.len);
      strip2.setPixelColor((uint16_t)(win2.end - offset), TOXIC_ORANGE);
    }

    strip1.show();
    strip2.show();
    toxicChasePos++;
    return;
  }

  const uint32_t halfPeriodMs =
      (ProjectConfig::Animation::kToxicAlternateHalfPeriodMs == 0)
          ? 1
          : ProjectConfig::Animation::kToxicAlternateHalfPeriodMs;
  const bool phase = (((now / halfPeriodMs) % 2) == 0);
  invalidateStaticFrame();
  strip1.clear();
  strip2.clear();
  for (uint16_t i = 0; i < win1.len; i++) {
    const bool even = ((i % 2) == 0);
    strip1.setPixelColor((uint16_t)(win1.start + i),
                         (even == phase) ? TOXIC_MAGENTA : CYAN);
  }
  for (uint16_t i = 0; i < win2.len; i++) {
    const bool even = ((i % 2) == 0);
    strip2.setPixelColor((uint16_t)(win2.start + i),
                         (even == phase) ? CYAN : TOXIC_MAGENTA);
  }
  strip1.show();
  strip2.show();
}

static void maintenanceAnimStep() {
  const uint8_t b = maintenanceUpdateBrightness();
  const uint32_t c = scaleColor(strip1, MAINT_ORANGE, b);
  staticFill(c);
}

static void enterActiveState() {
  chasePos = 0;
  breathePhase = 0;
  breatheStep = abs(breatheStep);
  const uint32_t nowMs = millis();
  activeFadeStartMs = nowMs;
  activeFadeFromColor = currentWaitColor(nowMs);
  lastActivityMs = nowMs;

  // The fade-in lands at full brightness, so the breath has to start from its
  // peak and fall away - otherwise the color would arrive and immediately jump
  // down to the dim end of the pulse.
  const uint32_t halfPeriodMs = ProjectConfig::Animation::kActivePulsePeriodMs / 2;
  activePulseStartMs =
      nowMs + ProjectConfig::Animation::kActiveFadeInMs - halfPeriodMs;

  if (ProjectConfig::Animation::kActiveAnim == 3) {
    uint32_t durMs = (uint32_t)(LedStateMachine::activeUntilMs() - millis());
    if (durMs == 0) {
      durMs = 1;
    }

    uint32_t halfPeriodMs = ProjectConfig::Animation::kBounceHalfPeriodMs;
    uint16_t upStart = ProjectConfig::Strips::kStrip1Start;
    uint16_t upEnd = ProjectConfig::Strips::kStrip1End;
    uint16_t dnStart = ProjectConfig::Strips::kStrip2Start;
    uint16_t dnEnd = ProjectConfig::Strips::kStrip2End;
    uint32_t runColor = currentActiveColor();

    bounceAnimBegin(durMs,
                    halfPeriodMs,
                    upStart,
                    upEnd,
                    runColor,
                    dnStart,
                    dnEnd,
                    runColor);
  }
}

static void onStateEntered(LedStateMachine::ProgramState st, uint32_t now) {
  nextFrameAt = 0;
  // The next state paints something different, so the cached frame is stale.
  invalidateStaticFrame();

  if (st == LedStateMachine::ST_STARTUP) {
    startupAnimStartMs = now;
  } else if (st == LedStateMachine::ST_WAIT) {
    breathePhase = 0;
    breatheStep = abs(breatheStep);
    // ENDING has already faded the strips to whatever the idle color is, so
    // land on it without a second fade. The host owns the tint, so a pour
    // started from the settings screens comes back to the settings color.
    waitColorFrom = waitColorTarget;
    waitFading = false;
    lastActivityMs = now;
  } else if (st == LedStateMachine::ST_TOXIC) {
    if (ProjectConfig::Animation::kToxicAnim == 2) {
      toxicBreathePhase = 0;
      toxicBreatheStep = abs(ProjectConfig::Animation::kToxicBreatheStep);
    } else {
      toxicPickPattern();
    }
  } else if (st == LedStateMachine::ST_MANUTENZIONE) {
    maintPhase = 0;
    maintStep = abs(maintStep);
  } else if (st == LedStateMachine::ST_ACTIVE) {
    enterActiveState();
  } else if (st == LedStateMachine::ST_ENDING) {
    endAnimStartMs = now;
    endingColor = activeColorValid ? activeColor : BLU_DAEVA;
  }
}

static bool parsePresetColorInternal(const char* name, uint32_t& outColor) {
  if (name == nullptr) {
    return false;
  }

  char norm[32];
  uint8_t w = 0;
  for (const char* p = name; *p != '\0' && w < (uint8_t)(sizeof(norm) - 1); p++) {
    char c = *p;
    if (c == '-' || c == ' ') {
      c = '_';
    }
    norm[w++] = (char)toupper((unsigned char)c);
  }
  norm[w] = '\0';

  for (uint8_t i = 0; i < ProjectConfig::Colors::kPresetColors.size(); i++) {
    const ProjectConfig::Colors::NamedColor& preset =
        ProjectConfig::Colors::kPresetColors[i];
    if (strcmp(norm, preset.name) == 0) {
      outColor = strip1.Color(preset.rgb.r, preset.rgb.g, preset.rgb.b);
      return true;
    }
  }
  return false;
}

}  // namespace

namespace LedAnimations {

void begin() {
  strip1.begin();
  strip2.begin();
  strip1.setBrightness(ProjectConfig::Animation::kGlobalBrightness);
  strip2.setBrightness(ProjectConfig::Animation::kGlobalBrightness);

  CYAN = strip1.Color(ProjectConfig::Colors::kCyan.r,
                      ProjectConfig::Colors::kCyan.g,
                      ProjectConfig::Colors::kCyan.b);
  YELL = strip1.Color(ProjectConfig::Colors::kYellow.r,
                      ProjectConfig::Colors::kYellow.g,
                      ProjectConfig::Colors::kYellow.b);
  BLU_DAEVA = strip1.Color(ProjectConfig::Colors::kBluDaeva.r,
                           ProjectConfig::Colors::kBluDaeva.g,
                           ProjectConfig::Colors::kBluDaeva.b);
  STARTUP_COLOR = strip1.Color(ProjectConfig::Colors::kStartupDefault.r,
                               ProjectConfig::Colors::kStartupDefault.g,
                               ProjectConfig::Colors::kStartupDefault.b);

  TOXIC_RED = strip1.Color(ProjectConfig::Colors::kToxicRed.r,
                           ProjectConfig::Colors::kToxicRed.g,
                           ProjectConfig::Colors::kToxicRed.b);
  TOXIC_ORANGE = strip1.Color(ProjectConfig::Colors::kToxicOrange.r,
                              ProjectConfig::Colors::kToxicOrange.g,
                              ProjectConfig::Colors::kToxicOrange.b);
  TOXIC_MAGENTA = strip1.Color(ProjectConfig::Colors::kToxicMagenta.r,
                               ProjectConfig::Colors::kToxicMagenta.g,
                               ProjectConfig::Colors::kToxicMagenta.b);
  MAINT_ORANGE = strip1.Color(ProjectConfig::Colors::kMaintenanceOrange.r,
                              ProjectConfig::Colors::kMaintenanceOrange.g,
                              ProjectConfig::Colors::kMaintenanceOrange.b);

  waitColorFrom = BLU_DAEVA;
  waitColorTarget = BLU_DAEVA;
  waitFading = false;
  idleTinted = false;
  lastActivityMs = millis();

  activeColor = BLU_DAEVA;
  activeColorValid = false;
  endingColor = BLU_DAEVA;

  randomSeed(analogRead(BoardConfig::Random::kAnalogSeedPin) ^ micros());

  LedStateMachine::setStartupDurationMs(ProjectConfig::Timing::kStartupDurationMs);
  LedStateMachine::setToxicTimeoutMs(ProjectConfig::Timing::kToxicTimeoutMs);
  LedStateMachine::begin();
  nextFrameAt = 0;
  onStateEntered(LedStateMachine::state(), millis());
}

void startActive(uint32_t activeUntilMs) {
  (void)startActiveWithColor(activeUntilMs, BLU_DAEVA);
}

void setStartupColor(uint8_t r, uint8_t g, uint8_t b) {
  STARTUP_COLOR = strip1.Color(r, g, b);
}

bool startActiveWithColor(uint32_t activeUntilMs, uint32_t color) {
  if (!LedStateMachine::startActive(activeUntilMs)) {
    return false;
  }

  activeColor = color;
  activeColorValid = true;
  onStateEntered(LedStateMachine::ST_ACTIVE, millis());
  return true;
}

bool setIdleTint(uint32_t color) {
  if (!LedStateMachine::isWaitingForCommand()) {
    return false;
  }
  idleTinted = (color != BLU_DAEVA);
  beginWaitFadeTo(color);
  return true;
}

bool clearIdleTint() {
  if (!LedStateMachine::isWaitingForCommand()) {
    return false;
  }
  idleTinted = false;
  beginWaitFadeTo(BLU_DAEVA);
  return true;
}

bool extendActive(uint32_t activeUntilMs) {
  return LedStateMachine::extendActive(activeUntilMs);
}

bool finishActiveNow() {
  return LedStateMachine::finishActiveNow(millis());
}

bool handleReadyCommand() {
  const LedStateMachine::ProgramState before = LedStateMachine::state();
  const bool handled = LedStateMachine::handleReady();
  const LedStateMachine::ProgramState after = LedStateMachine::state();
  if (handled && before != after) {
    onStateEntered(after, millis());
    if (after == LedStateMachine::ST_WAIT) {
      DAEVA_SERIAL.println("OK READY");
    }
  }
  return handled;
}

bool handleMaintenanceCommand() {
  const LedStateMachine::ProgramState before = LedStateMachine::state();
  const bool entered = LedStateMachine::enterMaintenance();
  const LedStateMachine::ProgramState after = LedStateMachine::state();
  if (entered && before != after) {
    onStateEntered(after, millis());
  }
  return entered;
}

bool handleToxicCommand() {
  const LedStateMachine::ProgramState before = LedStateMachine::state();
  const bool entered = LedStateMachine::enterToxic();
  const LedStateMachine::ProgramState after = LedStateMachine::state();
  if (entered && before != after) {
    onStateEntered(after, millis());
  }
  return entered;
}

bool parsePresetColor(const char* name, uint32_t& outColor) {
  return parsePresetColorInternal(name, outColor);
}

bool isWaitingForCommand() {
  return LedStateMachine::isWaitingForCommand();
}

bool update() {
  const uint32_t now = millis();
  LedStateMachine::TickResult tickRes =
      LedStateMachine::tick(now, endStatusDurationMs());
  const bool doneEvent = tickRes.doneEvent;

  if (tickRes.enteredState) {
    onStateEntered(tickRes.state, now);
    if (tickRes.state == LedStateMachine::ST_WAIT) {
      DAEVA_SERIAL.println("OK READY");
    }
  }

  if ((int32_t)(now - nextFrameAt) < 0) {
    return doneEvent;
  }

  if (tickRes.state == LedStateMachine::ST_STARTUP) {
    startupAnimStep();
    nextFrameAt = now + ProjectConfig::Animation::kFrameStartupMs;
    return doneEvent;
  }

  if (tickRes.state == LedStateMachine::ST_WAIT) {
    waitAnimStep(now);
    nextFrameAt = now + ProjectConfig::Animation::kFrameWaitMs;
    return doneEvent;
  }

  if (tickRes.state == LedStateMachine::ST_TOXIC) {
    if (ProjectConfig::Animation::kToxicAnim == 2) {
      toxicAnimSlowBreatheStep();
    } else {
      toxicAnimPatternStep(now);
    }
    nextFrameAt = now + ProjectConfig::Animation::kFrameToxicMs;
    return doneEvent;
  }

  if (tickRes.state == LedStateMachine::ST_MANUTENZIONE) {
    maintenanceAnimStep();
    nextFrameAt = now + ProjectConfig::Animation::kFrameMaintenanceMs;
    return doneEvent;
  }

  if (tickRes.state == LedStateMachine::ST_ACTIVE) {
    const uint32_t color = currentActiveColor();
    if (ProjectConfig::Animation::kActiveAnim == 1) {
      activeAnim1Step(color);
    } else if (ProjectConfig::Animation::kActiveAnim == 2) {
      activeAnim2Step(color);
    } else if (ProjectConfig::Animation::kActiveAnim == 3) {
      bounceAnimStep();
    } else if (ProjectConfig::Animation::kActiveAnim == 4) {
      activeAnim4Step(color, now);
    }

    nextFrameAt = now + ProjectConfig::Animation::kFrameActiveMs;
    return doneEvent;
  }

  endHoldStep(now);
  nextFrameAt = now + ProjectConfig::Animation::kFrameEndingMs;

  return doneEvent;
}

}  // namespace LedAnimations
