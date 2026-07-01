"""HTTP client for the RimWorld MCP Bridge mod API."""

import json
import time
from typing import Any

import httpx

from . import RIMWORLD_API_BASE, RIMWORLD_TIMEOUT, RIMWORLD_STARTUP_WAIT


class RimworldClient:
    """Client for the RimWorld MCP Bridge HTTP API."""

    def __init__(self, base_url: str = RIMWORLD_API_BASE, timeout: float = RIMWORLD_TIMEOUT):
        self.base_url = base_url.rstrip("/")
        self.timeout = timeout
        self._client = httpx.Client(base_url=self.base_url, timeout=timeout)

    def wait_for_connection(self, max_wait: float = RIMWORLD_STARTUP_WAIT) -> bool:
        """Wait for the RimWorld API server to become available."""
        start = time.time()
        while time.time() - start < max_wait:
            try:
                resp = self._client.get("/api/health", timeout=3.0)
                if resp.status_code == 200:
                    data = resp.json()
                    if data.get("data", {}).get("game_loaded") == "true":
                        return True
                    return True  # server is up even if no game loaded yet
            except (httpx.ConnectError, httpx.TimeoutException, json.JSONDecodeError):
                pass
            time.sleep(1)
        return False

    def get(self, path: str) -> dict[str, Any]:
        """Make a GET request to the RimWorld API."""
        resp = self._client.get(f"/api/{path.lstrip('/')}")
        resp.raise_for_status()
        data = resp.json()
        if not data.get("success", False):
            raise RuntimeError(data.get("error", "Unknown error"))
        return data.get("data", {})

    def post(self, path: str, body: dict[str, Any] | None = None) -> dict[str, Any]:
        """Make a POST request to the RimWorld API."""
        resp = self._client.post(
            f"/api/{path.lstrip('/')}",
            content=json.dumps(body or {}),
            headers={"Content-Type": "application/json"},
        )
        resp.raise_for_status()
        data = resp.json()
        if not data.get("success", False):
            raise RuntimeError(data.get("error", "Unknown error"))
        return data.get("data", {})

    def check_health(self) -> dict[str, Any]:
        """Check if the API is healthy."""
        resp = self._client.get("/api/health", timeout=5.0)
        return resp.json().get("data", {})

    def get_version(self) -> dict[str, Any]:
        """Get version info."""
        resp = self._client.get("/api/version", timeout=5.0)
        return resp.json().get("data", {})

    def __del__(self):
        if hasattr(self, "_client"):
            self._client.close()
