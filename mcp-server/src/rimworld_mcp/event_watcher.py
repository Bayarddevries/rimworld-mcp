"""RimWorld event watcher daemon.

Connects to the RimWorld mod's SSE event stream
and dispatches events to:
  1. A rolling JSONL log file at ~/rimworld-events.jsonl
  2. Optionally, Telegram (if TELEGRAM_BOT_TOKEN and TELEGRAM_CHAT_ID are set)
  3. Optionally, a named pipe or flag file for other processes to detect "new event"

Usage:
    python -m rimworld_mcp.event_watcher              # runs forever
    python -m rimworld_mcp.event_watcher --once        # poll /api/events/feed once and exit

Environment:
    TELEGRAM_BOT_TOKEN   — Telegram bot token for alerts
    TELEGRAM_CHAT_ID     — Telegram chat/group ID to send alerts to
"""

import json
import os
import sys
import time
import urllib.request
import urllib.error
from datetime import datetime
from pathlib import Path

from . import RIMWORLD_API_BASE

# ── Configuration ────────────────────────────────────────────────────

RIMWORLD_API = RIMWORLD_API_BASE
SSE_URL = f"{RIMWORLD_API}/api/events/stream"
FEED_URL = f"{RIMWORLD_API}/api/events/feed"
EVENTS_LOG = Path.home() / "rimworld-events.jsonl"
RECONNECT_DELAY = 2.0  # seconds before reconnecting on disconnect
POLL_INTERVAL = 1.0    # seconds between SS Event reads
MAX_LOG_SIZE = 10_000   # trim to this many lines

TELEGRAM_BOT_TOKEN = os.environ.get("TELEGRAM_BOT_TOKEN")
TELEGRAM_CHAT_ID = os.environ.get("TELEGRAM_CHAT_ID")


# ── SSE Client ───────────────────────────────────────────────────────

def connect_sse() -> None:
    """Connect to the SSE endpoint and process events as they arrive."""
    log_info(f"Connecting to SSE stream at {SSE_URL}")

    req = urllib.request.Request(SSE_URL, method="GET")
    req.add_header("Accept", "text/event-stream")

    while True:
        try:
            response = urllib.request.urlopen(req, timeout=None)
            log_info("Connected to event stream")
            _read_stream(response)
        except urllib.error.HTTPError as e:
            log_error(f"HTTP error connecting to SSE: {e.code} {e.reason}")
        except urllib.error.URLError as e:
            log_error(f"Cannot reach RimWorld API (server down?): {e.reason}")
        except OSError as e:
            log_error(f"SSE connection lost: {e}")
        except Exception as e:
            log_error(f"SSE error: {e}")

        log_info(f"Reconnecting in {RECONNECT_DELAY}s...")
        time.sleep(RECONNECT_DELAY)


def _read_stream(response: urllib.request.addinfourl) -> None:
    """Read SSE events from an open HTTP response."""
    event_type = ""
    data_buffer = ""

    for line_bytes in response:
        line = line_bytes.decode("utf-8", errors="replace").rstrip("\r\n")

        if line.startswith(":"):
            # Comment / keepalive
            continue

        if line.startswith("event: "):
            event_type = line[7:].strip()
            continue

        if line.startswith("data: "):
            data_buffer += line[6:]
            continue

        if line == "" and data_buffer:
            # Empty line = end of event
            _handle_sse_event(event_type, data_buffer)
            event_type = ""
            data_buffer = ""


CRITICAL_SEVERITIES = {"critical"}
ALERT_SEVERITIES = {"critical", "warning"}


def _handle_sse_event(event_type: str, data: str) -> None:
    """Process a single SSE event (may contain multiple game events)."""
    try:
        payload = json.loads(data)
    except json.JSONDecodeError:
        return

    events = payload.get("events", [])
    if not events:
        return

    # Write all events to log file
    _append_to_log(events)

    # Check for events that need immediate attention
    urgent = [e for e in events if e.get("severity") in ALERT_SEVERITIES]
    critical = [e for e in events if e.get("severity") in CRITICAL_SEVERITIES]

    if urgent:
        _notify(urgent, urgent_=True)

    if critical:
        _notify(critical, urgent_=True)


# ── File Logging ─────────────────────────────────────────────────────


def _append_to_log(events: list[dict]) -> None:
    """Append events to the rolling JSONL log file."""
    try:
        with open(EVENTS_LOG, "a") as f:
            for evt in events:
                evt["_logged_at"] = datetime.now().isoformat()
                f.write(json.dumps(evt) + "\n")

        # Trim if too large
        if EVENTS_LOG.stat().st_size > 500_000:
            _trim_log()
    except OSError:
        pass


def _trim_log() -> None:
    """Keep only the last MAX_LOG_SIZE lines."""
    try:
        lines = EVENTS_LOG.read_text().strip().split("\n")
        if len(lines) > MAX_LOG_SIZE:
            EVENTS_LOG.write_text("\n".join(lines[-MAX_LOG_SIZE:]) + "\n")
    except OSError:
        pass


# ── Notifications ────────────────────────────────────────────────────


def _notify(events: list[dict], urgent_: bool = False) -> None:
    """Send notifications for important events."""
    # Build a concise summary
    lines = []
    for evt in events:
        sev = evt.get("severity", "info").upper()
        desc = evt.get("description", "?")
        lines.append(f"[{sev}] {desc}")

    summary = "\n".join(lines)

    # Write to a well-known flag file (for other processes to detect)
    _write_flag(summary)

    # Telegram alert
    if TELEGRAM_BOT_TOKEN and TELEGRAM_CHAT_ID:
        _telegram_alert(summary)


def _write_flag(summary: str) -> None:
    """Write the latest urgent event to a flag file."""
    try:
        flag_path = Path("/tmp/rimworld-urgent-event")
        flag_path.write_text(
            json.dumps({
                "timestamp": datetime.now().isoformat(),
                "summary": summary,
            })
        )
    except OSError:
        pass


def _telegram_alert(text: str) -> None:
    """Send a message via Telegram bot API."""
    payload = json.dumps({
        "chat_id": TELEGRAM_CHAT_ID,
        "text": f"🏚️ RimWorld Alert\n\n{text}",
        "parse_mode": "HTML",
    }).encode("utf-8")

    req = urllib.request.Request(
        f"https://api.telegram.org/bot{TELEGRAM_BOT_TOKEN}/sendMessage",
        data=payload,
        headers={"Content-Type": "application/json"},
    )

    try:
        urllib.request.urlopen(req, timeout=10)
    except Exception:
        pass  # Telegram failures shouldn't crash the watcher


# ── One-shot mode (poll /api/events/feed) ────────────────────────────


def poll_once() -> None:
    """Poll the event feed once and print results. Used for testing."""
    import httpx

    try:
        client = httpx.Client(base_url=RIMWORLD_API, timeout=10)
        resp = client.get("/api/events/feed")
        data = resp.json()
        events = data.get("data", {}).get("events", [])
        print(json.dumps({"count": len(events), "events": events}, indent=2))
    except Exception as e:
        print(json.dumps({"error": str(e)}))
        sys.exit(1)


# ── Helpers ──────────────────────────────────────────────────────────


def log_info(msg: str) -> None:
    print(f"[rimworld-watcher] {msg}", flush=True)


def log_error(msg: str) -> None:
    print(f"[rimworld-watcher] ERROR: {msg}", flush=True)


# ── Entry Point ──────────────────────────────────────────────────────


def main():
    if "--once" in sys.argv:
        poll_once()
        return

    log_info("RimWorld Event Watcher starting")
    log_info(f"Events log: {EVENTS_LOG}")

    if TELEGRAM_BOT_TOKEN and TELEGRAM_CHAT_ID:
        log_info("Telegram alerts: ENABLED")
    else:
        log_info("Telegram alerts: disabled (set TELEGRAM_BOT_TOKEN and TELEGRAM_CHAT_ID)")

    connect_sse()


if __name__ == "__main__":
    main()
