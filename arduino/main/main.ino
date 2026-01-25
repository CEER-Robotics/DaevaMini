#include <Arduino.h>
#include <Adafruit_NeoPixel.h>

/* ===================== USER PARAMETERS ===================== */
static const uint32_t BAUD = 9600;

// Discrete LEDs (LED1..LED17)
static const uint8_t LED_COUNT = 17;
static const uint8_t ledPins[LED_COUNT] = {
  6, 7, 8, 9, 10, 11, 12, 13, 22, 24, 26, 28, 30, 32, 34, 36, 38
};

// Mega: PWM is 0..255 and only on PWM pins (2–13, 44–46). Others act ON/OFF.
static const uint8_t PWM_VALUE = 180;

// WS2812 strips
static const uint8_t STRIP1_PIN = 2;
static const uint8_t STRIP2_PIN = 3;
static const uint16_t STRIP1_N = 16;
static const uint16_t STRIP2_N = 16;

// Colors (parameterized)
static const uint8_t CYAN_R = 0,   CYAN_G = 255, CYAN_B = 255;
static const uint8_t YELL_R = 255, YELL_G = 255, YELL_B = 0;

// How long to run the “end status” animation after the last LED switches off
static const uint32_t EndStatusMs = 3000;

// Choose which ACTIVE animation to run (1 or 2)
static const uint8_t ACTIVE_ANIM = 1;
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
  while (Serial.available() > 0) {
    char c = (char)Serial.read();
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
static void waitAnim_step(uint32_t breatheColor) {
  breatheFill_step(breatheColor);
}

// ACTIVE ANIM 1: two-dot chase cyan/yellow
static void activeAnim1_step() {
  uint32_t CYAN = strip1.Color(CYAN_R, CYAN_G, CYAN_B);
  uint32_t YELL = strip1.Color(YELL_R, YELL_G, YELL_B);

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
  uint32_t CYAN = strip1.Color(CYAN_R, CYAN_G, CYAN_B);
  uint32_t YELL = strip2.Color(YELL_R, YELL_G, YELL_B);

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
  uint32_t CYAN = strip1.Color(CYAN_R, CYAN_G, CYAN_B);
  uint32_t YELL = strip2.Color(YELL_R, YELL_G, YELL_B);

  for (uint16_t i = 0; i < strip1.numPixels(); i++) strip1.setPixelColor(i, CYAN);
  for (uint16_t i = 0; i < strip2.numPixels(); i++) strip2.setPixelColor(i, YELL);

  strip1.show();
  strip2.show();
}

// END ANIM 2: alternating blocks cyan/yellow scrolling
static void endAnim2_step() {
  uint32_t CYAN = strip1.Color(CYAN_R, CYAN_G, CYAN_B);
  uint32_t YELL = strip1.Color(YELL_R, YELL_G, YELL_B);

  const uint8_t block = 4;
  for (uint16_t i = 0; i < strip1.numPixels(); i++) {
    bool sel = ((i + altPos) / block) % 2;
    strip1.setPixelColor(i, sel ? CYAN : YELL);
  }
  for (uint16_t i = 0; i < strip2.numPixels(); i++) {
    bool sel = ((i + altPos) / block) % 2;
    strip2.setPixelColor(i, sel ? YELL : CYAN);
  }

  strip1.show();
  strip2.show();
  altPos++;
}

// Main strip update (non-blocking, state-driven)
static void updateStripsNonBlocking() {
  uint32_t now = millis();

  // Transitions
  if (progState == ST_ACTIVE) {
    if ((int32_t)(now - activeUntil) >= 0) {
      progState = ST_ENDING;
      endingUntil = now + EndStatusMs;
      nextFrameAt = 0;
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
    // Pass ANY color here (variable)
    uint32_t waitColor = strip1.Color(CYAN_R, CYAN_G, CYAN_B);
    waitAnim_step(waitColor);
    nextFrameAt = now + 40;
    return;
  }

  if (progState == ST_ACTIVE) {
    if (ACTIVE_ANIM == 1) activeAnim1_step();
    else activeAnim2_step();
    nextFrameAt = now + 33;
    return;
  }

  // ST_ENDING
  if (END_ANIM == 1) endAnim1_step();
  else endAnim2_step();
  nextFrameAt = now + 80;
}

/* ---------------- Arduino setup/loop ---------------- */
void setup() {
  Serial.begin(BAUD);

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

  Serial.println("Ready. Send: {\"P1:700\",\"P3:1000\",\"P5:500\"}");
}

void loop() {
  readSerialLineNonBlocking();
  updateLedTimers();
  updateStripsNonBlocking();
}
