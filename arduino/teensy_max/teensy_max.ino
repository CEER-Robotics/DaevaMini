#include <DaevaMaxFirmware.h>

void setup() {
  DaevaMaxFirmware::begin();
}

void loop() {
  DaevaMaxFirmware::update();
}
