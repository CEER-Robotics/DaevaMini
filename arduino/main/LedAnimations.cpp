#include "LedAnimations.h"
#include "LedStateMachine.h"

#include <Adafruit_NeoPixel.h>
#include <array>
#include <ctype.h>
#include <string.h>

namespace {

static const uint8_t kStrip1Pin = 2;
static const uint8_t kStrip2Pin = 3;
static const uint16_t kStrip1Len = 24;
static const uint16_t kStrip2Len = 34;
static const uint16_t kStrip1Start = 0;
static const uint16_t kStrip1End = kStrip1Len - 1;
static const uint16_t kStrip2Start = 4;
static const uint16_t kStrip2End = 33 - 4;

static const uint32_t STARTUP_DURATION_MS = 10000;
static const uint32_t TOXIC_TIMEOUT_MS = 30000;

static uint8_t endFlashCount = 5;
static uint32_t endFlashPeriodMs = 500;
static uint8_t endFlashOnPart = 60;
static uint8_t endFlashOffPart = 40;
static const uint8_t kActiveAnim = 3;
static const int16_t kBreatheStep = 10;

Adafruit_NeoPixel strip1(kStrip1Len, kStrip1Pin, NEO_GRB + NEO_KHZ800);
Adafruit_NeoPixel strip2(kStrip2Len, kStrip2Pin, NEO_GRB + NEO_KHZ800);

static const std::array<uint8_t, 3> kCyanRgb = {0, 255, 255};
static const std::array<uint8_t, 3> kYellRgb = {255, 255, 0};
static const std::array<uint8_t, 3> kBluDaevaRgb = {202, 240, 248};
static const std::array<uint8_t, 3> kStartupDefaultRgb = {255, 120, 0};

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
static int16_t breatheStep = kBreatheStep;

static int16_t maintPhase = 0;
static int16_t maintStep = 3;

static uint32_t bounceStartMs = 0;
static uint32_t bounceDurationMs = 0;
static uint32_t bounceHalfPeriodMs = 2000;

static uint16_t upA = 0;
static uint16_t upB = kStrip1Len - 1;
static uint16_t dnA = 4;
static uint16_t dnB = 33 - 4;
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

struct PresetColorEntry {
  const char* name;
  uint8_t r;
  uint8_t g;
  uint8_t b;
};

static const PresetColorEntry kPresetColors[] = {
    {"ORANGE", 255, 120, 0},      {"RED", 255, 0, 0},
    {"GREEN", 0, 255, 0},         {"BLUE", 0, 0, 255},
    {"CYAN", 0, 255, 255},        {"MAGENTA", 255, 0, 255},
    {"YELLOW", 255, 255, 0},      {"WHITE", 255, 255, 255},
    {"WARM_WHITE", 255, 180, 110},{"PURPLE", 128, 0, 255}};

static uint32_t endStatusDurationMs() {
  if (endFlashCount == 0 || endFlashPeriodMs == 0) {
    return 1;
  }
  return (uint32_t)endFlashCount * endFlashPeriodMs;
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

static void staticFill(uint32_t color) {
  for (uint16_t i = 0; i < strip1.numPixels(); i++) {
    strip1.setPixelColor(i, color);
  }
  for (uint16_t i = 0; i < strip2.numPixels(); i++) {
    strip2.setPixelColor(i, color);
  }

  strip1.show();
  strip2.show();
}

static void waitAnimStep(uint32_t color) {
  staticFill(color);
}

static uint32_t currentActiveColor() {
  return activeColorValid ? activeColor : BLU_DAEVA;
}

static void activeAnim1Step(uint32_t color) {
  strip1.clear();
  strip2.clear();

  const uint16_t p1 = chasePos % strip1.numPixels();
  const uint16_t p2 = (chasePos + strip1.numPixels() / 2) % strip1.numPixels();
  const uint32_t accent = scaleColor(strip1, color, 120);

  strip1.setPixelColor(p1, color);
  strip1.setPixelColor(p2, accent);

  const uint16_t q1 = chasePos % strip2.numPixels();
  const uint16_t q2 = (chasePos + strip2.numPixels() / 3) % strip2.numPixels();
  strip2.setPixelColor(q1, accent);
  strip2.setPixelColor(q2, color);

  strip1.show();
  strip2.show();

  chasePos++;
}

static void activeAnim2Step(uint32_t color) {
  const uint8_t up = breatheUpdateGetBrightness();
  const uint8_t dn = (uint8_t)(255 - breathePhase);

  const uint32_t c1 = scaleColor(strip1, color, up);
  const uint32_t c2 = scaleColor(strip2, color, dn);

  for (uint16_t i = 0; i < strip1.numPixels(); i++) {
    strip1.setPixelColor(i, c1);
  }
  for (uint16_t i = 0; i < strip2.numPixels(); i++) {
    strip2.setPixelColor(i, c2);
  }

  strip1.show();
  strip2.show();
}

static void endFlashStep(uint32_t now) {
  const uint32_t periodMs = (endFlashPeriodMs == 0) ? 1 : endFlashPeriodMs;
  const uint16_t ratioTotal =
      (uint16_t)endFlashOnPart + (uint16_t)endFlashOffPart;
  const uint32_t onMs =
      (ratioTotal == 0) ? 0 : (periodMs * (uint32_t)endFlashOnPart) / ratioTotal;
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
  bounceStartMs = millis();
  bounceDurationMs = (durationMs == 0) ? 1 : durationMs;
  bounceHalfPeriodMs = halfPeriodMs;

  const uint16_t n1 = strip1.numPixels();
  const uint16_t n2 = strip2.numPixels();
  upA = (n1 > 0) ? (strip1Start % n1) : 0;
  upB = (n1 > 0) ? (strip1End % n1) : 0;
  dnA = (n2 > 0) ? (strip2Start % n2) : 0;
  dnB = (n2 > 0) ? (strip2End % n2) : 0;

  upCol = strip1Color;
  dnCol = strip2Color;

  strip1.clear();
  strip1.show();
  strip2.clear();
  strip2.show();
}

static void bounceAnimStep() {
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

  if (strip1.numPixels() > 0) {
    strip1.setPixelColor(pUp, upCol);
  }
  if (strip2.numPixels() > 0) {
    strip2.setPixelColor(pDn, dnCol);
  }

  strip1.show();
  strip2.show();
}

static void startupAnimStep() {
  const uint32_t now = millis();
  uint32_t elapsed = now - startupAnimStartMs;
  const uint32_t durationMs = (STARTUP_DURATION_MS == 0) ? 1 : STARTUP_DURATION_MS;
  if (elapsed > durationMs) {
    elapsed = durationMs;
  }

  const uint16_t strip1Len =
      (kStrip1End >= kStrip1Start) ? (kStrip1End - kStrip1Start + 1) : 0;
  const uint16_t strip2Len =
      (kStrip2End >= kStrip2Start) ? (kStrip2End - kStrip2Start + 1) : 0;
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

  const uint16_t strip1MidLeft = kStrip1Start + ((strip1Len - 1) / 2);
  const uint16_t strip1MidRight = kStrip1Start + (strip1Len / 2);
  const uint16_t strip2MidLeft = kStrip2Start + ((strip2Len - 1) / 2);
  const uint16_t strip2MidRight = kStrip2Start + (strip2Len / 2);

  strip1.clear();
  strip2.clear();

  const uint16_t strip1Lit = (litLevel > strip1Steps) ? strip1Steps : litLevel;
  const uint16_t strip2Lit = (litLevel > strip2Steps) ? strip2Steps : litLevel;

  for (uint16_t i = 0; i < strip1Lit; i++) {
    const int16_t left = (int16_t)strip1MidLeft - (int16_t)i;
    const int16_t right = (int16_t)strip1MidRight + (int16_t)i;
    if (left >= (int16_t)kStrip1Start && left < (int16_t)strip1.numPixels()) {
      strip1.setPixelColor((uint16_t)left, STARTUP_COLOR);
    }
    if (right <= (int16_t)kStrip1End &&
        right < (int16_t)strip1.numPixels() &&
        right != left) {
      strip1.setPixelColor((uint16_t)right, STARTUP_COLOR);
    }
  }

  for (uint16_t i = 0; i < strip2Lit; i++) {
    const int16_t left = (int16_t)strip2MidLeft - (int16_t)i;
    const int16_t right = (int16_t)strip2MidRight + (int16_t)i;
    if (left >= (int16_t)kStrip2Start && left < (int16_t)strip2.numPixels()) {
      strip2.setPixelColor((uint16_t)left, STARTUP_COLOR);
    }
    if (right <= (int16_t)kStrip2End &&
        right < (int16_t)strip2.numPixels() &&
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

static void toxicAnimStep(uint32_t now) {
  if ((int32_t)(now - (toxicPatternStartMs + 1800)) >= 0) {
    toxicPickPattern();
  }

  if (toxicPattern == TOXIC_STROBE) {
    const bool on = (((now / 120) % 2) == 0);
    staticFill(on ? TOXIC_RED : 0);
    return;
  }

  if (toxicPattern == TOXIC_CHASE) {
    strip1.clear();
    strip2.clear();

    if (strip1.numPixels() > 0) {
      const uint16_t p1 = toxicChasePos % strip1.numPixels();
      strip1.setPixelColor(p1, TOXIC_ORANGE);
    }
    if (strip2.numPixels() > 0) {
      const uint16_t p2 = strip2.numPixels() - 1 - (toxicChasePos % strip2.numPixels());
      strip2.setPixelColor(p2, TOXIC_ORANGE);
    }

    strip1.show();
    strip2.show();
    toxicChasePos++;
    return;
  }

  const bool phase = (((now / 180) % 2) == 0);
  for (uint16_t i = 0; i < strip1.numPixels(); i++) {
    const bool even = ((i % 2) == 0);
    strip1.setPixelColor(i, (even == phase) ? TOXIC_MAGENTA : CYAN);
  }
  for (uint16_t i = 0; i < strip2.numPixels(); i++) {
    const bool even = ((i % 2) == 0);
    strip2.setPixelColor(i, (even == phase) ? CYAN : TOXIC_MAGENTA);
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

  if (kActiveAnim == 3) {
    uint32_t durMs = (uint32_t)(LedStateMachine::activeUntilMs() - millis());
    if (durMs == 0) {
      durMs = 1;
    }

    uint32_t halfPeriodMs = 1500;
    uint16_t upStart = kStrip1Start;
    uint16_t upEnd = kStrip1End;
    uint16_t dnStart = kStrip2Start;
    uint16_t dnEnd = kStrip2End;
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

  if (st == LedStateMachine::ST_STARTUP) {
    startupAnimStartMs = now;
  } else if (st == LedStateMachine::ST_WAIT) {
    breathePhase = 0;
    breatheStep = abs(breatheStep);
  } else if (st == LedStateMachine::ST_TOXIC) {
    toxicPickPattern();
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

  for (uint8_t i = 0; i < (uint8_t)(sizeof(kPresetColors) / sizeof(kPresetColors[0])); i++) {
    if (strcmp(norm, kPresetColors[i].name) == 0) {
      outColor = strip1.Color(kPresetColors[i].r,
                              kPresetColors[i].g,
                              kPresetColors[i].b);
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
  strip1.setBrightness(255);
  strip2.setBrightness(255);

  CYAN = strip1.Color(kCyanRgb[0], kCyanRgb[1], kCyanRgb[2]);
  YELL = strip1.Color(kYellRgb[0], kYellRgb[1], kYellRgb[2]);
  BLU_DAEVA = strip1.Color(kBluDaevaRgb[0], kBluDaevaRgb[1], kBluDaevaRgb[2]);
  STARTUP_COLOR = strip1.Color(kStartupDefaultRgb[0],
                               kStartupDefaultRgb[1],
                               kStartupDefaultRgb[2]);

  TOXIC_RED = strip1.Color(255, 0, 0);
  TOXIC_ORANGE = strip1.Color(255, 120, 0);
  TOXIC_MAGENTA = strip1.Color(255, 0, 255);
  MAINT_ORANGE = strip1.Color(255, 120, 0);

  activeColor = BLU_DAEVA;
  activeColorValid = false;
  endingColor = BLU_DAEVA;

  randomSeed(analogRead(A0) ^ micros());

  LedStateMachine::setStartupDurationMs(STARTUP_DURATION_MS);
  LedStateMachine::setToxicTimeoutMs(TOXIC_TIMEOUT_MS);
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
  }

  if ((int32_t)(now - nextFrameAt) < 0) {
    return doneEvent;
  }

  if (tickRes.state == LedStateMachine::ST_STARTUP) {
    startupAnimStep();
    nextFrameAt = now + 50;
    return doneEvent;
  }

  if (tickRes.state == LedStateMachine::ST_WAIT) {
    waitAnimStep(BLU_DAEVA);
    nextFrameAt = now + 40;
    return doneEvent;
  }

  if (tickRes.state == LedStateMachine::ST_TOXIC) {
    toxicAnimStep(now);
    nextFrameAt = now + 50;
    return doneEvent;
  }

  if (tickRes.state == LedStateMachine::ST_MANUTENZIONE) {
    maintenanceAnimStep();
    nextFrameAt = now + 45;
    return doneEvent;
  }

  if (tickRes.state == LedStateMachine::ST_ACTIVE) {
    const uint32_t color = currentActiveColor();
    if (kActiveAnim == 1) {
      activeAnim1Step(color);
    } else if (kActiveAnim == 2) {
      activeAnim2Step(color);
    } else if (kActiveAnim == 3) {
      bounceAnimStep();
    }

    nextFrameAt = now + 33;
    return doneEvent;
  }

  endFlashStep(now);
  nextFrameAt = now + 20;

  return doneEvent;
}

}  // namespace LedAnimations
