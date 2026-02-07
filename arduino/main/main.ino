#include <Arduino.h>
#include <ctype.h>
#include <string.h>

#include "LedAnimations.h"
#include "PumpControl.h"

static const uint32_t BAUD = 115200;

static char rxLine[256];
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

static bool extractActiveColorName(const char* line,
                                   char* outName,
                                   size_t outNameLen) {
  if (line == nullptr || outName == nullptr || outNameLen == 0) {
    return false;
  }

  outName[0] = '\0';

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

    if (strcmp(key, "BASE") == 0 || strcmp(key, "COLOR") == 0) {
      if (*val == '\0') {
        return false;
      }
      strncpy(outName, val, outNameLen - 1);
      outName[outNameLen - 1] = '\0';
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
    if (LedAnimations::handleReadyCommand()) {
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
  if (!extractActiveColorName(line, colorName, sizeof(colorName))) {
    SerialUSB.println("ERR ACTIVE COLOR");
    return;
  }

  if (!LedAnimations::isWaitingForCommand()) {
    SerialUSB.println("IGNORED ACTIVE");
    return;
  }

  uint32_t selectedColor = 0;
  if (!LedAnimations::parsePresetColor(colorName, selectedColor)) {
    // Reject ACTIVE when preset color is missing/unknown.
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

static void readSerialLineNonBlocking() {
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

void setup() {
  SerialUSB.begin(BAUD);
  PumpControl::begin();
  LedAnimations::begin();
  LedAnimations::setStartupColor(255, 120, 0);

  SerialUSB.println("Ready. ACTIVE, BASE:ORANGE, P1:700, P3:1000");
}

void loop() {
  readSerialLineNonBlocking();
  PumpControl::update();
  if (LedAnimations::update()) {
    SerialUSB.println("DONE");
  }
}
