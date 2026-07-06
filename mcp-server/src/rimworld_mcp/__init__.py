"""RimWorld MCP Bridge — configuration."""
import os

# Read from env var (set by start.sh for WSL2), fall back to localhost
RIMWORLD_API_BASE = os.environ.get(
    "RIMWORLD_API_BASE", "http://localhost:8765"
)
RIMWORLD_TIMEOUT = float(os.environ.get("RIMWORLD_TIMEOUT", "10.0"))
RIMWORLD_STARTUP_WAIT = float(os.environ.get("RIMWORLD_STARTUP_WAIT", "30.0"))
