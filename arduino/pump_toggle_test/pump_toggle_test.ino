// Diagnostic sketch: drive ONE pump/valve channel as a slow square wave, so a
// solenoid can be heard and measured actuating without involving the host, the
// serial protocol or the LED state machine.
//
// The production path is deliberately not used here: an ACTIVE command runs the
// channel for a set time and then walks the board through ENDING before it will
// accept another, so it cannot produce a clean, repeating on/off of equal length.
// This can.
//
// Set kTestChannel to the channel under test - the same numbering the protocol
// and LiquidAssignments use, where P1 is the first channel. The pin comes from
// the real board profile, so it stays correct if the pin map is ever changed.
//
// Every other channel is driven low and held there for the duration, so only the
// channel being tested can move. Note pin 13 is the Teensy's onboard LED and is
// ALSO pump 8 on this board: there is deliberately no blinking heartbeat here.
//
// Not for production. Flash arduino/teensy_max back when finished, with the usual
// command from the root CLAUDE.md.

#include <ProjectConfig.h>

namespace {

// Channel under test, 1-based (P11 = the first keg line on this machine).
constexpr uint8_t kTestChannel = 11;

// Half-period: how long the channel stays on, and then off.
constexpr uint32_t kPhaseMs = 5000;

static_assert(kTestChannel >= 1 && kTestChannel <= ProjectConfig::Pump::kCount,
              "kTestChannel must be a valid pump channel");

constexpr uint8_t kTestPin = BoardConfig::Pump::kPins[kTestChannel - 1];

}  // namespace

void setup() {
  // Same setup order PumpControl::begin() uses, so the pins end up in exactly the
  // state production puts them in.
  BoardConfig::Pump::configurePwm();
  for (uint8_t i = 0; i < ProjectConfig::Pump::kCount; i++) {
    pinMode(BoardConfig::Pump::kPins[i], OUTPUT);
    analogWrite(BoardConfig::Pump::kPins[i], 0);
  }
}

void loop() {
  // analogWrite at kDefaultPwm (255), not digitalWrite: that is what PumpControl
  // does, and on a Teensy 4.x the two go through different peripherals - the PWM
  // timer versus plain GPIO. Driving the pin with digitalWrite was measured on
  // this machine not to actuate the valve, so the test has to use the same call
  // production uses or it is testing something else.
  analogWrite(kTestPin, ProjectConfig::Pump::kDefaultPwm);
  delay(kPhaseMs);
  analogWrite(kTestPin, 0);
  delay(kPhaseMs);
}
