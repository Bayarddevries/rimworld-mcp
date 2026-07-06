# AGENTS.md — Colony Command Dashboard

## Project Context

This dashboard is the user-facing UI for the RimWorld MCP Bridge mod. It replaces the basic Hermes bridge dashboard with a full-featured mobile-responsive web app served directly from the RimWorld game process.

**Key constraint:** Everything must work from a phone browser via LAN or Tailscale. No external servers. No CORS issues (same-origin). The mod's HTTP server runs inside RimWorld on Windows port 8765.

## File Locations

| Path | Description |
|------|-------------|
| `~/rimworld-dashboard/dashboard.html` | Single-file dashboard (HTML + CSS + JS) |
| `~/rimworld-dashboard/AGENTS.md` | This file — agent instructions |
| `~/rimworld-dashboard/PLAN.md` | Full feature parity plan (Phase 1-3) |
| `~/rimworld-dashboard/README.md` | Project docs |
| `~/rimworld-mcp/RimworldMcp/Source/` | All C# API handlers |
| `~/rimworld-mcp/RimworldMcp/Source/HttpServer.cs` | HTTP server + route registration |
| `~/rimworld-mcp/RimworldMcp/Source/GameBridge.cs` | Thread marshaling to game thread |
| `~/rimworld-mcp/RimworldMcp/Source/MCPLifecycle.cs` | GameComponent (Bridge.Tick) setup |
| `/mnt/c/Rimworld/Mods/RimworldMcpBridge/Assemblies/` | Deployed DLL + dashboard.html |

## Dashboard Development Workflow

1. Edit `~/rimworld-dashboard/dashboard.html`
2. Copy to mod folders: `cp ~/rimworld-dashboard/dashboard.html "/mnt/c/Rimworld/Mods/RimworldMcpBridge/Assemblies/" && cp ~/rimworld-dashboard/dashboard.html "/mnt/c/Rimworld/Mods/RimworldMcpBridge/Current/Assemblies/"`
3. User refreshes browser (no game restart needed for HTML changes)
4. Always verify JS is valid: `node -e "const fs=require('fs');new Function(fs.readFileSync('...').match(/<script>([\s\S]*?)<\/script>/)[1]);"`
5. User tests from phone and reports issues

## Mod Development Workflow

1. Edit C# source in `~/rimworld-mcp/RimworldMcp/Source/`
2. Build: `cd ~/rimworld-mcp/RimworldMcp && bash build.sh`
3. Copy DLL: `cp build/Assemblies/RimworldMcp.dll "/mnt/c/Rimworld/Mods/RimworldMcpBridge/Assemblies/"`
4. Game restart required (DLL locked while game runs)
5. Copy dashboard.html alongside the DLL
6. Verify: `curl http://172.18.208.1:8765/api/health`

## Critical Architecture: No Harmony Patches

**Harmony patches have been entirely removed.** Previously they caused conflicts with the Hospitality mod and made all API calls hang. Instead:

- **Bridge.Tick()** runs via `RimworldMcpGameComp.GameComponentTick()` — a GameComponent auto-registered by a background poller. No Harmony needed.
- **HTTP server** starts directly from the mod constructor via `StartServer()`. Works on the main menu.
- **Fail-fast bridge** — `GameBridge.Execute()` returns "Game is not loaded yet" immediately if Current.Game is null, instead of blocking forever.

## API Quirks to Remember

- POST `/api/colony/time` uses `{action: "pause|play|fast|superfast"}` NOT `{speed: ...}`
- GET `/api/pawns/{id}` returns `{success: true, data: object}` (single object, not array)
- GET `/api/events/history` returns `{success: true, data: {count, events}}` — extract `data.events`
- `BuildJsonObject()` does NOT quote values — use `ToJsonString()` for all string fields
- Numeric strings (e.g. `"42"`) become JSON numbers; `"true"`/`"false"` become JSON booleans
- Route matching uses **longest-prefix** (e.g. `/api/pawns/` matches before `/api/pawns`)
- ParseSimpleJson() handles flat key-value only — nested JSON needs manual brace-depth parsing

## C# Development Pitfalls (RimWorld 1.6)

| Mistake | Correct |
|---------|---------|
| `Name.ToStringShort()` | `Name.ToStringShort` (property, not method) |
| `BodyPartRecord.label` | `BodyPartRecord.Label` (capital L) |
| `summaryHealth.Summary` | `summaryHealth.SummaryHealthPercent` |
| `pawn.IsPrisonerOfMyFaction` | `pawn.guest.IsPrisoner && pawn.guest.HostFaction == Faction.OfPlayer` |
| `JobDefOf.HaulToStockpile` | `JobDefOf.HaulToCell` |
| `TaggedString?.ToString()` | `TaggedString.ToString()` (struct, can't null-conditional) |
| `continue` inside try-catch | Use `if/else` flow control instead |

## Network Architecture

- Game runs on Windows at `C:\Rimworld\Mods\RimworldMcpBridge\`
- HTTP server binds to `http://*:8765/` (all interfaces)
- Windows LAN IP: `10.0.0.247`
- Tailscale IP: `100.65.25.105`
- WSL2 IP: `172.18.208.1` (only accessible from Windows via localhost)
- **DO NOT use WSL proxy server** — the mod itself serves the dashboard directly
- Windows Firewall may block LAN access to port 8765; Tailscale usually bypasses this

## Current Endpoints (Phase 1-2 Complete)

### GET Routes
| Path | Purpose |
|------|---------|
| `/api/health` | Game status |
| `/api/version` | Version info |
| `/api/colony/overview` | Colony stats |
| `/api/colony/resources` | Stockpiled resources |
| `/api/colony/paused` | Pause/speed state |
| `/api/colony/autopause` | Auto-pause config |
| `/api/pawns` | All colonists |
| `/api/pawns/{id}` | Pawn detail |
| `/api/research` | Research tree |
| `/api/map` | Map info |
| `/api/map/bills` | Production bills |
| `/api/events/history` | Event history |
| `/api/events/feed` | Live event feed |
| `/api/events/storyteller` | Storyteller info |
| `/api/goals` | Goal tracking |
| `/api/prisoners` | Prisoner list |
| `/api/zones` | Stockpile zones |

### POST Routes
| Path | Purpose |
|------|---------|
| `/api/colony/time` | Set speed/pause |
| `/api/colony/forbid` | Forbid/unforbid item |
| `/api/colony/command` | Toggle draft |
| `/api/colony/autopause` | Toggle auto-pause |
| `/api/pawns/skill` | Set skill level |
| `/api/pawns/trait` | Add trait |
| `/api/pawns/health` | Heal pawn |
| `/api/pawns/needs` | Set needs |
| `/api/pawns/gear` | Equip gear |
| `/api/pawns/unequip` | Unequip gear |
| `/api/pawns/inspire` | Inspire pawn |
| `/api/pawns/priorities` | Set work priorities |
| `/api/pawns/inventory` | Get inventory |
| `/api/pawns/rename` | Rename pawn |
| `/api/pawns/surgery` | Install/remove bionics |
| `/api/pawns/command` | Direct commands (haul/build/tend/rescue) |
| `/api/spawn/thing` | Spawn item |
| `/api/spawn/pawn` | Spawn colonist |
| `/api/research/unlock` | Unlock research |
| `/api/events/trigger` | Trigger story event |
| `/api/map/bills/add` | Add production bill |
| `/api/map/bills/remove` | Remove production bill |
| `/api/map/bills/suspend` | Suspend/resume bill |
| `/api/prisoners/action` | Recruit/release/execute prisoner |
| `/api/goals/set` | Set goal |
| `/api/goals/remove` | Remove goal |
| `/api/chat/send` | Send chat message |
| `/api/chat/respond` | Respond in chat |
| `/api/save` | Save game |

## Dashboard Tabs

| Tab | Features |
|-----|----------|
| **Overview** | Colony stats, resources, quick events, speed control, auto-pause, debug log |
| **Pawns** | Pawn list, search, detail (Health/Skills/Gear/Needs/Work/Inventory/Bionics), draft, heal, inspire, rename, direct commands |
| **Research** | Research tree, search, filter by completed, unlock |
| **Spawn** | Quick spawn items, custom spawn, spawn colonist |
| **Bills** | Production bills grouped by work bench, suspend/resume, remove |
| **Zones** | Stockpile zone list |
| **Prisoners** | Prisoner list, recruit, release, reduce resistance |
| **Chronicle** | Narrative event timeline |
| **Goals** | Goal tracking with progress |
| **Chat** | In-game chat |

## Current State (July 6, 2026)

- **Phase 1 Complete:** Resources, pawns, research, spawn, events, goals, chat, priorities, forbid, inventory, equip/unequip, rename, work priorities
- **Phase 2 Complete:** Bills (list/add/remove/suspend), prisoners (list/recruit/release), bionics (install/remove), direct commands (haul/build/tend/rescue), zones (list)
- **Architecture:** Harmony patches removed entirely. GameComponent handles Bridge.Tick(). Server starts from mod constructor.
- **Test workflow:** Run `bash ~/.hermes/skills/gaming/rimworld-dashboard/scripts/test-suite.sh` before every deploy
