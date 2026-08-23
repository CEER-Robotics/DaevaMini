// Diagnostic sketch: is the WS2812 corruption caused by TRANSMITTING, or is it
// there regardless?
//
// Background: the data lines are driven straight from the Teensy at 3.3 V,
// below the 0.7 x VDD = 3.5 V threshold of a 5 V WS2812, so every show() is a
// chance to latch a wrong bit. That predicts corruption that scales with the
// number of show() calls. A failing power rail predicts the opposite: it scales
// with how many LEDs are lit, and is worst at full brightness.
//
// The two are separated by phases 1 and 3 below. They paint the SAME image, so
// they draw the same current; the only difference is that phase 1 transmits it
// once and phase 3 transmits it thirty times a second. If phase 3 is dirty and
// phase 1 is clean, the transmissions are the cause and power is exonerated.
//
// Phases are announced by white blinks (1 blink = phase 1) and, if a USB serial
// monitor is attached, by name. Not for production: flash the normal firmware
// back when finished.

#include <Adafruit_NeoPixel.h>

// Pins, lengths and brightness come from the real configuration, so the test
// runs under exactly the conditions production does.
#include <ProjectConfig.h>

namespace {

Adafruit_NeoPixel strip1(ProjectConfig::Strips::kStrip1Len,
                         BoardConfig::Strips::kStrip1Pin,
                         NEO_GRB + NEO_KHZ800);
Adafruit_NeoPixel strip2(ProjectConfig::Strips::kStrip2Len,
                         BoardConfig::Strips::kStrip2Pin,
                         NEO_GRB + NEO_KHZ800);

// The idle color the machine shows when it is static and correct, so the test
// starts from the case already known to be good.
constexpr uint8_t kR = 202;
constexpr uint8_t kG = 240;
constexpr uint8_t kB = 248;

constexpr uint32_t kPhaseMs = 15000;
constexpr uint8_t kPhaseCount = 6;

uint32_t idleColor1 = 0;
uint32_t idleColor2 = 0;

void fill(Adafruit_NeoPixel& s, uint32_t color) {
  for (uint16_t i = 0; i < s.numPixels(); i++) {
    s.setPixelColor(i, color);
  }
}

void showBoth() {
  strip1.show();
  strip2.show();
}

void paintIdle() {
  fill(strip1, idleColor1);
  fill(strip2, idleColor2);
}

// Announces the phase number, then leaves the strips dark for a moment so the
// start of the phase is unmistakable.
void marker(uint8_t phase) {
  const uint32_t white1 = strip1.Color(255, 255, 255);
  const uint32_t white2 = strip2.Color(255, 255, 255);

  for (uint8_t i = 0; i < phase; i++) {
    fill(strip1, white1);
    fill(strip2, white2);
    showBoth();
    delay(120);
    strip1.clear();
    strip2.clear();
    showBoth();
    delay(200);
  }
  delay(600);
}

void announce(uint8_t phase, const char* what) {
  Serial.print("PHASE ");
  Serial.print(phase);
  Serial.print(": ");
  Serial.println(what);
  marker(phase);
}

// Paints once and then does nothing at all for the whole phase. Any change you
// see here happened with zero transmissions.
void phaseStaticOnce() {
  announce(1, "static, transmitted ONCE (watch for drift with no show())");
  paintIdle();
  showBoth();
  delay(kPhaseMs);
}

// Same image, re-transmitted slowly. Glitches that appear only at the moments
// of refresh point at the transmission.
void phaseSlowRefresh() {
  announce(2, "same image, 1 refresh every 2 s");
  const uint32_t until = millis() + kPhaseMs;
  while ((int32_t)(millis() - until) < 0) {
    paintIdle();
    showBoth();
    delay(2000);
  }
}

// The control that matters: identical image and identical current draw to
// phase 1, but ~30 transmissions per second.
void phaseFastRefresh() {
  announce(3, "SAME image as phase 1, 30 refreshes/s - the decisive one");
  const uint32_t until = millis() + kPhaseMs;
  while ((int32_t)(millis() - until) < 0) {
    paintIdle();
    showBoth();
    delay(33);
  }
}

// The real animation, to confirm it behaves like phase 3 and not worse.
void phasePulse() {
  announce(4, "breathing animation, 30 refreshes/s");
  const uint32_t start = millis();
  const uint32_t until = start + kPhaseMs;
  while ((int32_t)(millis() - until) < 0) {
    const uint32_t phase = (millis() - start) % 2600;
    const uint32_t half = 1300;
    const float tri = (phase < half) ? ((float)phase / (float)half)
                                     : ((float)(2600 - phase) / (float)half);
    const float eased = tri * tri * (3.0f - 2.0f * tri);
    const uint8_t b = (uint8_t)(25 + (uint8_t)(230.0f * eased + 0.5f));

    const uint32_t c1 = strip1.Color((uint8_t)(kR * b / 255),
                                     (uint8_t)(kG * b / 255),
                                     (uint8_t)(kB * b / 255));
    const uint32_t c2 = strip2.Color((uint8_t)(kR * b / 255),
                                     (uint8_t)(kG * b / 255),
                                     (uint8_t)(kB * b / 255));
    fill(strip1, c1);
    fill(strip2, c2);
    showBoth();
    delay(33);
  }
}

// Which strip is intolerant: the notes say strip 1 misbehaves and strip 2 does
// not, which would mean different LED revisions. Worth confirming before
// buying parts for one line or for both.
void phaseOneStrip(uint8_t phase, bool useStrip1) {
  announce(phase, useStrip1 ? "strip 1 ONLY, 30 refreshes/s"
                            : "strip 2 ONLY, 30 refreshes/s");
  const uint32_t until = millis() + kPhaseMs;
  while ((int32_t)(millis() - until) < 0) {
    if (useStrip1) {
      fill(strip1, idleColor1);
      strip2.clear();
    } else {
      strip1.clear();
      fill(strip2, idleColor2);
    }
    showBoth();
    delay(33);
  }
}

}  // namespace

void setup() {
  Serial.begin(115200);

  strip1.begin();
  strip2.begin();
  strip1.setBrightness(ProjectConfig::Animation::kGlobalBrightness);
  strip2.setBrightness(ProjectConfig::Animation::kGlobalBrightness);
  strip1.clear();
  strip2.clear();
  strip1.show();
  strip2.show();

  idleColor1 = strip1.Color(kR, kG, kB);
  idleColor2 = strip2.Color(kR, kG, kB);

  Serial.println("LED signal diagnostic - 6 phases, 15 s each, looping.");
}

void loop() {
  phaseStaticOnce();
  phaseSlowRefresh();
  phaseFastRefresh();
  phasePulse();
  phaseOneStrip(5, true);
  phaseOneStrip(6, false);
}
