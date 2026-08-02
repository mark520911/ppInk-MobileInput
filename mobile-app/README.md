# ppInk Mobile - Android companion app for ppInk

Allows controlling ppInk on PC from phone via WiFi, USB, or Bluetooth.

## Features
- Touch input to PC as mouse/ink events
- Bidirectional sync - PC sends strokes to phone
- Phone shows handwritten content only (immersive fullscreen)
- WiFi (WebSocket), USB (ADB reverse), Bluetooth (BLE NUS)
- QR code scanning for one-tap connection

## Building
1. Open `mobile-app/` in Android Studio (Giraffe+)
2. Build and run on phone

## Usage
1. Enable mobile input in ppInk Options
2. Click "Show QR" to generate connection QR
3. Scan QR code with phone to auto-connect
