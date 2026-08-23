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
void setToxicTimeoutMs(int32_t timeoutMs);
bool startActive(uint32_t activeUntilMs);
// Pushes the end of an ACTIVE already under way further out. Used by tap mode,
// where the host keeps extending the pour instead of declaring its length up
// front. Only valid in ACTIVE.
bool extendActive(uint32_t activeUntilMs);
// Ends an ACTIVE now. The normal end-of-pour path then runs by itself: DONE,
// the ENDING hold and fade, and back to WAIT.
bool finishActiveNow(uint32_t now);
bool enterMaintenance();
bool enterToxic();
bool handleReady();
TickResult tick(uint32_t now, uint32_t endStatusMs);
bool isWaitingForCommand();
ProgramState state();
uint32_t activeUntilMs();

}  // namespace LedStateMachine
