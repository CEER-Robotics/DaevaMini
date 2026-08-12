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
static uint32_t endAnimStartMs = 0;
static uint32_t startupAnimStartMs = 0;

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
  if (ProjectConfig::Timing::kEndFlashCount == 0 ||
      ProjectConfig::Timing::kEndFlashPeriodMs == 0) {
    return 1;
  }
  return (uint32_t)ProjectConfig::Timing::kEndFlashCount *
         ProjectConfig::Timing::kEndFlashPeriodMs;
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

static void waitAnimStep(uint32_t color) {
  staticFill(color);
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

static void endFlashStep(uint32_t now) {
  const uint32_t periodMs = (ProjectConfig::Timing::kEndFlashPeriodMs == 0)
                                ? 1
                                : ProjectConfig::Timing::kEndFlashPeriodMs;
  const uint16_t ratioTotal = (uint16_t)ProjectConfig::Timing::kEndFlashOnPart +
                              (uint16_t)ProjectConfig::Timing::kEndFlashOffPart;
  const uint32_t onMs =
      (ratioTotal == 0)
          ? 0
          : (periodMs * (uint32_t)ProjectConfig::Timing::kEndFlashOnPart) /
                ratioTotal;
  const uint32_t phaseMs = (now - endAnimStartMs) % periodMs;
  const bool isOn = (onMs > 0) && (phaseMs < onMs);
  staticFill(isOn ? endingColor : 0);
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
    waitAnimStep(BLU_DAEVA);
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
    }

    nextFrameAt = now + ProjectConfig::Animation::kFrameActiveMs;
    return doneEvent;
  }

  endFlashStep(now);
  nextFrameAt = now + ProjectConfig::Animation::kFrameEndingMs;

  return doneEvent;
}

}  // namespace LedAnimations
