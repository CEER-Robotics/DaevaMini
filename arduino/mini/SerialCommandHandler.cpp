#include "SerialCommandHandler.h"

#include <Arduino.h>
#include <ctype.h>
#include <stdlib.h>
#include <string.h>

#include "ProjectConfig.h"
#include "PumpControl.h"

namespace {

enum State : uint8_t {
  ST_WAIT,
  ST_ACTIVE,
  ST_TOXIC,
  ST_MANUTENZIONE,
};

static State state = ST_WAIT;
static uint32_t activeEndMs = 0;

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
    PumpControl::allOff();
    state = ST_MANUTENZIONE;
    activeEndMs = 0;
    Serial.println("OK MANUTENZIONE");
    return;
  }

  if (isExactCommand(line, "READY")) {
    if (state == ST_TOXIC || state == ST_MANUTENZIONE) {
      state = ST_WAIT;
      Serial.println("OK READY");
    }
    return;
  }

  if (isExactCommand(line, "TOXIC")) {
    PumpControl::allOff();
    state = ST_TOXIC;
    activeEndMs = 0;
    Serial.println("OK TOXIC");
    return;
  }

  if (!isActiveCommand(line)) {
    return;
  }

  // ACTIVE command — accepted only in WAIT.
  if (state != ST_WAIT) {
    Serial.println("IGNORED ACTIVE");
    return;
  }

  PumpControl::allOff();
  const uint32_t maxEnd = PumpControl::scheduleFromLine(line);
  if (maxEnd == 0) {
    Serial.println("ERR ACTIVE PARAMS");
    return;
  }

  state = ST_ACTIVE;
  activeEndMs = maxEnd;
  Serial.println("OK ACTIVE");
}

}  // namespace

namespace SerialCommandHandler {

void begin() {
  rxLen = 0;
  rxLine[0] = '\0';
  state = ST_WAIT;
  activeEndMs = 0;
}

void update() {
  // Check if ACTIVE period has elapsed.
  if (state == ST_ACTIVE && activeEndMs != 0 &&
      (int32_t)(millis() - activeEndMs) >= 0) {
    state = ST_WAIT;
    activeEndMs = 0;
    Serial.println("DONE");
  }

  while (Serial.available() > 0) {
    char c = (char)Serial.read();
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
