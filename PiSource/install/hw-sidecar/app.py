import asyncio
import json
import logging
import os
import threading
import time
from dataclasses import dataclass
from typing import Dict, Optional

from fastapi import FastAPI
from pydantic import BaseModel

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
logger = logging.getLogger("terrarium-hw-sidecar")

try:
    import board  # type: ignore
    import adafruit_dht  # type: ignore
except Exception as ex:  # pragma: no cover - environment-specific import
    board = None
    adafruit_dht = None
    logger.warning("DHT22 modules unavailable: %s", ex)

try:
    import gpiod  # type: ignore
except Exception as ex:  # pragma: no cover - environment-specific import
    gpiod = None
    logger.warning("python gpiod module unavailable: %s", ex)

try:
    import serial  # type: ignore
except Exception as ex:  # pragma: no cover - environment-specific import
    serial = None
    logger.warning("pyserial module unavailable: %s", ex)


app = FastAPI(title="Terrarium Hardware Sidecar", version="0.1.0")


class SensorReadRequest(BaseModel):
    sensorId: int
    bcmGpioPin: int


class SensorReadResponse(BaseModel):
    success: bool
    sensorId: int
    bcmGpioPin: int
    temperatureC: Optional[float] = None
    humidityPercent: Optional[float] = None
    error: Optional[str] = None


class RelaySetRequest(BaseModel):
    relayId: int
    bcmGpioPin: int
    state: bool


class RelaySetResponse(BaseModel):
    success: bool
    relayId: int
    bcmGpioPin: int
    state: bool
    error: Optional[str] = None


@dataclass
class RelayLineRef:
    chip: object
    line: object


_dht_cache: Dict[int, object] = {}
_relay_cache: Dict[int, RelayLineRef] = {}
_cache_lock = asyncio.Lock()


class PicoUartBridge:
    def __init__(self) -> None:
        self.mode = os.getenv("SENSOR_SOURCE", "onboard_dht").strip().lower()
        self.enabled = self.mode == "pico_uart"
        self.port = os.getenv("PICO_UART_PORT", "/dev/serial0")
        self.baud = int(os.getenv("PICO_UART_BAUD", "115200"))
        self.timeout_seconds = float(os.getenv("PICO_UART_TIMEOUT", "1.0"))
        self.stale_after_seconds = float(os.getenv("PICO_STALE_SECONDS", "20"))

        self._lock = threading.Lock()
        self._stop_event = threading.Event()
        self._thread: Optional[threading.Thread] = None
        self._latest_by_sensor: Dict[int, dict] = {}
        self._last_frame_monotonic: float = 0.0
        self._last_error: Optional[str] = None
        self._last_sequence: Optional[int] = None

        if self.enabled and serial is None:
            self._last_error = "SENSOR_SOURCE=pico_uart but pyserial is not installed"
            logger.error(self._last_error)
            self.enabled = False

    def start(self) -> None:
        if not self.enabled:
            logger.info("Sensor source mode is '%s'; Pico UART bridge disabled", self.mode)
            return

        if self._thread is not None:
            return

        self._thread = threading.Thread(target=self._run, name="pico-uart-bridge", daemon=True)
        self._thread.start()
        logger.info("Started Pico UART bridge on %s @ %s baud", self.port, self.baud)

    def stop(self) -> None:
        self._stop_event.set()
        if self._thread is not None:
            self._thread.join(timeout=2.0)

    def _run(self) -> None:
        while not self._stop_event.is_set():
            try:
                assert serial is not None
                with serial.Serial(self.port, baudrate=self.baud, timeout=self.timeout_seconds) as port:
                    logger.info("Connected to Pico UART on %s", self.port)
                    while not self._stop_event.is_set():
                        raw = port.readline()
                        if not raw:
                            continue

                        line = raw.decode("utf-8", errors="replace").strip()
                        if not line:
                            continue

                        self._ingest(line)
            except Exception as ex:
                with self._lock:
                    self._last_error = f"UART read failure: {ex}"
                logger.warning("Pico UART bridge read failure: %s", ex)
                time.sleep(1.0)

    def _ingest(self, line: str) -> None:
        payload = json.loads(line)
        sensors = payload.get("sensors")
        if not isinstance(sensors, list):
            raise ValueError("Frame does not contain a 'sensors' array")

        now_monotonic = time.monotonic()
        sequence = payload.get("sequence")

        with self._lock:
            for sensor in sensors:
                if not isinstance(sensor, dict):
                    continue

                sensor_id = sensor.get("sensorId")
                if not isinstance(sensor_id, int):
                    continue

                self._latest_by_sensor[sensor_id] = {
                    "ok": bool(sensor.get("ok", False)),
                    "temperatureC": sensor.get("temperatureC"),
                    "humidityPercent": sensor.get("humidityPercent"),
                    "error": sensor.get("error"),
                    "ingestedAtUtc": time.time(),
                }

            self._last_frame_monotonic = now_monotonic
            self._last_sequence = sequence if isinstance(sequence, int) else None
            self._last_error = None

    def get_sensor_reading(self, sensor_id: int) -> tuple[Optional[dict], Optional[str]]:
        if not self.enabled:
            return None, "Pico UART bridge is disabled"

        with self._lock:
            if self._last_frame_monotonic <= 0:
                return None, "No frames received from Pico yet"

            age = time.monotonic() - self._last_frame_monotonic
            if age > self.stale_after_seconds:
                return None, f"Latest Pico frame is stale ({age:.1f}s old)"

            entry = self._latest_by_sensor.get(sensor_id)
            if entry is None:
                return None, f"Sensor {sensor_id} not found in latest Pico data"

            if not entry.get("ok"):
                return None, str(entry.get("error") or "Pico marked reading as invalid")

            temperature = entry.get("temperatureC")
            humidity = entry.get("humidityPercent")
            if temperature is None or humidity is None:
                return None, "Pico reading missing temperature/humidity values"

            try:
                return {
                    "temperatureC": float(temperature),
                    "humidityPercent": float(humidity),
                }, None
            except Exception:
                return None, "Pico returned non-numeric temperature/humidity"

    def get_status(self) -> dict:
        with self._lock:
            frame_age = None
            if self._last_frame_monotonic > 0:
                frame_age = round(time.monotonic() - self._last_frame_monotonic, 3)

            return {
                "enabled": self.enabled,
                "mode": self.mode,
                "port": self.port,
                "baud": self.baud,
                "staleAfterSeconds": self.stale_after_seconds,
                "knownSensors": sorted(self._latest_by_sensor.keys()),
                "lastSequence": self._last_sequence,
                "lastFrameAgeSeconds": frame_age,
                "lastError": self._last_error,
            }


_pico_uart_bridge = PicoUartBridge()
_pico_uart_bridge.start()


def _to_board_pin(bcm_gpio_pin: int):
    if board is None:
        return None

    # board.D22, board.D23, ... naming convention
    return getattr(board, f"D{bcm_gpio_pin}", None)


def _open_dht(bcm_gpio_pin: int):
    if adafruit_dht is None:
        raise RuntimeError("adafruit_dht is unavailable")

    board_pin = _to_board_pin(bcm_gpio_pin)
    if board_pin is None:
        raise RuntimeError(f"Unsupported BCM pin for DHT22: {bcm_gpio_pin}")

    return adafruit_dht.DHT22(board_pin, use_pulseio=False)


def _open_relay_line(bcm_gpio_pin: int) -> RelayLineRef:
    if gpiod is None:
        raise RuntimeError("python gpiod is unavailable")

    chip = gpiod.Chip("/dev/gpiochip0")
    line = chip.get_line(bcm_gpio_pin)
    line.request(consumer="terrarium-sidecar", type=gpiod.LINE_REQ_DIR_OUT, default_vals=[0])
    return RelayLineRef(chip=chip, line=line)


@app.get("/health")
async def health():
    return {
        "ok": True,
        "dhtAvailable": adafruit_dht is not None and board is not None,
        "gpiodAvailable": gpiod is not None,
        "relayPinsOpen": sorted(_relay_cache.keys()),
        "sensorSource": _pico_uart_bridge.get_status(),
    }


@app.post("/api/sensors/read", response_model=SensorReadResponse)
async def read_sensor(request: SensorReadRequest):
    if _pico_uart_bridge.enabled:
        reading, error = _pico_uart_bridge.get_sensor_reading(request.sensorId)
        if reading is not None:
            return SensorReadResponse(
                success=True,
                sensorId=request.sensorId,
                bcmGpioPin=request.bcmGpioPin,
                temperatureC=reading["temperatureC"],
                humidityPercent=reading["humidityPercent"],
            )

        return SensorReadResponse(
            success=False,
            sensorId=request.sensorId,
            bcmGpioPin=request.bcmGpioPin,
            error=error,
        )

    try:
        async with _cache_lock:
            dht = _dht_cache.get(request.bcmGpioPin)
            if dht is None:
                dht = _open_dht(request.bcmGpioPin)
                _dht_cache[request.bcmGpioPin] = dht

        # DHT22 can intermittently fail on first try, so do two quick attempts.
        for _ in range(2):
            try:
                temperature = dht.temperature
                humidity = dht.humidity
                if temperature is not None and humidity is not None:
                    return SensorReadResponse(
                        success=True,
                        sensorId=request.sensorId,
                        bcmGpioPin=request.bcmGpioPin,
                        temperatureC=float(temperature),
                        humidityPercent=float(humidity),
                    )
            except Exception:
                await asyncio.sleep(0.2)

        return SensorReadResponse(
            success=False,
            sensorId=request.sensorId,
            bcmGpioPin=request.bcmGpioPin,
            error="DHT22 returned no data",
        )
    except Exception as ex:
        return SensorReadResponse(
            success=False,
            sensorId=request.sensorId,
            bcmGpioPin=request.bcmGpioPin,
            error=str(ex),
        )


@app.post("/api/relays/set", response_model=RelaySetResponse)
async def set_relay(request: RelaySetRequest):
    try:
        async with _cache_lock:
            line_ref = _relay_cache.get(request.bcmGpioPin)
            if line_ref is None:
                line_ref = _open_relay_line(request.bcmGpioPin)
                _relay_cache[request.bcmGpioPin] = line_ref

        line_ref.line.set_value(1 if request.state else 0)
        return RelaySetResponse(
            success=True,
            relayId=request.relayId,
            bcmGpioPin=request.bcmGpioPin,
            state=request.state,
        )
    except Exception as ex:
        return RelaySetResponse(
            success=False,
            relayId=request.relayId,
            bcmGpioPin=request.bcmGpioPin,
            state=request.state,
            error=str(ex),
        )


@app.on_event("shutdown")
async def shutdown_event():
    _pico_uart_bridge.stop()

    async with _cache_lock:
        for pin, line_ref in list(_relay_cache.items()):
            try:
                line_ref.line.set_value(0)
            except Exception:
                pass
            try:
                line_ref.line.release()
            except Exception:
                pass
            try:
                line_ref.chip.close()
            except Exception:
                pass
            logger.info("Released relay line BCM GPIO %s", pin)

        _relay_cache.clear()

        for pin, dht in list(_dht_cache.items()):
            try:
                dht.exit()
            except Exception:
                pass
            logger.info("Closed DHT22 instance on BCM GPIO %s", pin)

        _dht_cache.clear()
