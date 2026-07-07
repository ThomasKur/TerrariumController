#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
VENV_DIR="${SCRIPT_DIR}/.venv"

if [[ ! -d "${VENV_DIR}" ]]; then
  python3 -m venv "${VENV_DIR}"
  "${VENV_DIR}/bin/pip" install --upgrade pip
fi

"${VENV_DIR}/bin/pip" install -r "${SCRIPT_DIR}/requirements.txt"

exec "${VENV_DIR}/bin/uvicorn" app:app --host 127.0.0.1 --port 5580 --app-dir "${SCRIPT_DIR}"
