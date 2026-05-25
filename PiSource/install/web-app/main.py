import os
from datetime import datetime, timezone
from typing import Any

import aiosqlite
import httpx
from fastapi import FastAPI, Request
from fastapi.responses import JSONResponse
from fastapi.templating import Jinja2Templates

app = FastAPI(title="Terrarium Controller (Python)", version="0.1.0")
templates = Jinja2Templates(directory=os.path.join(os.path.dirname(__file__), "templates"))

DB_PATH = os.environ.get("TERRARIUM_DB_PATH", "/opt/terrarium/terrarium.db")
SIDECAR_BASE_URL = os.environ.get("TERRARIUM_SIDECAR_BASE_URL", "http://127.0.0.1:5580")


def board_to_bcm(board_pin: int) -> int | None:
    board_map = {
        3: 2, 5: 3, 7: 4, 8: 14, 10: 15, 11: 17, 12: 18, 13: 27,
        15: 22, 16: 23, 18: 24, 19: 10, 21: 9, 22: 25, 23: 11, 24: 8,
        26: 7, 27: 0, 28: 1, 29: 5, 31: 6, 32: 12, 33: 13, 35: 19,
        36: 16, 37: 26, 38: 20, 40: 21,
    }
    return board_map.get(board_pin)


async def get_settings_row() -> dict[str, Any] | None:
    query = """
        SELECT Id, Threshold1Temperature, Threshold2Temperature, Threshold3Temperature,
               Sensor1HumidityThreshold, TemperatureHysteresis, Relay4OnTime, Relay4OffTime,
               Relay1GPIO, Relay2GPIO, Relay3GPIO, Relay4GPIO, Relay5GPIO, Relay6GPIO,
               Sensor1GPIO, Sensor2GPIO, Sensor3GPIO, LinuxGpioChip,
               CameraWidth, CameraHeight, CameraFramerate, LogRetentionMonths,
               HumidityLockoutHours, LastModified
        FROM Settings
        LIMIT 1
    """

    async with aiosqlite.connect(DB_PATH) as db:
        db.row_factory = aiosqlite.Row
        async with db.execute(query) as cur:
            row = await cur.fetchone()
            return dict(row) if row else None


async def read_sidecar_sensor(sensor_id: int, bcm_pin: int) -> dict[str, Any]:
    async with httpx.AsyncClient(timeout=5.0) as client:
        response = await client.post(
            f"{SIDECAR_BASE_URL}/api/sensors/read",
            json={"sensorId": sensor_id, "bcmGpioPin": bcm_pin},
        )
        response.raise_for_status()
        return response.json()


@app.get("/")
async def home(request: Request):
    settings = await get_settings_row()
    return templates.TemplateResponse(
        "index.html",
        {
            "request": request,
            "settings": settings,
            "now": datetime.now(timezone.utc).isoformat(),
        },
    )


@app.get("/healthz")
async def healthz():
    return {
        "status": "ok",
        "service": "terrarium-python-web",
        "utc": datetime.now(timezone.utc).isoformat(),
    }


@app.get("/readyz")
async def readyz():
    database_ready = False
    sidecar_ready = False

    try:
        settings = await get_settings_row()
        database_ready = settings is not None
    except Exception:
        database_ready = False

    try:
        async with httpx.AsyncClient(timeout=5.0) as client:
            response = await client.get(f"{SIDECAR_BASE_URL}/health")
            sidecar_ready = response.status_code == 200
    except Exception:
        sidecar_ready = False

    payload = {
        "isReady": database_ready and sidecar_ready,
        "databaseReady": database_ready,
        "gpioReady": sidecar_ready,
        "controlLoopStarted": False,
        "lastSuccessfulCycleUtc": None,
        "lastCycleStatus": "not-implemented",
        "snapshotUtc": datetime.now(timezone.utc).isoformat(),
    }

    if payload["isReady"]:
        return payload

    return JSONResponse(status_code=503, content=payload)


@app.get("/api/settings")
async def api_settings():
    settings = await get_settings_row()
    if settings is None:
        return JSONResponse(status_code=404, content={"error": "settings row not found"})
    return settings


@app.get("/api/sensors/readings")
async def api_sensors_readings():
    settings = await get_settings_row()
    if settings is None:
        return JSONResponse(status_code=404, content={"error": "settings row not found"})

    sensor_board_pins = {
        1: int(settings.get("Sensor1GPIO") or 0),
        2: int(settings.get("Sensor2GPIO") or 0),
        3: int(settings.get("Sensor3GPIO") or 0),
    }

    data: list[dict[str, Any]] = []
    for sensor_id, board_pin in sensor_board_pins.items():
        bcm_pin = board_to_bcm(board_pin)
        if bcm_pin is None:
            data.append({
                "sensorId": sensor_id,
                "boardPin": board_pin,
                "success": False,
                "error": "unsupported board pin",
            })
            continue

        try:
            sidecar_payload = await read_sidecar_sensor(sensor_id, bcm_pin)
            sidecar_payload["boardPin"] = board_pin
            data.append(sidecar_payload)
        except Exception as ex:
            data.append({
                "sensorId": sensor_id,
                "bcmGpioPin": bcm_pin,
                "boardPin": board_pin,
                "success": False,
                "error": str(ex),
            })

    return {"items": data, "utc": datetime.now(timezone.utc).isoformat()}
