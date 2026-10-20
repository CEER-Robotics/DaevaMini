// Diagnostic sketch: how many LEDs does each strip physically have, and does the
// chain break somewhere?
//
// Both strips are addressed with kProbeLen pixels, far more than either can hold.
// Over-addressing is harmless: data past the last controller is simply dropped by
// the strip, so nothing is damaged and nothing is misread.
//
// The pattern is built for counting rather than for looking nice:
//
//   every 10th LED (0, 10, 20, ...) is RED
//   the one right after each marker (1, 11, 21, ...) is BLUE
//   everything else is dim WHITE
//
// So the reds are decade markers. Count them: 4 reds and a partial run after the
// fourth means 30-something LEDs. The blue after each red tells you which way the
// data flows - blue is always on the far side of red from the controller.
//
// How to read the result:
//   - Count the lit LEDs. That is the true physical length; put it in
//     ProjectConfig.h as kStrip1Len / kStrip2Len.
//   - If the lit run stops part way and the rest stays dark no matter how long
//     kProbeLen is, the chain is broken at that point: the first dark LED is the
//     dead one. No firmware value will bring the rest back - that LED has to be
//     replaced.
//   - If the tail flickers instead of staying dark, that is the old unshifted
//     data-line fault, not a length problem.
//
// Brightness is deliberately low so a sagging supply cannot dim the far end and
// be mistaken for a chain break.
//
// Not for production. Flash arduino/teensy_max back when finished.

#include <Adafruit_NeoPixel.h>
#include <ProjectConfig.h>

namespace {

// Comfortably longer than either strip is believed to be.
constexpr uint16_t kProbeLen = 80;

// Low enough that 80 lit LEDs cannot brown out the rail.
constexpr uint8_t kBrightness = 24;

Adafruit_NeoPixel strip1(kProbeLen, BoardConfig::Strips::kStrip1Pin,
                         NEO_RGB + NEO_KHZ800);
Adafruit_NeoPixel strip2(kProbeLen, BoardConfig::Strips::kStrip2Pin,
                         NEO_RGB + NEO_KHZ800);

void paint(Adafruit_NeoPixel& s) {
  for (uint16_t i = 0; i < kProbeLen; i++) {
    if (i % 10 == 0) {
      s.setPixelColor(i, s.Color(255, 0, 0));        // decade marker
    } else if (i % 10 == 1) {
      s.setPixelColor(i, s.Color(0, 0, 255));        // direction marker
    } else {
      s.setPixelColor(i, s.Color(60, 60, 60));       // dim white filler
    }
  }
  s.show();
}

}  // namespace

void setup() {
  // Pumps off and staying off: nothing here should ever move a valve, and pin 13
  // is pump 8 on this board.
  BoardConfig::Pump::configurePwm();
  for (uint8_t i = 0; i < ProjectConfig::Pump::kCount; i++) {
    pinMode(BoardConfig::Pump::kPins[i], OUTPUT);
    analogWrite(BoardConfig::Pump::kPins[i], 0);
  }

  strip1.begin();
  strip2.begin();
  strip1.setBrightness(kBrightness);
  strip2.setBrightness(kBrightness);
  paint(strip1);
  paint(strip2);
}

void loop() {
  // Painted once and left alone. A static image is the point: anything that
  // changes after this is a transmission fault, not the pattern.
}
