#include "PumpControl.h"
#include "ProjectConfig.h"

namespace {

static uint32_t offAtMs[ProjectConfig::Pump::kCount];

static inline void pumpOff(uint8_t idx) {
  analogWrite(ProjectConfig::Pump::kPins[idx], 0);
  offAtMs[idx] = 0;
}

static inline void pumpOnFor(uint8_t idx, uint32_t durationMs) {
  analogWrite(ProjectConfig::Pump::kPins[idx], ProjectConfig::Pump::kDefaultPwm);
  offAtMs[idx] = millis() + durationMs;
}

}  // namespace

namespace PumpControl {

void begin() {
  for (uint8_t i = 0; i < ProjectConfig::Pump::kCount; i++) {
    pinMode(ProjectConfig::Pump::kPins[i], OUTPUT);
    digitalWrite(ProjectConfig::Pump::kPins[i], LOW);
    offAtMs[i] = 0;
  }
}

void allOff() {
  for (uint8_t i = 0; i < ProjectConfig::Pump::kCount; i++) {
    analogWrite(ProjectConfig::Pump::kPins[i], 0);
    offAtMs[i] = 0;
  }
}

void update() {
  const uint32_t now = millis();
  for (uint8_t i = 0; i < ProjectConfig::Pump::kCount; i++) {
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
      if (pumpNum < 1 || pumpNum > (int)ProjectConfig::Pump::kCount) {
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
