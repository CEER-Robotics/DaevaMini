#include "PumpControl.h"

namespace {

static const uint8_t kPumpCount = 17;
static const uint8_t kPwmValue = 110;
static const uint8_t kPumpPins[kPumpCount] = {
    6, 7, 8, 9, 10, 11, 12, 13, 22, 24, 26, 28, 30, 32, 34, 36, 38};

static uint32_t offAtMs[kPumpCount];

static inline void pumpOff(uint8_t idx) {
  analogWrite(kPumpPins[idx], 0);
  offAtMs[idx] = 0;
}

static inline void pumpOnFor(uint8_t idx, uint32_t durationMs) {
  analogWrite(kPumpPins[idx], kPwmValue);
  offAtMs[idx] = millis() + durationMs;
}

}  // namespace

namespace PumpControl {

void begin() {
  for (uint8_t i = 0; i < kPumpCount; i++) {
    pinMode(kPumpPins[i], OUTPUT);
    digitalWrite(kPumpPins[i], LOW);
    offAtMs[i] = 0;
  }
}

void allOff() {
  for (uint8_t i = 0; i < kPumpCount; i++) {
    analogWrite(kPumpPins[i], 0);
    offAtMs[i] = 0;
  }
}

void update() {
  const uint32_t now = millis();
  for (uint8_t i = 0; i < kPumpCount; i++) {
    if (offAtMs[i] != 0 && (int32_t)(now - offAtMs[i]) >= 0) {
      pumpOff(i);
    }
  }
}

uint32_t scheduleFromLine(const char* s) {
  uint32_t maxEnd = 0;

  while (*s) {
    if (*s == 'P' || *s == 'p') {
      s++;

      int pumpNum = 0;
      while (*s >= '0' && *s <= '9') {
        pumpNum = pumpNum * 10 + (*s - '0');
        s++;
      }
      if (pumpNum < 1 || pumpNum > (int)kPumpCount) {
        continue;
      }

      while (*s && *s != ':') {
        s++;
      }
      if (*s != ':') {
        continue;
      }
      s++;

      uint32_t dur = 0;
      bool hasDigits = false;
      while (*s >= '0' && *s <= '9') {
        hasDigits = true;
        dur = dur * 10u + (uint32_t)(*s - '0');
        s++;
      }
      if (!hasDigits) {
        continue;
      }

      const uint8_t idx = (uint8_t)(pumpNum - 1);
      if (dur == 0) {
        pumpOff(idx);
      } else {
        pumpOnFor(idx, dur);
        if (offAtMs[idx] > maxEnd) {
          maxEnd = offAtMs[idx];
        }
      }
    } else {
      s++;
    }
  }

  return maxEnd;
}

}  // namespace PumpControl

