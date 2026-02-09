#include <Arduino.h>
#include <Adafruit_NeoPixel.h>
#include <array>

/* ===================== USER PARAMETERS ===================== */
static const uint32_t BAUD = 115200;

// Discrete LEDs (LED1..LED17)
static const uint8_t LED_COUNT = 17;
static const uint8_t ledPins[LED_COUNT] = {
  6, 7, 8, 9, 10, 11, 12, 13, 22, 24, 26, 28, 30, 32, 34, 36, 38
};

// Mega: PWM is 0..255 and only on PWM pins (2–13, 44–46). Others act ON/OFF.
static const uint8_t PWM_VALUE = 130;

// WS2812 strips
static const uint8_t STRIP1_PIN = 2;
static const uint8_t STRIP2_PIN = 3;
static const uint16_t STRIP1_N = 24;//stiscia inf
static const uint16_t STRIP2_N = 34;//striscia sup 33

// How long to run the “end status” animation after the last LED switches off
static const uint32_t EndStatusMs = 3000;

// Choose which ACTIVE animation to run (1 or 2)
static const uint8_t ACTIVE_ANIM = 3;
// Choose which END animation to run (1 or 2)
static const uint8_t END_ANIM = 1;

// Breathing speed (step per frame). 1 = smooth/slow, 10 = faster
static const int16_t BREATHE_STEP = 10;
/* ============================================================ */

// Per-LED OFF timestamps (0 means off)
static uint32_t offAtMs[LED_COUNT];

// Serial line buffer
static char rxLine[256];
static size_t rxLen = 0;

// WS2812 objects
Adafruit_NeoPixel strip1(STRIP1_N, STRIP1_PIN, NEO_GRB + NEO_KHZ800);
Adafruit_NeoPixel strip2(STRIP2_N, STRIP2_PIN, NEO_GRB + NEO_KHZ800);

// Colors (parameterized)
static const std::array<uint8_t, 3> CYAN_ = {0, 255, 255};
static const std::array<uint8_t, 3> YELL_ = {0, 255, 255};
static const std::array<uint8_t, 3> BLU_DAEVA_ = {202,240,248};

uint32_t CYAN = strip1.Color(CYAN_[0], CYAN_[1], CYAN_[2]);
uint32_t YELL = strip1.Color(YELL_[0], YELL_[1], YELL_[2]);
uint32_t BLU_DAEVA = strip1.Color(BLU_DAEVA_[0], BLU_DAEVA_[1], BLU_DAEVA_[2]);

// ---------- Strip/Program state machine ----------
enum ProgramState { ST_START_WAIT, ST_ACTIVE, ST_ENDING };
static ProgramState progState = ST_START_WAIT;

static uint32_t activeUntil = 0;
static uint32_t endingUntil = 0;

// Frame scheduling
static uint32_t nextFrameAt = 0;

// Animation state vars
static uint16_t chasePos = 0;
static uint16_t altPos = 0;

// ONE breathing engine (used everywhere)
static int16_t breathePhase = 0;            // 0..255
static int16_t breatheStep  = BREATHE_STEP; // +/-

/* ---------------- Discrete LED helpers ---------------- */
static inline void ledOff(uint8_t idx) {
  analogWrite(ledPins[idx], 0);
  offAtMs[idx] = 0;
}

static inline void ledOnFor(uint8_t idx, uint32_t durationMs) {
  analogWrite(ledPins[idx], PWM_VALUE);
  offAtMs[idx] = millis() + durationMs;
}

static void allDiscreteOff() {
  for (uint8_t i = 0; i < LED_COUNT; i++) {
    analogWrite(ledPins[i], 0);
    offAtMs[i] = 0;
  }
}

/* ---------------- Color helpers ---------------- */
static uint32_t scaleColor(Adafruit_NeoPixel& s, uint32_t c, uint8_t b) {
  uint8_t r = (uint8_t)(c >> 16);
  uint8_t g = (uint8_t)(c >>  8);
  uint8_t bl= (uint8_t)(c >>  0);
  r  = (uint8_t)((uint16_t)r  * b / 255);
  g  = (uint8_t)((uint16_t)g  * b / 255);
  bl = (uint8_t)((uint16_t)bl * b / 255);
  return s.Color(r, g, bl);
}

/* ---------------- Breathing engine ----------------
   - Updates breathePhase as a triangle wave: 0..255..0
   - Returns current brightness (0..255)
*/
static uint8_t breatheUpdate_getBrightness() {
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

/* Fill both strips with a BREATHING version of the passed color */
static void breatheFill_step(uint32_t color) {
  uint8_t b = breatheUpdate_getBrightness();       // 0..255
  uint32_t c = scaleColor(strip1, color, b);       // apply brightness to the passed color

  for (uint16_t i = 0; i < strip1.numPixels(); i++) strip1.setPixelColor(i, c);
  for (uint16_t i = 0; i < strip2.numPixels(); i++) strip2.setPixelColor(i, c);

  strip1.show();
  strip2.show();
}

/* Fill both strips with a STATIC (no breathing) version of the passed color */
static void staticFill(uint32_t color) {
  // If you still want to respect the current global brightness setting of the strip,
  // keep the next line; otherwise, just use `uint32_t c = color;`
  uint32_t c = color;

  for (uint16_t i = 0; i < strip1.numPixels(); i++) strip1.setPixelColor(i, c);
  for (uint16_t i = 0; i < strip2.numPixels(); i++) strip2.setPixelColor(i, c);

  strip1.show();
  strip2.show();
}

/* ============================================================
   PARSING: returns max OFF timestamp among LEDs scheduled by THIS message
   ============================================================ */
static uint32_t parseAndScheduleGetMaxEnd(const char* s) {
  uint32_t maxEnd = 0;

  while (*s) {
    if (*s == 'P' || *s == 'p') {
      s++;

      int ledNum = 0;
      while (*s >= '0' && *s <= '9') {
        ledNum = ledNum * 10 + (*s - '0');
        s++;
      }
      if (ledNum < 1 || ledNum > (int)LED_COUNT) continue;

      while (*s && *s != ':') s++;
      if (*s != ':') continue;
      s++;

      uint32_t dur = 0; bool hasDigits = false;
      while (*s >= '0' && *s <= '9') {
        hasDigits = true;
        dur = dur * 10u + (uint32_t)(*s - '0');
        s++;
      }
      if (!hasDigits) continue;

      uint8_t idx = (uint8_t)(ledNum - 1);

      if (dur == 0) {
        ledOff(idx);
      } else {
        ledOnFor(idx, dur);
        if (offAtMs[idx] > maxEnd) maxEnd = offAtMs[idx];
      }
    } else {
      s++;
    }
  }
  return maxEnd;
}


/* ============================================================
   SERIAL: accept a NEW command only when in ST_START_WAIT
   ============================================================ */
static void readSerialLineNonBlocking() {
  while (SerialUSB.available() > 0) {
    char c = (char)SerialUSB.read();
    if (c == '\r') continue;

    if (c == '\n') {
      rxLine[rxLen] = '\0';

      if (rxLen > 0 && progState == ST_START_WAIT) {
        allDiscreteOff();

        uint32_t maxEnd = parseAndScheduleGetMaxEnd(rxLine);
        if (maxEnd != 0) {
          activeUntil = maxEnd;
          endingUntil = 0;
          progState = ST_ACTIVE;
          nextFrameAt = 0;

          // reset animation state
          chasePos = 0;
          altPos = 0;
          breathePhase = 0;
          breatheStep = abs(breatheStep); // start increasing
        }
      }

      rxLen = 0;
      return;
    }

    if (rxLen < sizeof(rxLine) - 1) rxLine[rxLen++] = c;
    else rxLen = 0;
  }
}

/* ---------------- Discrete LED timers ---------------- */
static void updateLedTimers() {
  uint32_t now = millis();
  for (uint8_t i = 0; i < LED_COUNT; i++) {
    if (offAtMs[i] != 0 && (int32_t)(now - offAtMs[i]) >= 0) {
      ledOff(i);
    }
  }
}

/* ============================================================
   WS2812 PATTERNS
   ============================================================ */

// START_WAIT: breathing CYAN (color passed as variable)
static void waitAnim_step(uint32_t Color) {
  //breatheFill_step(Color);
  staticFill(Color);
}

// ACTIVE ANIM 1: two-dot chase cyan/yellow
static void activeAnim1_step() {
  

  strip1.clear();
  strip2.clear();

  uint16_t p1 = chasePos % strip1.numPixels();
  uint16_t p2 = (chasePos + strip1.numPixels() / 2) % strip1.numPixels();
  strip1.setPixelColor(p1, CYAN);
  strip1.setPixelColor(p2, YELL);

  uint16_t q1 = chasePos % strip2.numPixels();
  uint16_t q2 = (chasePos + strip2.numPixels() / 3) % strip2.numPixels();
  strip2.setPixelColor(q1, YELL);
  strip2.setPixelColor(q2, CYAN);

  strip1.show();
  strip2.show();

  chasePos++;
}

// ACTIVE ANIM 2: cross-breath (still uses the SAME breathing engine)
static void activeAnim2_step() {
  //uint32_t CYAN = strip1.Color(CYAN_R, CYAN_G, CYAN_B);
  //uint32_t YELL = strip2.Color(YELL_R, YELL_G, YELL_B);

  uint8_t up = breatheUpdate_getBrightness();       // 0..255
  uint8_t dn = (uint8_t)(255 - breathePhase);       // 255..0 (same phase)

  uint32_t c1 = scaleColor(strip1, CYAN, up);
  uint32_t c2 = scaleColor(strip2, YELL, dn);

  for (uint16_t i = 0; i < strip1.numPixels(); i++) strip1.setPixelColor(i, c1);
  for (uint16_t i = 0; i < strip2.numPixels(); i++) strip2.setPixelColor(i, c2);

  strip1.show();
  strip2.show();
}

// END ANIM 1: solid cyan on strip1, solid yellow on strip2
static void endAnim1_step() {
  //uint32_t CYAN = strip1.Color(CYAN_R, CYAN_G, CYAN_B);
  //uint32_t YELL = strip2.Color(YELL_R, YELL_G, YELL_B);

  for (uint16_t i = 0; i < strip1.numPixels(); i++) strip1.setPixelColor(i, CYAN);
  for (uint16_t i = 0; i < strip2.numPixels(); i++) strip2.setPixelColor(i, YELL);

  strip1.show();
  strip2.show();
}

// END ANIM 2: alternating blocks cyan/yellow scrolling
static void endAnim2_step() {
  //uint32_t CYAN = strip1.Color(CYAN_R, CYAN_G, CYAN_B);
  //uint32_t YELL = strip1.Color(YELL_R, YELL_G, YELL_B);

  const uint8_t block = 4;
  for (uint16_t i = 0; i < strip1.numPixels(); i++) {
    bool sel = ((i + altPos) / block) % 2;
    strip1.setPixelColor(i, BLU_DAEVA);
  }
  for (uint16_t i = 0; i < strip2.numPixels(); i++) {
    bool sel = ((i + altPos) / block) % 2;
    strip2.setPixelColor(i, BLU_DAEVA);
  }

  strip1.show();
  strip2.show();
  altPos++;
}




// ===================== BOUNCING DOT (per strip, start/end, time-based) =====================
// - One dot per strip.
// - Each strip has its own START index and END index.
// - The dot moves START -> END in `bounceHalfPeriodMs`, then END -> START in `bounceHalfPeriodMs`.
// - Total cycle = 2 * bounceHalfPeriodMs (e.g. 500ms + 500ms = 1s per round trip).
// - Repeats until `bounceDurationMs` (use your longest duration / active duration).
// - Non-blocking (millis-based).

static uint32_t bounceStartMs     = 0;
static uint32_t bounceDurationMs  = 0;   // how long to keep bouncing (e.g. longest duration)
static uint32_t bounceHalfPeriodMs = 2000; // configurable: time for one leg (start->end OR end->start)

// Per-strip config
static uint16_t up_a = 0, up_b = 24;   // strip1 start/end
static uint16_t dn_a = 4, dn_b = 33-4;   // strip2 start/end
static uint32_t up_col = 0;
static uint32_t dn_col = 0;

static inline uint16_t clampU16(int32_t v, uint16_t lo, uint16_t hi) {
  if (v < (int32_t)lo) return lo;
  if (v > (int32_t)hi) return hi;
  return (uint16_t)v;
}

static inline uint16_t lerpIndexU16(uint16_t a, uint16_t b, float t) {
  // t in [0,1]
  float v = (float)a + ((float)b - (float)a) * t;
  int32_t vi = (int32_t)(v + 0.5f);
  uint16_t lo = (a < b) ? a : b;
  uint16_t hi = (a > b) ? a : b;
  return clampU16(vi, lo, hi);
}

static void bounceAnim_begin(uint32_t longestDurationMs,
                             uint32_t halfPeriodMs,
                             // STRIP UP (strip1)
                             uint16_t strip1_start, uint16_t strip1_end, uint32_t strip1_color,
                             // STRIP DOWN (strip2)
                             uint16_t strip2_start, uint16_t strip2_end, uint32_t strip2_color) {
  bounceStartMs     = millis();
  bounceDurationMs  = (longestDurationMs == 0) ? 1 : longestDurationMs;
  bounceHalfPeriodMs = halfPeriodMs;

  // Clamp indices to valid ranges
  uint16_t n1 = strip1.numPixels();
  uint16_t n2 = strip2.numPixels();
  up_a = (n1 > 0) ? (strip1_start % n1) : 0;
  up_b = (n1 > 0) ? (strip1_end   % n1) : 0;
  dn_a = (n2 > 0) ? (strip2_start % n2) : 0;
  dn_b = (n2 > 0) ? (strip2_end   % n2) : 0;

  up_col = strip1_color;
  dn_col = strip2_color;

  // Start OFF
  strip1.clear(); strip1.show();
  strip2.clear(); strip2.show();
}

static void bounceAnim_step() {
  uint32_t now = millis();
  uint32_t dt  = now - bounceStartMs;

  // If duration elapsed, keep last frame or clear; here: keep running pattern clamped
  if (dt > bounceDurationMs) dt = bounceDurationMs;

  // Phase in a full cycle [0, 2*halfPeriod)
  uint32_t cycleMs = 2u * bounceHalfPeriodMs;
  uint32_t ph = (cycleMs > 0) ? (dt % cycleMs) : 0;

  // legT: 0..1
  float t = (bounceHalfPeriodMs > 0) ? ((float)(ph % bounceHalfPeriodMs) / (float)bounceHalfPeriodMs) : 1.0f;

  bool forward = (ph < bounceHalfPeriodMs); // first half: start->end, second half: end->start

  // Compute positions
  uint16_t pUp = forward ? lerpIndexU16(up_a, up_b, t) : lerpIndexU16(up_b, up_a, t);
  uint16_t pDn = forward ? lerpIndexU16(dn_a, dn_b, t) : lerpIndexU16(dn_b, dn_a, t);

  strip1.clear();
  strip2.clear();

  if (strip1.numPixels() > 0) strip1.setPixelColor(pUp, up_col);
  if (strip2.numPixels() > 0) strip2.setPixelColor(pDn, dn_col);

  strip1.show();
  strip2.show();
}




// Main strip update (non-blocking, state-driven)
static void updateStripsNonBlocking() {
  uint32_t now = millis();

  static uint8_t prevProgState = ST_START_WAIT;

  if (progState != prevProgState) {
    if (progState == ST_ACTIVE && ACTIVE_ANIM == 3) {

      // total ACTIVE duration at entry (matches your "longest time passed through serial")
      uint32_t durMs = (uint32_t)(activeUntil - now);
      if (durMs == 0) durMs = 1;

      // -------- BOUNCE CONFIG (per strip start/end + speed) --------
      // Half-period: time to go from START -> END (and same time END -> START)
      // Example: 500 ms out + 500 ms back = 1 second round trip
      uint32_t halfPeriodMs = 1500;     // <-- set this (0.5 sec)

      // STRIP UP = strip1
      uint16_t upStart = 0;                                // <-- set this
      uint16_t upEnd   = 24;  // <-- set this

      // STRIP DOWN = strip2
      uint16_t dnStart = 4;                                // <-- set this
      uint16_t dnEnd   = 33-4;  // <-- set this

      uint32_t upCol = BLU_DAEVA;                          // <-- set this
      uint32_t dnCol = BLU_DAEVA;                          // <-- set this
      // ------------------------------------------------------------

      // Begin bouncing animation (runs for durMs total)
      bounceAnim_begin(
        durMs,
        halfPeriodMs,
        upStart, upEnd, upCol,
        dnStart, dnEnd, dnCol
      );
    }

    prevProgState = progState;
  }
  // ============================================================

  // Transitions
  if (progState == ST_ACTIVE) {
    if ((int32_t)(now - activeUntil) >= 0) {
      progState = ST_ENDING;
      endingUntil = now + EndStatusMs;
      nextFrameAt = 0;
      SerialUSB.println("DONE");
    }
  } else if (progState == ST_ENDING) {
    if ((int32_t)(now - endingUntil) >= 0) {
      progState = ST_START_WAIT;
      nextFrameAt = 0;

      // reset breathing for a clean wait animation
      breathePhase = 0;
      breatheStep = abs(breatheStep);
    }
  }

  // Frame scheduling
  if ((int32_t)(now - nextFrameAt) < 0) return;

  if (progState == ST_START_WAIT) {
    waitAnim_step(BLU_DAEVA);
    nextFrameAt = now + 40;
    return;
  }

  if (progState == ST_ACTIVE) {

    if (ACTIVE_ANIM == 1) {
      activeAnim1_step();

    } else if (ACTIVE_ANIM == 2) {
      activeAnim2_step();

    } else if (ACTIVE_ANIM == 3) {
      bounceAnim_step();      // <-- BOUNCING DOT animation
    }

    nextFrameAt = now + 33;  // ~30 FPS, non-blocking
    return;
  }

  // ST_ENDING
  if (END_ANIM == 1) endAnim1_step();
  else endAnim2_step();

  nextFrameAt = now + 80;
}



/* ---------------- Arduino setup/loop ---------------- */
void setup() {
  SerialUSB.begin(BAUD);

  // Init discrete LEDs LOW
  for (uint8_t i = 0; i < LED_COUNT; i++) {
    pinMode(ledPins[i], OUTPUT);
    digitalWrite(ledPins[i], LOW);
    offAtMs[i] = 0;
  }

  // Init WS2812
  strip1.begin();
  strip2.begin();
  strip1.setBrightness(255);
  strip2.setBrightness(255);

  // Start in WAIT state
  progState = ST_START_WAIT;
  nextFrameAt = 0;

  SerialUSB.println("Ready. Send: {\"P1:700\",\"P3:1000\",\"P5:500\"}");
}

void loop() {
  readSerialLineNonBlocking();
  updateLedTimers();
  updateStripsNonBlocking();
}
