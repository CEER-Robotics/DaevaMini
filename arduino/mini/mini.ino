#include <Arduino.h>

#include "PumpControl.h"
#include "ProjectConfig.h"
#include "SerialCommandHandler.h"

void setup() {
  Serial.begin(ProjectConfig::Serial::kBaudRate);
  PumpControl::begin();
  SerialCommandHandler::begin();

  Serial.println("Ready. ACTIVE, P1:700, P3:1000");
}

void loop() {
  SerialCommandHandler::update();
  PumpControl::update();
}
