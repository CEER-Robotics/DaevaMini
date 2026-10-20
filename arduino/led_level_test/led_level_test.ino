// Diagnostic sketch: does the level converter fitted on the WS2812 data lines
// actually translate 3.3 V up to 5 V?
//
// Both strip data pins are parked at a steady logic HIGH so the converter's
// output side can be read with a multimeter, with nothing else driving them.
//
// What to expect on the OUTPUT side of the converter:
//   ~5 V      the converter is working; the 0.7 x VDD = 3.5 V WS2812 threshold
//             is now cleared with margin.
//   ~3.3 V    it is passing the input straight through - typically the Teensy
//             wired to the B (high) side instead of A, VCCA not fed from 3.3 V,
//             or OE left floating instead of pulled high.
//   ~0 V      no output at all - check VCCB, ground continuity between the
//             converter and the Teensy, and the channel actually used.
//
// LIMIT, and it matters: this proves the DC path only. A converter can hold a
// steady level correctly and still fail at the WS2812 bit rate, where each bit
// lasts ~1.25 us - slew rate and propagation delay do not show up in a static
// measurement. A clean 5 V here is necessary but not sufficient. Confirm with
// the real firmware afterwards, and with a scope on the edges if you have one.
//
// The pumps are explicitly held off for the whole test. This is not paranoia:
// pin 13 is the Teensy's onboard LED and would be the obvious place for a
// heartbeat blink, but on this board it is ALSO pump 8, so nothing here may
// touch it.
//
// Not for production. Flash arduino/teensy_max back when finished, with the
// usual command from the root CLAUDE.md (compile -u onto the teensy-protocol
// port, --libraries arduino/libraries, sketch arduino/teensy_max).

#include <ProjectConfig.h>

void setup() {
  // Every pump driven low and left there, so a board sitting on the bench with
  // its tubing connected cannot pour while somebody is holding a multimeter.
  for (uint8_t i = 0; i < ProjectConfig::Pump::kCount; i++) {
    pinMode(BoardConfig::Pump::kPins[i], OUTPUT);
    digitalWrite(BoardConfig::Pump::kPins[i], LOW);
  }

  // LED1 and LED2 data lines: steady HIGH, nothing else driving them.
  pinMode(BoardConfig::Strips::kStrip1Pin, OUTPUT);
  pinMode(BoardConfig::Strips::kStrip2Pin, OUTPUT);
  digitalWrite(BoardConfig::Strips::kStrip1Pin, HIGH);
  digitalWrite(BoardConfig::Strips::kStrip2Pin, HIGH);
}

void loop() {
  // Deliberately empty: the point of the test is that nothing changes.
}
