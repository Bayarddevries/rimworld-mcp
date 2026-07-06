# Colony Command — RimWorld Dashboard

A mobile-responsive web dashboard for RimWorld, served directly from the RimWorld MCP Bridge mod. View colony status, manage pawns, control production, direct commands, and more — all from your phone.

## Architecture

```
┌──────────────────────┐      ┌──────────────────┐
│  Phone Browser       │─────►│ RimWorld Game    │
│  (dashboard.html)    │ HTTP │ (C# HTTP API)    │
│                     │◄─────│ port 8765        │
└──────────────────────┘      └──────────────────┘
```

The dashboard HTML is served by the RimWorld MCP Bridge mod itself — no external server needed. The mod's `DashboardHandler` reads `dashboard.html` from disk alongside the DLL.

## Features (Phase 1-2 Complete)

### Colony Management
- **Overview:** Colony stats, resources, speed control, auto-pause, threat slider
- **Pawns:** Full list with search, detail view with Health/Skills/Gear/Needs/Work/Inventory/Bionics tabs
- **Direct Commands:** Order pawns to haul, build, tend, or rescue
- **Work Priorities:** Set per-pawn priority grid (1-4) for all work types

### Production & Economy
- **Bills:** View all production bills by work bench, suspend/resume, remove
- **Resources:** Stockpile view with forbid/unforbid
- **Bionics:** Install/remove implants and body parts on colonists

### Prisoners
- **List:** View all prisoners with stats
- **Actions:** Recruit, release, reduce resistance

### Events & Goals
- **Chronicle:** Narrative event timeline with severity indicators
- **Goal tracking:** Set and track colony goals with progress bars

### Utilities
- **Research:** View tree, search, unlock techs
- **Spawn:** Quick item spawns + custom spawn, spawn colonist
- **Chat:** In-game chat send/receive
- **Save:** Save game from dashboard

## Files

| File | Purpose |
|------|---------|
| `dashboard.html` | Single-file web dashboard (HTML + CSS + JS) |
| `AGENTS.md` | Agent instructions for development |
| `PLAN.md` | Full feature parity plan |
| `server.py` | Legacy Python proxy (no longer needed — kept for reference) |

## Deployment

Copy `dashboard.html` and the compiled `RimworldMcp.dll` to the mod's Assemblies folder:

```bash
# HTML only (no restart needed):
cp dashboard.html "/mnt/c/Rimworld/Mods/RimworldMcpBridge/Assemblies/"
cp dashboard.html "/mnt/c/Rimworld/Mods/RimworldMcpBridge/Current/Assemblies/"

# DLL (requires game restart):
cp build/Assemblies/RimworldMcp.dll "/mnt/c/Rimworld/Mods/RimworldMcpBridge/Assemblies/"
```

Dashboard picks up automatically (re-reads from disk every 30 seconds). The DLL requires a game restart.

## Access

- **Local PC:** http://localhost:8765/dashboard
- **LAN:** http://10.0.0.247:8765/dashboard
- **Tailscale:** http://100.65.25.105:8765/dashboard

## API Documentation

Full API docs in [AGENTS.md](AGENTS.md). Key endpoints:

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/api/health` | Game status |
| GET | `/api/pawns` | All colonists |
| GET | `/api/colony/overview` | Colony stats |
| GET | `/api/map/bills` | Production bills |
| GET | `/api/prisoners` | Prisoner list |
| GET | `/api/zones` | Stockpile zones |
| POST | `/api/pawns/command` | Direct pawn commands |
| POST | `/api/pawns/surgery` | Bionic install/remove |
| POST | `/api/map/bills/suspend` | Suspend/resume bills |
| POST | `/api/prisoners/action` | Recruit/release |

## Known Issues

- Event feed init may fail silently (non-critical, event tracking still works via periodic checks)
- Bionic install falls back to whole-body if body part not specified
- Stockpile zone creation not yet available (list only)

## Development

See [AGENTS.md](AGENTS.md) for full development workflow, C# pitfalls, and API quirks.
