#include "DaevaMaxFirmware.h"

#include "LedAnimations.h"
#include "ProjectConfig.h"
#include "PumpControl.h"
#include "SerialCommandHandler.h"

namespace DaevaMaxFirmware {

void begin() {
  DAEVA_SERIAL.begin(ProjectConfig::Serial::kBaudRate);
  PumpControl::begin();
  LedAnimations::begin();
  SerialCommandHandler::begin();
  LedAnimations::setStartupColor(ProjectConfig::Colors::kStartupDefault.r,
                                 ProjectConfig::Colors::kStartupDefault.g,
                                 ProjectConfig::Colors::kStartupDefault.b);

  DAEVA_SERIAL.println("Ready. ACTIVE, BASE:ORANGE, P1:700, P3:1000");
}

void update() {
  SerialCommandHandler::update();
  PumpControl::update();
  if (LedAnimations::update()) {
    DAEVA_SERIAL.println("DONE");
  }
}

}  // namespace DaevaMaxFirmware
