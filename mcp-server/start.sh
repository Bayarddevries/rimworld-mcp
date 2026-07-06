#!/bin/bash
# Auto-detect Windows host IP from WSL2 and start the MCP server
set -euo pipefail

# Get the Windows host IP (default gateway from WSL's perspective)
WIN_IP=$(ip route show default | awk '{print $3}')

echo "Detected Windows host IP: $WIN_IP"
echo "Starting RimWorld MCP server..."

export RIMWORLD_API_BASE="http://${WIN_IP}:8765"

# Activate venv and run the server
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
cd "$SCRIPT_DIR"
exec .venv/bin/python3 -m rimworld_mcp.server
