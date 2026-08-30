"""
FastAPI dependency sketch — single-process / learning only.
Use RetryShield gateway when multiple workers share the same API.
"""

from __future__ import annotations

import hashlib
import json
from typing import Any

from fastapi import Header, HTTPException, Request
from fastapi.responses import JSONResponse

_store: dict[str, dict[str, Any]] = {}


def _fingerprint(method: str, path: str, body: bytes) -> str:
    raw = f"{method}:{path}:{body.decode('utf-8', errors='replace')}"
    return hashlib.sha256(raw.encode("utf-8")).hexdigest()


async def require_idempotency(
    request: Request,
    idempotency_key: str | None = Header(default=None, alias="Idempotency-Key"),
) -> str:
    if not idempotency_key:
        raise HTTPException(status_code=400, detail="Idempotency-Key required")

    body = await request.body()
    fp = _fingerprint(request.method, request.url.path, body)
    existing = _store.get(idempotency_key)

    if existing:
        if existing["fingerprint"] != fp:
            raise HTTPException(status_code=422, detail="conflict")
        if existing["status"] == "completed":
            # Caller should short-circuit with replay — see demo route below.
            request.state.idempotency_replay = existing
            return idempotency_key
        if existing["status"] == "processing":
            raise HTTPException(status_code=409, detail="processing")

    _store[idempotency_key] = {"fingerprint": fp, "status": "processing"}
    request.state.idempotency_key = idempotency_key
    request.state.idempotency_fp = fp
    return idempotency_key


def complete(request: Request, status_code: int, payload: dict[str, Any]) -> JSONResponse:
    key = getattr(request.state, "idempotency_key", None)
    fp = getattr(request.state, "idempotency_fp", None)
    if key and fp:
        _store[key] = {
            "fingerprint": fp,
            "status": "completed",
            "status_code": status_code,
            "body": payload,
        }
    response = JSONResponse(payload, status_code=status_code)
    response.headers["Idempotency-Status"] = "created"
    return response


def maybe_replay(request: Request) -> JSONResponse | None:
    replay = getattr(request.state, "idempotency_replay", None)
    if not replay:
        return None
    response = JSONResponse(replay["body"], status_code=replay["status_code"])
    response.headers["Idempotency-Status"] = "replayed"
    return response
