# Needs 4.7K to 10K resistor between 3.3V and signal pin.
# Pins VCC, Sig, NC, GND - view from front.
from machine import Pin, UART
from time import sleep, ticks_ms

import dht
import ujson


# DHT22 data pins on Pico (GP16, GP17, GP18)
sensor_pins = [16, 17, 18]
sensors = [dht.DHT22(Pin(pin)) for pin in sensor_pins]

# UART0: TX=GP0, RX=GP1. Connect Pico TX->Pi RX, Pico GND->Pi GND.
uart = UART(0, baudrate=115200, tx=Pin(0), rx=Pin(1))

sequence = 0


def emit_frame(frame):
    line = ujson.dumps(frame)
    print(line)
    uart.write(line + "\n")


while True:
    frame = {
        "version": 1,
        "source": "pico2-dht22",
        "sequence": sequence,
        "timestampMs": ticks_ms(),
        "sensors": []
    }

    for index in range(len(sensors)):
        sensor_id = index + 1
        sensor_payload = {
            "sensorId": sensor_id,
            "ok": False,
            "temperatureC": None,
            "humidityPercent": None,
            "error": None
        }

        try:
            # DHT22 recommends >= 1 second between reads.
            sleep(2)
            sensors[index].measure()
            sensor_payload["temperatureC"] = sensors[index].temperature()
            sensor_payload["humidityPercent"] = sensors[index].humidity()
            sensor_payload["ok"] = True
        except OSError as ex:
            sensor_payload["error"] = "OSError:{}".format(ex)
        except Exception as ex:
            sensor_payload["error"] = "Exception:{}".format(ex)

        frame["sensors"].append(sensor_payload)

    emit_frame(frame)
    sequence += 1
