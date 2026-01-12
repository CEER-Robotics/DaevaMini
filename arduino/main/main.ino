struct Channel {
    int pin;
    unsigned long onTime;
    unsigned long duration;
    bool active;
  };
  
  Channel channels[4] = {
    {4, 0, 0, false},  // c1
    {5, 0, 0, false},  // c2
    {6, 0, 0, false},  // c3
    {7, 0, 0, false}   // c4
  };
  
  void setup() {
    Serial.begin(115200);
    
    for (int i = 0; i < 4; i++) {
      pinMode(channels[i].pin, OUTPUT);
      digitalWrite(channels[i].pin, LOW);
    }
  }
  
  void loop() {
    // Check serial for new commands
    if (Serial.available() > 0) {
      String input = Serial.readStringUntil('\n');
      input.trim();
      parseCommand(input);
    }
    
    // Update channel states
    unsigned long now = millis();
    bool allDone = true;
    for (int i = 0; i < 4; i++) {
      if (channels[i].active) {
        allDone = false;
        if (now - channels[i].onTime >= channels[i].duration) {
          digitalWrite(channels[i].pin, LOW);  // Back to closed
          channels[i].active = false;
        }
      }
    }
    
    // Send DONE when all channels have finished
    static bool wasDispensing = false;
    bool isDispensing = !allDone;
    if (wasDispensing && !isDispensing) {
      Serial.println("DONE");
    }
    wasDispensing = isDispensing;
  }
  
  void parseCommand(String input) {
    int startPos = 0;
    unsigned long now = millis();
    
    while (startPos < input.length()) {
      int colonPos = input.indexOf(':', startPos);
      if (colonPos == -1) break;
      
      int commaPos = input.indexOf(',', colonPos);
      if (commaPos == -1) commaPos = input.length();
      
      String channelStr = input.substring(startPos, colonPos);
      channelStr.replace("c", "");
      int channel = channelStr.toInt();
      
      String durationStr = input.substring(colonPos + 1, commaPos);
      int duration = durationStr.toInt();
      
      // Map channel 1-4 to array index 0-3
      int idx = channel - 1;
      
      if (idx >= 0 && idx < 4 && duration > 0) {
        channels[idx].onTime = now;
        channels[idx].duration = duration;
        channels[idx].active = true;
        digitalWrite(channels[idx].pin, HIGH);  // Activate = HIGH
      }
      
      startPos = commaPos + 1;
    }
  }