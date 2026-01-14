#!/usr/bin/env python3
"""
Quick DHT22 sensor test for Raspberry Pi
Usage: sudo python3 test_dht22.py [GPIO_PIN] [ATTEMPTS]
Default: GPIO 23 (BCM), 5 attempts with 2s delay
"""

import sys
import time
import board
import adafruit_dht

def test_dht22(gpio_pin=23, attempts=5):
    """Test DHT22 sensor on specified BCM GPIO pin"""
    print(f"Testing DHT22 on BCM GPIO {gpio_pin}...")
    print(f"Attempts: {attempts} (2s delay between reads)")
    print("-" * 50)
    
    try:
        # Create DHT device, with data pin on specified GPIO
        # board.D23 = BCM 23 (physical pin 16)
        pin_map = {
            23: board.D23,
            24: board.D24,
            25: board.D25,
        }
        
        if gpio_pin not in pin_map:
            print(f"Error: GPIO {gpio_pin} not in pin map. Supported: 23, 24, 25")
            return False
        
        dhtDevice = adafruit_dht.DHT22(pin_map[gpio_pin], use_pulseio=False)
        
        success = False
        for attempt in range(1, attempts + 1):
            try:
                temperature = dhtDevice.temperature
                humidity = dhtDevice.humidity
                
                if temperature is not None and humidity is not None:
                    print(f"✓ Attempt {attempt}: T={temperature:.1f}°C, RH={humidity:.1f}%")
                    success = True
                else:
                    print(f"✗ Attempt {attempt}: Read returned None")
                    
            except RuntimeError as e:
                print(f"✗ Attempt {attempt}: {e}")
            
            if attempt < attempts:
                time.sleep(2)
        
        dhtDevice.exit()
        return success
        
    except Exception as e:
        print(f"Error: {e}")
        print("\nTroubleshooting:")
        print("1. Install adafruit-circuitpython-dht: pip3 install adafruit-circuitpython-dht")
        print("2. Verify wiring:")
        print("   - Data pin → BCM GPIO (default 23 = board pin 16)")
        print("   - VCC → 3.3V (NOT 5V)")
        print("   - GND → Ground")
        print("   - 4.7k-10k pull-up resistor from Data to 3.3V")
        print("3. Run with sudo: sudo python3 test_dht22.py")
        return False

if __name__ == "__main__":
    gpio_pin = 23
    attempts = 5
    
    if len(sys.argv) > 1:
        try:
            gpio_pin = int(sys.argv[1])
        except ValueError:
            print(f"Invalid GPIO: {sys.argv[1]}")
            sys.exit(1)
    
    if len(sys.argv) > 2:
        try:
            attempts = int(sys.argv[2])
        except ValueError:
            print(f"Invalid attempts: {sys.argv[2]}")
            sys.exit(1)
    
    success = test_dht22(gpio_pin, attempts)
    sys.exit(0 if success else 1)
