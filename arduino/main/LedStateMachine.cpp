#include "LedStateMachine.h"

namespace {

static LedStateMachine::ProgramState progState = LedStateMachine::ST_STARTUP;
static uint32_t startupDurationMs = 10000;
static uint32_t toxicTimeoutMs = 30000;
static bool toxicTimeoutEnabled = true;
static uint32_t stateEnteredAt = 0;
static uint32_t waitLastValidAt = 0;
static uint32_t activeUntil = 0;
static uint32_t endingUntil = 0;

static bool enterState(LedStateMachine::ProgramState nextState, uint32_t now) {
  if (progState == nextState) {
    return false;
  }

  progState = nextState;
  stateEnteredAt = now;
  if (nextState == LedStateMachine::ST_WAIT) {
    waitLastValidAt = now;
  }
  return true;
}

}  // namespace

namespace LedStateMachine {

void begin() {
  const uint32_t now = millis();
  progState = ST_STARTUP;
  stateEnteredAt = now;
  waitLastValidAt = now;
  activeUntil = 0;
  endingUntil = 0;
}

void setStartupDurationMs(uint32_t durationMs) {
  startupDurationMs = (durationMs == 0) ? 1 : durationMs;
}

void setToxicTimeoutMs(int32_t timeoutMs) {
  if (timeoutMs > 0) {
    toxicTimeoutMs = (uint32_t)timeoutMs;
    toxicTimeoutEnabled = true;
  } else {
    toxicTimeoutMs = 0;
    toxicTimeoutEnabled = false;
  }
}

bool startActive(uint32_t activeUntilMs) {
  if (progState != ST_WAIT) {
    return false;
  }

  const uint32_t now = millis();
  if ((int32_t)(now - activeUntilMs) >= 0) {
    activeUntilMs = now + 1;
  }

  activeUntil = activeUntilMs;
  endingUntil = 0;
  enterState(ST_ACTIVE, now);
  return true;
}

bool enterMaintenance() {
  if (progState == ST_STARTUP) {
    return false;
  }

  if (progState == ST_MANUTENZIONE) {
    return true;
  }

  const uint32_t now = millis();
  return enterState(ST_MANUTENZIONE, now);
}

bool enterToxic() {
  if (progState == ST_STARTUP) {
    return false;
  }

  if (progState == ST_TOXIC) {
    return true;
  }

  const uint32_t now = millis();
  return enterState(ST_TOXIC, now);
}

bool handleReady() {
  const uint32_t now = millis();
  if (progState == ST_WAIT) {
    waitLastValidAt = now;
    return true;
  }

  if (progState == ST_TOXIC || progState == ST_MANUTENZIONE) {
    enterState(ST_WAIT, now);
    return true;
  }

  return false;
}

TickResult tick(uint32_t now, uint32_t endStatusMs) {
  TickResult result{progState, false, false};

  if (progState == ST_STARTUP) {
    if ((uint32_t)(now - stateEnteredAt) >= startupDurationMs) {
      result.enteredState = enterState(ST_WAIT, now);
    }
  } else if (progState == ST_WAIT) {
    if (toxicTimeoutEnabled &&
        (uint32_t)(now - waitLastValidAt) >= toxicTimeoutMs) {
      result.enteredState = enterState(ST_TOXIC, now);
    }
  } else if (progState == ST_ACTIVE) {
    if ((int32_t)(now - activeUntil) >= 0) {
      const uint32_t endingMs = (endStatusMs == 0) ? 1 : endStatusMs;
      endingUntil = now + endingMs;
      result.enteredState = enterState(ST_ENDING, now);
      result.doneEvent = true;
    }
  } else if (progState == ST_ENDING) {
    if ((int32_t)(now - endingUntil) >= 0) {
      result.enteredState = enterState(ST_WAIT, now);
    }
  }

  result.state = progState;
  return result;
}

bool isWaitingForCommand() {
  return progState == ST_WAIT;
}

ProgramState state() {
  return progState;
}

uint32_t activeUntilMs() {
  return activeUntil;
}

}  // namespace LedStateMachine
