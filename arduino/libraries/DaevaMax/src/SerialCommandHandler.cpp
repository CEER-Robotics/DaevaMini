#include "SerialCommandHandler.h"

#include <Arduino.h>
#include <ctype.h>
#include <stdlib.h>
#include <string.h>

#include "LedAnimations.h"
#include "ProjectConfig.h"
#include "PumpControl.h"

namespace {

static char rxLine[ProjectConfig::Serial::kRxBufferSize];
static size_t rxLen = 0;

static char* trimInPlace(char* s) {
  while (*s != '\0' && isspace((unsigned char)*s)) {
    s++;
  }

  char* end = s + strlen(s);
  while (end > s && isspace((unsigned char)*(end - 1))) {
    end--;
  }
  *end = '\0';
  return s;
}

static bool isExactCommand(const char* line, const char* cmd) {
  if (line == nullptr || cmd == nullptr) {
    return false;
  }

  char buf[sizeof(rxLine)];
  strncpy(buf, line, sizeof(buf) - 1);
  buf[sizeof(buf) - 1] = '\0';
  char* t = trimInPlace(buf);
  return strcmp(t, cmd) == 0;
}

// Parses a 0-255 byte value; returns true and sets *out if valid.
static bool parseByte(const char* s, uint8_t* out) {
  if (s == nullptr || out == nullptr) {
    return false;
  }
  char* end = nullptr;
  long n = strtol(s, &end, 10);
  if (end == s || *end != '\0') {
    return false;
  }
  if (n < 0 || n > 255) {
    return false;
  }
  *out = (uint8_t)n;
  return true;
}

// Extracts active color: either BASE/COLOR:<name> or RGB:r,g,b.
// For RGB, the value after the colon is r; the next two comma-separated tokens are g and b.
static bool extractCommandColor(const char* line,
                               const char* command,
                               char* outName,
                               size_t outNameLen,
                               uint8_t* outR,
                               uint8_t* outG,
                               uint8_t* outB,
                               bool* outIsRgb) {
  if (line == nullptr || outName == nullptr || outNameLen == 0 ||
      outR == nullptr || outG == nullptr || outB == nullptr ||
      outIsRgb == nullptr) {
    return false;
  }

  outName[0] = '\0';
  *outIsRgb = false;

  char buf[sizeof(rxLine)];
  strncpy(buf, line, sizeof(buf) - 1);
  buf[sizeof(buf) - 1] = '\0';

  char* tok = strtok(buf, ",");
  if (tok == nullptr) {
    return false;
  }

  tok = trimInPlace(tok);
  if (strcmp(tok, command) != 0) {
    return false;
  }

  while ((tok = strtok(nullptr, ",")) != nullptr) {
    char* field = trimInPlace(tok);
    char* sep = strchr(field, ':');
    if (sep == nullptr) {
      continue;
    }

    *sep = '\0';
    char* key = trimInPlace(field);
    char* val = trimInPlace(sep + 1);

    if (strcmp(key, "RGB") == 0) {
      uint8_t r = 0, g = 0, b = 0;
      if (!parseByte(val, &r)) {
        return false;
      }
      char* tokG = strtok(nullptr, ",");
      char* tokB = strtok(nullptr, ",");
      if (tokG == nullptr || tokB == nullptr) {
        return false;
      }
      if (!parseByte(trimInPlace(tokG), &g) || !parseByte(trimInPlace(tokB), &b)) {
        return false;
      }
      *outR = r;
      *outG = g;
      *outB = b;
      *outIsRgb = true;
      return true;
    }

    if (strcmp(key, "BASE") == 0 || strcmp(key, "COLOR") == 0) {
      if (*val == '\0') {
        return false;
      }
      strncpy(outName, val, outNameLen - 1);
      outName[outNameLen - 1] = '\0';
      *outIsRgb = false;
      return true;
    }
  }

  return false;
}

static bool isCommand(const char* line, const char* command) {
  if (line == nullptr) {
    return false;
  }

  char buf[sizeof(rxLine)];
  strncpy(buf, line, sizeof(buf) - 1);
  buf[sizeof(buf) - 1] = '\0';

  char* tok = strtok(buf, ",");
  if (tok == nullptr) {
    return false;
  }
  tok = trimInPlace(tok);
  return strcmp(tok, command) == 0;
}

// Turns a parsed color field into a packed color, whether it arrived as an RGB
// triple or as a preset name.
static bool resolveColor(bool isRgb,
                         uint8_t r,
                         uint8_t g,
                         uint8_t b,
                         const char* name,
                         uint32_t& out) {
  if (isRgb) {
    out = ((uint32_t)r << 16) | ((uint32_t)g << 8) | (uint32_t)b;
    return true;
  }
  return LedAnimations::parsePresetColor(name, out);
}

// First pump id in the line, as a bare "P<n>" with no duration. Tap mode does
// not carry one: the board decides how long to stay open, not the host.
static bool extractTapPump(const char* line, uint8_t& outPump) {
  for (const char* p = line; *p != '\0'; p++) {
    if (*p != 'P' && *p != 'p') {
      continue;
    }
    const char* d = p + 1;
    if (*d < '0' || *d > '9') {
      continue;
    }
    int n = 0;
    while (*d >= '0' && *d <= '9') {
      n = n * 10 + (*d - '0');
      d++;
    }
    if (n >= 1 && n <= (int)ProjectConfig::Pump::kCount) {
      outPump = (uint8_t)n;
      return true;
    }
  }
  return false;
}

// Re-arms one channel for the keep-alive window by handing PumpControl a line in
// the format it already validates, rather than adding a second scheduling path.
static bool armTapChannel(uint8_t pump) {
  char line[24];
  snprintf(line, sizeof(line), "P%u:%lu", (unsigned)pump,
           (unsigned long)ProjectConfig::Timing::kTapKeepAliveWindowMs);
  return PumpControl::scheduleFromLine(line) != 0;
}

// When the tap was opened, so the absolute ceiling can be enforced across
// keep-alives rather than being pushed forward by them.
static uint32_t tapOpenedAtMs = 0;
static bool tapOpen = false;

static void handleSerialLine(const char* line) {
  if (line == nullptr || line[0] == '\0') {
    return;
  }

  if (isExactCommand(line, "MANUTENZIONE")) {
    if (LedAnimations::handleMaintenanceCommand()) {
      PumpControl::allOff();
      DAEVA_SERIAL.println("OK MANUTENZIONE");
    }
    return;
  }

  if (isExactCommand(line, "READY")) {
    const bool wasWaiting = LedAnimations::isWaitingForCommand();
    if (LedAnimations::handleReadyCommand() && wasWaiting) {
      DAEVA_SERIAL.println("OK READY");
    }
    return;
  }

  if (isExactCommand(line, "TOXIC")) {
    if (LedAnimations::handleToxicCommand()) {
      PumpControl::allOff();
      DAEVA_SERIAL.println("OK TOXIC");
    }
    return;
  }

  if (isExactCommand(line, "TAP OFF")) {
    tapOpen = false;
    PumpControl::allOff();
    if (LedAnimations::finishActiveNow()) {
      DAEVA_SERIAL.println("OK TAP OFF");
    } else {
      DAEVA_SERIAL.println("IGNORED TAP");
    }
    return;
  }

  if (isCommand(line, "TAP")) {
    char tapName[32];
    uint8_t tr2 = 0, tg2 = 0, tb2 = 0;
    bool tapIsRgb = false;
    if (!extractCommandColor(line, "TAP", tapName, sizeof(tapName),
                             &tr2, &tg2, &tb2, &tapIsRgb)) {
      DAEVA_SERIAL.println("ERR TAP COLOR");
      return;
    }

    uint32_t tapColor = 0;
    if (!resolveColor(tapIsRgb, tr2, tg2, tb2, tapName, tapColor)) {
      DAEVA_SERIAL.println("ERR TAP COLOR");
      return;
    }

    uint8_t pump = 0;
    if (!extractTapPump(line, pump)) {
      DAEVA_SERIAL.println("ERR TAP PARAMS");
      return;
    }

    const uint32_t now = millis();
    const uint32_t until = now + ProjectConfig::Timing::kTapKeepAliveWindowMs;

    // A keep-alive for a tap already open: re-arm, but never past the ceiling.
    if (tapOpen && LedAnimations::extendActive(until)) {
      if ((uint32_t)(now - tapOpenedAtMs) >= ProjectConfig::Timing::kTapMaxOpenMs) {
        tapOpen = false;
        PumpControl::allOff();
        LedAnimations::finishActiveNow();
        DAEVA_SERIAL.println("ERR TAP TIMEOUT");
        return;
      }
      if (!armTapChannel(pump)) {
        DAEVA_SERIAL.println("ERR TAP PARAMS");
        return;
      }
      DAEVA_SERIAL.println("OK TAP");
      return;
    }

    // Opening for the first time: same gate as ACTIVE, only from WAIT.
    if (!LedAnimations::isWaitingForCommand()) {
      DAEVA_SERIAL.println("IGNORED TAP");
      return;
    }

    PumpControl::allOff();
    if (!armTapChannel(pump)) {
      DAEVA_SERIAL.println("ERR TAP PARAMS");
      return;
    }

    if (!LedAnimations::startActiveWithColor(until, tapColor)) {
      PumpControl::allOff();
      DAEVA_SERIAL.println("IGNORED TAP");
      return;
    }

    tapOpen = true;
    tapOpenedAtMs = now;
    DAEVA_SERIAL.println("OK TAP");
    return;
  }

  if (isExactCommand(line, "TINT OFF")) {
    if (LedAnimations::clearIdleTint()) {
      DAEVA_SERIAL.println("OK TINT");
    } else {
      DAEVA_SERIAL.println("IGNORED TINT");
    }
    return;
  }

  if (isCommand(line, "TINT")) {
    char tintName[32];
    uint8_t tr = 0, tg = 0, tb = 0;
    bool tintIsRgb = false;
    if (!extractCommandColor(line, "TINT", tintName, sizeof(tintName),
                             &tr, &tg, &tb, &tintIsRgb)) {
      DAEVA_SERIAL.println("ERR TINT COLOR");
      return;
    }

    uint32_t tintColor = 0;
    if (!resolveColor(tintIsRgb, tr, tg, tb, tintName, tintColor)) {
      DAEVA_SERIAL.println("ERR TINT COLOR");
      return;
    }

    // Only the idle strips can be retinted; anywhere else they are busy saying
    // something more important than which screen the host is on.
    if (!LedAnimations::setIdleTint(tintColor)) {
      DAEVA_SERIAL.println("IGNORED TINT");
      return;
    }

    DAEVA_SERIAL.println("OK TINT");
    return;
  }

  if (!isCommand(line, "ACTIVE")) {
    return;
  }

  char colorName[32];
  uint8_t rgbR = 0, rgbG = 0, rgbB = 0;
  bool isRgb = false;
  if (!extractCommandColor(line, "ACTIVE", colorName, sizeof(colorName),
                           &rgbR, &rgbG, &rgbB, &isRgb)) {
    DAEVA_SERIAL.println("ERR ACTIVE COLOR");
    return;
  }

  if (!LedAnimations::isWaitingForCommand()) {
    DAEVA_SERIAL.println("IGNORED ACTIVE");
    return;
  }

  uint32_t selectedColor = 0;
  if (!resolveColor(isRgb, rgbR, rgbG, rgbB, colorName, selectedColor)) {
    DAEVA_SERIAL.println("ERR ACTIVE COLOR");
    return;
  }

  PumpControl::allOff();
  const uint32_t maxEnd = PumpControl::scheduleFromLine(line);
  if (maxEnd == 0) {
    DAEVA_SERIAL.println("ERR ACTIVE PARAMS");
    return;
  }

  if (LedAnimations::startActiveWithColor(maxEnd, selectedColor)) {
    DAEVA_SERIAL.println("OK ACTIVE");
  }
}

}  // namespace

namespace SerialCommandHandler {

void begin() {
  rxLen = 0;
  rxLine[0] = '\0';
}

void update() {
  while (DAEVA_SERIAL.available() > 0) {
    char c = (char)DAEVA_SERIAL.read();
    if (c == '\r') {
      continue;
    }

    if (c == '\n') {
      rxLine[rxLen] = '\0';

      if (rxLen > 0) {
        handleSerialLine(rxLine);
      }

      rxLen = 0;
      return;
    }

    if (rxLen < sizeof(rxLine) - 1) {
      rxLine[rxLen++] = c;
    } else {
      rxLen = 0;
    }
  }
}

}  // namespace SerialCommandHandler
