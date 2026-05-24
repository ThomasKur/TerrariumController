import asyncio
import logging
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
    }


@app.post("/api/sensors/read", response_model=SensorReadResponse)
async def read_sensor(request: SensorReadRequest):
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
