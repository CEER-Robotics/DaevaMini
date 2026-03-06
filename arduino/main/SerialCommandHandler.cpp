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
static bool extractActiveColor(const char* line,
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
  if (strcmp(tok, "ACTIVE") != 0) {
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

static bool isActiveCommand(const char* line) {
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
  return strcmp(tok, "ACTIVE") == 0;
}

static void handleSerialLine(const char* line) {
  if (line == nullptr || line[0] == '\0') {
    return;
  }

  if (isExactCommand(line, "MANUTENZIONE")) {
    if (LedAnimations::handleMaintenanceCommand()) {
      PumpControl::allOff();
      SerialUSB.println("OK MANUTENZIONE");
    }
    return;
  }

  if (isExactCommand(line, "READY")) {
    const bool wasWaiting = LedAnimations::isWaitingForCommand();
    if (LedAnimations::handleReadyCommand() && wasWaiting) {
      SerialUSB.println("OK READY");
    }
    return;
  }

  if (isExactCommand(line, "TOXIC")) {
    if (LedAnimations::handleToxicCommand()) {
      PumpControl::allOff();
      SerialUSB.println("OK TOXIC");
    }
    return;
  }

  if (!isActiveCommand(line)) {
    return;
  }

  char colorName[32];
  uint8_t rgbR = 0, rgbG = 0, rgbB = 0;
  bool isRgb = false;
  if (!extractActiveColor(line, colorName, sizeof(colorName),
                          &rgbR, &rgbG, &rgbB, &isRgb)) {
    SerialUSB.println("ERR ACTIVE COLOR");
    return;
  }

  if (!LedAnimations::isWaitingForCommand()) {
    SerialUSB.println("IGNORED ACTIVE");
    return;
  }

  uint32_t selectedColor = 0;
  if (isRgb) {
    selectedColor = ((uint32_t)rgbR << 16) | ((uint32_t)rgbG << 8) | (uint32_t)rgbB;
  } else if (!LedAnimations::parsePresetColor(colorName, selectedColor)) {
    SerialUSB.println("ERR ACTIVE COLOR");
    return;
  }

  PumpControl::allOff();
  const uint32_t maxEnd = PumpControl::scheduleFromLine(line);
  if (maxEnd == 0) {
    SerialUSB.println("ERR ACTIVE PARAMS");
    return;
  }

  if (LedAnimations::startActiveWithColor(maxEnd, selectedColor)) {
    SerialUSB.println("OK ACTIVE");
  }
}

}  // namespace

namespace SerialCommandHandler {

void begin() {
  rxLen = 0;
  rxLine[0] = '\0';
}

void update() {
  while (SerialUSB.available() > 0) {
    char c = (char)SerialUSB.read();
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
