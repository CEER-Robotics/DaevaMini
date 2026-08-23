// Diagnostic sketch: which protocol do these strips actually speak?
//
// The firmware drives them as WS2812: NEO_GRB + NEO_KHZ800, 31 and 44 pixels.
// That assumption comes from the notes, but the strips run at 24 V, and a 24 V
// addressable strip is not a WS2812 - it is a WS2811/UCS1903 family part where
// one controller drives a SEGMENT of several LEDs in series, and where 400 kHz
// is common. Sending the wrong bit rate, or the wrong colour order, produces
// exactly the reported symptom: a wrong image that then sits perfectly still
// until the next transmission.
//
// Three unknowns are separated here:
//
//   1. Bit rate     - 800 kHz vs 400 kHz
//   2. Colour order - GRB vs RGB
//   3. Length       - every variant addresses kProbeLen segments, far more than
//                     either strip can hold, so segments left dark or stale by a
//                     too-short configured length cannot be mistaken for a
//                     protocol fault. Data past the last controller is simply
//                     dropped by the strip, so over-addressing is harmless.
//
// Watch for the block where RED looks red, GREEN looks green, BLUE looks blue,
// and every segment of the strip agrees. That block names the right settings.
//
// Not for production: flash the normal firmware back when finished.

#include <Adafruit_NeoPixel.h>
#include <ProjectConfig.h>

namespace {

// Deliberately far longer than either strip. Cost is 3 bytes per segment.
constexpr uint16_t kProbeLen = 300;

constexpr uint32_t kColorMs = 2500;
constexpr uint32_t kGapMs = 2000;

Adafruit_NeoPixel strip1(kProbeLen,
                         BoardConfig::Strips::kStrip1Pin,
                         NEO_GRB + NEO_KHZ800);
Adafruit_NeoPixel strip2(kProbeLen,
                         BoardConfig::Strips::kStrip2Pin,
                         NEO_GRB + NEO_KHZ800);

struct Variant {
  const char* name;
  neoPixelType type;
};

const Variant kVariants[] = {
    {"A: 800 kHz, GRB  (what the firmware uses today)", NEO_GRB + NEO_KHZ800},
    {"B: 800 kHz, RGB", NEO_RGB + NEO_KHZ800},
    {"C: 400 kHz, GRB", NEO_GRB + NEO_KHZ400},
    {"D: 400 kHz, RGB", NEO_RGB + NEO_KHZ400},
};
constexpr uint8_t kVariantCount = sizeof(kVariants) / sizeof(kVariants[0]);

// Full brightness: this test is about whether the bits arrive, so nothing is
// scaled down. Note this draws more current than the machine normally does.
void fillBoth(uint8_t r, uint8_t g, uint8_t b) {
  for (uint16_t i = 0; i < kProbeLen; i++) {
    strip1.setPixelColor(i, strip1.Color(r, g, b));
    strip2.setPixelColor(i, strip2.Color(r, g, b));
  }
  strip1.show();
  strip2.show();
}

void holdColor(const char* label, uint8_t r, uint8_t g, uint8_t b, uint32_t ms) {
  Serial.print("    expect ");
  Serial.println(label);

  // Re-transmitted continuously: if the image is right but unstable, that is a
  // signalling problem on top of whatever the protocol turns out to be.
  const uint32_t until = millis() + ms;
  while ((int32_t)(millis() - until) < 0) {
    fillBoth(r, g, b);
    delay(50);
  }
}

void runVariant(const Variant& v) {
  Serial.println(v.name);

  strip1.updateType(v.type);
  strip2.updateType(v.type);

  holdColor("RED", 255, 0, 0, kColorMs);
  holdColor("GREEN", 0, 255, 0, kColorMs);
  holdColor("BLUE", 0, 0, 255, kColorMs);

  // Dark gap so the four blocks are easy to tell apart and to count.
  for (uint16_t i = 0; i < kProbeLen; i++) {
    strip1.setPixelColor(i, 0);
    strip2.setPixelColor(i, 0);
  }
  strip1.show();
  strip2.show();
  delay(kGapMs);
}

}  // namespace

void setup() {
  Serial.begin(115200);

  strip1.begin();
  strip2.begin();
  // No global dimming here, unlike production.
  strip1.setBrightness(255);
  strip2.setBrightness(255);
  strip1.clear();
  strip2.clear();
  strip1.show();
  strip2.show();

  Serial.println();
  Serial.println("LED protocol probe.");
  Serial.print("Addressing ");
  Serial.print(kProbeLen);
  Serial.println(" segments per strip (far more than configured).");
  Serial.print("Firmware currently assumes ");
  Serial.print(ProjectConfig::Strips::kStrip1Len);
  Serial.print(" and ");
  Serial.print(ProjectConfig::Strips::kStrip2Len);
  Serial.println(" pixels, GRB, 800 kHz.");
  Serial.println("Four blocks of RED/GREEN/BLUE, 2 s dark between them.");
  Serial.println();
}

void loop() {
  for (uint8_t i = 0; i < kVariantCount; i++) {
    runVariant(kVariants[i]);
  }
  Serial.println("--- cycle complete, repeating ---");
  Serial.println();
}
