#pragma once

#include <Arduino.h>

namespace LedStateMachine {

enum ProgramState : uint8_t {
  ST_STARTUP,
  ST_WAIT,
  ST_START_WAIT = ST_WAIT,
  ST_TOXIC,
  ST_MANUTENZIONE,
  ST_ACTIVE,
  ST_ENDING
};

struct TickResult {
  ProgramState state;
  bool doneEvent;
  bool enteredState;
};

void begin();
void setStartupDurationMs(uint32_t durationMs);
void setToxicTimeoutMs(uint32_t timeoutMs);
bool startActive(uint32_t activeUntilMs);
bool enterMaintenance();
bool enterToxic();
bool handleReady();
TickResult tick(uint32_t now, uint32_t endStatusMs);
bool isWaitingForCommand();
ProgramState state();
uint32_t activeUntilMs();

}  // namespace LedStateMachine
