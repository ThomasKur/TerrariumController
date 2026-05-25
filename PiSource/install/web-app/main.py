import os
import asyncio
import contextlib
from datetime import datetime, timezone, timedelta
from typing import Any

import aiosqlite
import httpx
from fastapi import FastAPI, Request
from fastapi.responses import JSONResponse
from fastapi.templating import Jinja2Templates
from pydantic import BaseModel

app = FastAPI(title="Terrarium Controller (Python)", version="0.1.0")
templates = Jinja2Templates(directory=os.path.join(os.path.dirname(__file__), "templates"))

DB_PATH = os.environ.get("TERRARIUM_DB_PATH", "/opt/terrarium/terrarium.db")
SIDECAR_BASE_URL = os.environ.get("TERRARIUM_SIDECAR_BASE_URL", "http://127.0.0.1:5580")
CONTROL_POLL_SECONDS = int(os.environ.get("TERRARIUM_CONTROL_POLL_SECONDS", "30"))
STALE_FAILSAFE_SECONDS = int(os.environ.get("TERRARIUM_STALE_FAILSAFE_SECONDS", "300"))

app.state.control_loop_started = False
app.state.last_successful_cycle_utc = None
app.state.last_cycle_status = "not-started"
app.state.last_valid_sensor_timestamps = {}
app.state.control_loop_task = None
app.state.last_hourly_snapshot = datetime.now(timezone.utc)
app.state.last_daily_prune = datetime.now(timezone.utc)


def board_to_bcm(board_pin: int) -> int | None:
    board_map = {
        3: 2, 5: 3, 7: 4, 8: 14, 10: 15, 11: 17, 12: 18, 13: 27,
        15: 22, 16: 23, 18: 24, 19: 10, 21: 9, 22: 25, 23: 11, 24: 8,
        26: 7, 27: 0, 28: 1, 29: 5, 31: 6, 32: 12, 33: 13, 35: 19,
        36: 16, 37: 26, 38: 20, 40: 21,
    }
    return board_map.get(board_pin)


def to_iso_utc(value: datetime) -> str:
    return value.astimezone(timezone.utc).isoformat()


def parse_utc(value: Any) -> datetime | None:
    if value is None:
        return None
    if isinstance(value, datetime):
        return value if value.tzinfo else value.replace(tzinfo=timezone.utc)
    if isinstance(value, str):
        try:
            normalized = value.replace("Z", "+00:00")
            parsed = datetime.fromisoformat(normalized)
            return parsed if parsed.tzinfo else parsed.replace(tzinfo=timezone.utc)
        except Exception:
            return None
    return None


def resolve_gpio_pin(configured_pin: int) -> int | None:
    bcm_pin = board_to_bcm(configured_pin)
    if bcm_pin is not None:
        return bcm_pin

    # Backward compatibility for settings that stored BCM values.
    if 0 <= configured_pin <= 27:
        return configured_pin

    return None


def should_relay_be_on(current_state: bool, temperature: float, threshold: float, hysteresis: float) -> bool:
    if current_state:
        return temperature < (threshold + hysteresis)
    return temperature < threshold


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


async def get_latest_relay_state(relay_id: int) -> bool:
    query = """
        SELECT State
        FROM RelayStates
        WHERE RelayId = ?
        ORDER BY Timestamp DESC, Id DESC
        LIMIT 1
    """

    async with aiosqlite.connect(DB_PATH) as db:
        async with db.execute(query, (relay_id,)) as cur:
            row = await cur.fetchone()
            return bool(row[0]) if row is not None else False


async def persist_relay_and_log(
    relay_id: int,
    state: bool,
    trigger_source: str,
    sensor_id: int | None = None,
    temperature: float | None = None,
    humidity: float | None = None,
) -> None:
    now_iso = to_iso_utc(datetime.now(timezone.utc))
    log_details = f"Relay {relay_id} turned {'ON' if state else 'OFF'} - Trigger: {trigger_source}"

    async with aiosqlite.connect(DB_PATH) as db:
        await db.execute(
            """
            INSERT INTO RelayStates (RelayId, Timestamp, State, TriggerSource, SourceSensorId, SensorTemperature, SensorHumidity)
            VALUES (?, ?, ?, ?, ?, ?, ?)
            """,
            (relay_id, now_iso, int(state), trigger_source, sensor_id, temperature, humidity),
        )

        sensor_values = {
            "Sensor1Temperature": temperature if sensor_id == 1 else None,
            "Sensor1Humidity": humidity if sensor_id == 1 else None,
            "Sensor2Temperature": temperature if sensor_id == 2 else None,
            "Sensor2Humidity": humidity if sensor_id == 2 else None,
            "Sensor3Temperature": temperature if sensor_id == 3 else None,
            "Sensor3Humidity": humidity if sensor_id == 3 else None,
        }

        await db.execute(
            """
            INSERT INTO LogEntries (
                Timestamp, LogType, Details, RelayId, RelayState,
                Sensor1Temperature, Sensor1Humidity,
                Sensor2Temperature, Sensor2Humidity,
                Sensor3Temperature, Sensor3Humidity)
            VALUES (?, 'StateChange', ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            (
                now_iso,
                log_details,
                relay_id,
                int(state),
                sensor_values["Sensor1Temperature"],
                sensor_values["Sensor1Humidity"],
                sensor_values["Sensor2Temperature"],
                sensor_values["Sensor2Humidity"],
                sensor_values["Sensor3Temperature"],
                sensor_values["Sensor3Humidity"],
            ),
        )

        await db.commit()


async def write_relay_state(
    settings: dict[str, Any],
    relay_id: int,
    state: bool,
    trigger_source: str,
    sensor_id: int | None = None,
    temperature: float | None = None,
    humidity: float | None = None,
) -> bool:
    relay_pin_key = f"Relay{relay_id}GPIO"
    configured_pin = int(settings.get(relay_pin_key) or 0)
    bcm_pin = resolve_gpio_pin(configured_pin)
    if bcm_pin is None:
        return False

    current = await get_latest_relay_state(relay_id)
    if current == state:
        return True

    async with httpx.AsyncClient(timeout=5.0) as client:
        response = await client.post(
            f"{SIDECAR_BASE_URL}/api/relays/set",
            json={"relayId": relay_id, "bcmGpioPin": bcm_pin, "state": state},
        )
        response.raise_for_status()
        payload = response.json()

    if payload.get("success") is True:
        await persist_relay_and_log(relay_id, state, trigger_source, sensor_id, temperature, humidity)
        return True

    return False


async def read_sidecar_sensor(sensor_id: int, bcm_pin: int) -> dict[str, Any]:
    async with httpx.AsyncClient(timeout=5.0) as client:
        response = await client.post(
            f"{SIDECAR_BASE_URL}/api/sensors/read",
            json={"sensorId": sensor_id, "bcmGpioPin": bcm_pin},
        )
        response.raise_for_status()
        return response.json()


async def load_or_create_humidity_lockout(sensor_id: int) -> dict[str, Any]:
    async with aiosqlite.connect(DB_PATH) as db:
        db.row_factory = aiosqlite.Row
        async with db.execute(
            """
            SELECT Id, SensorId, LastTriggeredTime, IsLocked, LockExpiresAt
            FROM HumidityLockoutStates
            WHERE SensorId = ?
            LIMIT 1
            """,
            (sensor_id,),
        ) as cur:
            row = await cur.fetchone()

        if row is None:
            now_iso = to_iso_utc(datetime.now(timezone.utc))
            await db.execute(
                """
                INSERT INTO HumidityLockoutStates (SensorId, LastTriggeredTime, IsLocked, LockExpiresAt)
                VALUES (?, ?, 0, ?)
                """,
                (sensor_id, now_iso, now_iso),
            )
            await db.commit()
            async with db.execute(
                """
                SELECT Id, SensorId, LastTriggeredTime, IsLocked, LockExpiresAt
                FROM HumidityLockoutStates
                WHERE SensorId = ?
                LIMIT 1
                """,
                (sensor_id,),
            ) as cur2:
                row = await cur2.fetchone()

        return dict(row) if row else {}


async def persist_humidity_lockout(lockout_row: dict[str, Any]) -> None:
    async with aiosqlite.connect(DB_PATH) as db:
        await db.execute(
            """
            UPDATE HumidityLockoutStates
            SET LastTriggeredTime = ?, IsLocked = ?, LockExpiresAt = ?
            WHERE Id = ?
            """,
            (
                lockout_row["LastTriggeredTime"],
                int(bool(lockout_row["IsLocked"])),
                lockout_row["LockExpiresAt"],
                lockout_row["Id"],
            ),
        )
        await db.commit()


async def log_hourly_snapshot(sensor_readings: dict[int, dict[str, Any]]) -> None:
    now_iso = to_iso_utc(datetime.now(timezone.utc))

    def field(sensor_id: int, key: str) -> float | None:
        item = sensor_readings.get(sensor_id, {})
        if item.get("success"):
            return item.get(key)
        return None

    async with aiosqlite.connect(DB_PATH) as db:
        await db.execute(
            """
            INSERT INTO LogEntries (
                Timestamp, LogType, Details, RelayId, RelayState,
                Sensor1Temperature, Sensor1Humidity,
                Sensor2Temperature, Sensor2Humidity,
                Sensor3Temperature, Sensor3Humidity)
            VALUES (?, 'HourlySnapshot', 'Hourly sensor reading snapshot', NULL, NULL, ?, ?, ?, ?, ?, ?)
            """,
            (
                now_iso,
                field(1, "temperatureC"),
                field(1, "humidityPercent"),
                field(2, "temperatureC"),
                field(2, "humidityPercent"),
                field(3, "temperatureC"),
                field(3, "humidityPercent"),
            ),
        )
        await db.commit()


async def prune_old_entries(retention_months: int) -> None:
    async with aiosqlite.connect(DB_PATH) as db:
        # Approximate month retention by 30-day blocks for SQLite compatibility.
        cutoff_query = f"datetime('now', '-{max(1, retention_months) * 30} days')"
        await db.execute(f"DELETE FROM LogEntries WHERE Timestamp < {cutoff_query}")
        await db.commit()


async def read_all_sensors(settings: dict[str, Any]) -> dict[int, dict[str, Any]]:
    output: dict[int, dict[str, Any]] = {}
    for sensor_id in (1, 2, 3):
        board_pin = int(settings.get(f"Sensor{sensor_id}GPIO") or 0)
        bcm_pin = resolve_gpio_pin(board_pin)
        if bcm_pin is None:
            output[sensor_id] = {
                "sensorId": sensor_id,
                "boardPin": board_pin,
                "success": False,
                "error": "unsupported sensor pin",
            }
            continue

        try:
            payload = await read_sidecar_sensor(sensor_id, bcm_pin)
            payload["boardPin"] = board_pin
            output[sensor_id] = payload
        except Exception as ex:
            output[sensor_id] = {
                "sensorId": sensor_id,
                "boardPin": board_pin,
                "bcmGpioPin": bcm_pin,
                "success": False,
                "error": str(ex),
            }
    return output


async def apply_scheduler(settings: dict[str, Any]) -> None:
    on_time_str = str(settings.get("Relay4OnTime") or "08:00")
    off_time_str = str(settings.get("Relay4OffTime") or "20:00")

    try:
        on_hour, on_minute = [int(part) for part in on_time_str.split(":", 1)]
        off_hour, off_minute = [int(part) for part in off_time_str.split(":", 1)]
    except Exception:
        return

    now_local = datetime.now().time()
    on_minutes = on_hour * 60 + on_minute
    off_minutes = off_hour * 60 + off_minute
    now_minutes = now_local.hour * 60 + now_local.minute

    if on_minutes < off_minutes:
        should_on = on_minutes <= now_minutes < off_minutes
    else:
        should_on = now_minutes >= on_minutes or now_minutes < off_minutes

    await write_relay_state(settings, 4, should_on, "Scheduler")


async def apply_humidity_lockout(settings: dict[str, Any], humidity: float | None) -> None:
    if humidity is None:
        return

    lockout = await load_or_create_humidity_lockout(1)
    now_utc = datetime.now(timezone.utc)
    threshold = float(settings.get("Sensor1HumidityThreshold") or 60.0)
    lockout_hours = int(settings.get("HumidityLockoutHours") or 6)

    lock_expires_at = parse_utc(lockout.get("LockExpiresAt"))
    is_locked = bool(lockout.get("IsLocked"))
    if is_locked and lock_expires_at and now_utc >= lock_expires_at:
        lockout["IsLocked"] = 0
        is_locked = False

    if (not is_locked) and humidity < threshold:
        await write_relay_state(settings, 5, True, "Humidity Threshold", sensor_id=1, humidity=humidity)
        await asyncio.sleep(1)
        await write_relay_state(settings, 5, False, "Humidity Pulse Complete", sensor_id=1, humidity=humidity)

        lockout["LastTriggeredTime"] = to_iso_utc(now_utc)
        lockout["IsLocked"] = 1
        lockout["LockExpiresAt"] = to_iso_utc(now_utc + timedelta(hours=lockout_hours))

    await persist_humidity_lockout(lockout)


async def run_control_cycle() -> None:
    now_utc = datetime.now(timezone.utc)
    settings = await get_settings_row()
    if settings is None:
        app.state.last_cycle_status = "settings-missing"
        return

    readings = await read_all_sensors(settings)

    for sensor_id in (1, 2, 3):
        reading = readings.get(sensor_id, {})
        success = bool(reading.get("success"))
        if success and reading.get("temperatureC") is not None:
            app.state.last_valid_sensor_timestamps[sensor_id] = now_utc

            threshold = float(settings.get(f"Threshold{sensor_id}Temperature") or 29.0)
            hysteresis = float(settings.get("TemperatureHysteresis") or 1.0)
            current_state = await get_latest_relay_state(sensor_id)
            target = should_relay_be_on(
                current_state,
                float(reading.get("temperatureC")),
                threshold,
                hysteresis,
            )
            await write_relay_state(
                settings,
                sensor_id,
                target,
                "Temperature Threshold",
                sensor_id=sensor_id,
                temperature=float(reading.get("temperatureC")),
                humidity=float(reading.get("humidityPercent")) if reading.get("humidityPercent") is not None else None,
            )

            if sensor_id == 1:
                await apply_humidity_lockout(settings, float(reading.get("humidityPercent")) if reading.get("humidityPercent") is not None else None)
        else:
            last_valid = app.state.last_valid_sensor_timestamps.get(sensor_id)
            if last_valid is None or (now_utc - last_valid).total_seconds() >= STALE_FAILSAFE_SECONDS:
                await write_relay_state(settings, sensor_id, False, "Sensor Stale Failsafe")

    await apply_scheduler(settings)

    if (now_utc - app.state.last_hourly_snapshot).total_seconds() >= 3600:
        await log_hourly_snapshot(readings)
        app.state.last_hourly_snapshot = now_utc

    if (now_utc - app.state.last_daily_prune).total_seconds() >= 86400:
        retention = int(settings.get("LogRetentionMonths") or 12)
        await prune_old_entries(retention)
        app.state.last_daily_prune = now_utc

    app.state.last_successful_cycle_utc = to_iso_utc(now_utc)
    app.state.last_cycle_status = "ok"


async def control_loop() -> None:
    app.state.control_loop_started = True
    while True:
        try:
            await run_control_cycle()
        except asyncio.CancelledError:
            raise
        except Exception:
            app.state.last_cycle_status = "error"

        await asyncio.sleep(max(5, CONTROL_POLL_SECONDS))


@app.on_event("startup")
async def startup_event() -> None:
    app.state.control_loop_task = asyncio.create_task(control_loop())


@app.on_event("shutdown")
async def shutdown_event() -> None:
    task = app.state.control_loop_task
    if task is not None:
        task.cancel()
        with contextlib.suppress(asyncio.CancelledError):
            await task


class ManualRelayRequest(BaseModel):
    state: bool


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
        "controlLoopStarted": bool(app.state.control_loop_started),
        "lastSuccessfulCycleUtc": app.state.last_successful_cycle_utc,
        "lastCycleStatus": app.state.last_cycle_status,
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

    readings = await read_all_sensors(settings)
    return {"items": [readings[idx] for idx in (1, 2, 3)], "utc": datetime.now(timezone.utc).isoformat()}


@app.get("/api/logs")
async def api_logs(limit: int = 50):
    limit = max(1, min(500, limit))
    async with aiosqlite.connect(DB_PATH) as db:
        db.row_factory = aiosqlite.Row
        async with db.execute(
            """
            SELECT Id, Timestamp, LogType, Details, RelayId, RelayState,
                   Sensor1Temperature, Sensor1Humidity,
                   Sensor2Temperature, Sensor2Humidity,
                   Sensor3Temperature, Sensor3Humidity
            FROM LogEntries
            ORDER BY Timestamp DESC, Id DESC
            LIMIT ?
            """,
            (limit,),
        ) as cur:
            rows = await cur.fetchall()
            return {"items": [dict(row) for row in rows]}


@app.get("/api/diagnostics/control")
async def api_control_diagnostics():
    return {
        "controlLoopStarted": bool(app.state.control_loop_started),
        "lastSuccessfulCycleUtc": app.state.last_successful_cycle_utc,
        "lastCycleStatus": app.state.last_cycle_status,
        "lastValidSensorTimestamps": {
            str(k): to_iso_utc(v) if isinstance(v, datetime) else None
            for k, v in app.state.last_valid_sensor_timestamps.items()
        },
        "pollIntervalSeconds": CONTROL_POLL_SECONDS,
        "staleFailsafeSeconds": STALE_FAILSAFE_SECONDS,
    }


@app.post("/api/relays/{relay_id}")
async def api_set_relay(relay_id: int, request: ManualRelayRequest):
    if relay_id < 1 or relay_id > 6:
        return JSONResponse(status_code=400, content={"error": "relay_id must be 1..6"})

    settings = await get_settings_row()
    if settings is None:
        return JSONResponse(status_code=404, content={"error": "settings row not found"})

    ok = await write_relay_state(settings, relay_id, request.state, "Manual Override")
    return {"success": ok, "relayId": relay_id, "state": request.state}
