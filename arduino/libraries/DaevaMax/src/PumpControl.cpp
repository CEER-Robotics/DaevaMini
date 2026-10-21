#include "PumpControl.h"
#include "ProjectConfig.h"

namespace {

static_assert(BoardConfig::Pump::kPins.size() == ProjectConfig::Pump::kCount,
              "The board profile must define exactly 17 pump pins.");

static uint32_t offAtMs[ProjectConfig::Pump::kCount];

static inline void pumpOff(uint8_t idx) {
  analogWrite(BoardConfig::Pump::kPins[idx], 0);
  offAtMs[idx] = 0;
}

static inline void pumpOnFor(uint8_t idx, uint32_t durationMs, uint8_t duty) {
  analogWrite(BoardConfig::Pump::kPins[idx], duty);
  offAtMs[idx] = millis() + durationMs;
}

}  // namespace

namespace PumpControl {

void begin() {
  BoardConfig::Pump::configurePwm();
  for (uint8_t i = 0; i < ProjectConfig::Pump::kCount; i++) {
    pinMode(BoardConfig::Pump::kPins[i], OUTPUT);
    digitalWrite(BoardConfig::Pump::kPins[i], LOW);
    offAtMs[i] = 0;
  }
}

void allOff() {
  for (uint8_t i = 0; i < ProjectConfig::Pump::kCount; i++) {
    analogWrite(BoardConfig::Pump::kPins[i], 0);
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

      // Optional "@<duty>" after the duration, 1-255: how hard this channel is
      // driven. The bottle pumps spray when run flat out, so the host slows them;
      // the keg valves never carry it and stay at the default. Anything missing or
      // out of range falls back to the default rather than refusing the segment,
      // so the pour still happens at the power every older host already used.
      uint8_t duty = ProjectConfig::Pump::kDefaultPwm;
      if (*s == '@') {
        s++;
        uint32_t requested = 0;
        bool hasDuty = false;
        while (*s >= '0' && *s <= '9') {
          hasDuty = true;
          if (requested <= 255u) {
            requested = requested * 10u + (uint32_t)(*s - '0');
          }
          s++;
        }
        if (hasDuty && requested >= 1u && requested <= 255u) {
          duty = (uint8_t)requested;
        }
      }

      const uint8_t idx = (uint8_t)(pumpNum - 1);
      if (dur == 0) {
        pumpOff(idx);
      } else {
        pumpOnFor(idx, dur, duty);
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
