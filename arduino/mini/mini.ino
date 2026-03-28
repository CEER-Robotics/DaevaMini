#include <Arduino.h>

#include "LedAnimations.h"
#include "PumpControl.h"
#include "ProjectConfig.h"
#include "SerialCommandHandler.h"

void setup() {
  Serial.begin(ProjectConfig::Serial::kBaudRate);
  PumpControl::begin();
  LedAnimations::begin();
  SerialCommandHandler::begin();
  LedAnimations::setStartupColor(ProjectConfig::Colors::kStartupDefault.r,
                                 ProjectConfig::Colors::kStartupDefault.g,
                                 ProjectConfig::Colors::kStartupDefault.b);

  Serial.println("Ready. ACTIVE, BASE:ORANGE, P1:700, P3:1000");
}

void loop() {
  SerialCommandHandler::update();
  PumpControl::update();
  if (LedAnimations::update()) {
    Serial.println("DONE");
  }
}
