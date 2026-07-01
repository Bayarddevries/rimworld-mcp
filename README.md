# RimWorld MCP Bridge

An AI agent bridge for RimWorld. Lets Hermes (or any MCP-compatible agent) read and control your colony in real time — with event-driven alerts, goal tracking, and colony narrative logging.

## Architecture

```
┌──────────────────┐     ┌──────────────────┐     ┌──────────────────┐
│   Hermes Agent   │◄───►│  MCP Server      │◄───►│ RimWorld Mod     │
│   (LLM)          │     │  (Python)        │     │ (C# + HTTP API)  │
└──────────────────┘     │  SSE client      │     │ SSE stream       │
                         │  Event watcher   │     │ Chat window      │
                         └──────┬───────────┘     │ Goal tracker     │
                                │                 │ Event feed       │
                         Telegram / File          └──────────────────┘
```

**RimWorld Mod** — C# mod that runs an HTTP API + SSE event stream inside RimWorld using `System.Net.HttpListener`.

**MCP Server** — Python server that translates MCP tool calls to RimWorld HTTP API calls. All standard MCP tools are available to any MCP client.

**Event Watcher** — Optional background daemon that connects to the SSE stream and pushes real-time alerts to Telegram or a local log file.

**Hermes** — connects via standard MCP protocol. No Hermes-specific features required.

## Features

### Core Control (Read)
| Tool | Description |
|------|-------------|
| `rimworld_status` | Connection health check |
| `rimworld_colony_overview` | Colony stats: wealth, pawns, biome, season |
| `rimworld_resources` | Stockpiled resources with counts |
| `rimworld_list_pawns` | All colonists with skills, traits, mood, health, gear |
| `rimworld_get_pawn` | Detailed info on a specific colonist |
| `rimworld_list_research` | Research tree: completed, in-progress, available |
| `rimworld_map_info` | Weather, temperature, time of day |
| `rimworld_list_bills` | Production bills at work tables |

### Core Control (Write)
| Tool | Description |
|------|-------------|
| `rimworld_set_skill` | Set skill level (0-20) |
| `rimworld_add_trait` | Add a trait to a colonist |
| `rimworld_heal_pawn` | Heal all injuries or a specific body part |
| `rimworld_set_need` | Set food, rest, mood, or recreation level |
| `rimworld_equip_gear` | Equip weapons or apparel |
| `rimworld_inspire_pawn` | Give an inspiration |
| `rimworld_draft_pawn` | Draft or release a colonist |
| `rimworld_spawn_item` | Spawn items/resources |
| `rimworld_spawn_colonist` | Spawn a new colonist |
| `rimworld_trigger_event` | Trigger story events (raid, traders, etc.) |
| `rimworld_complete_research` | Instantly complete a research project |
| `rimworld_add_bill` | Add a production bill |
| `rimworld_save` | Save the game |

### 💬 In-Game Chat
| Tool | Description |
|------|-------------|
| `rimworld_check_messages` | Check if the player typed in chat |
| `rimworld_chat_respond` | Send a response to the in-game chat window |

Press **F12** in-game to open the chat window. Type messages to Hermes. Hermes responds inline.

### 📡 Event Feed & Streaming
| Tool / Feature | Description |
|----------------|-------------|
| `rimworld_check_events` | Poll for new game events since last check |
| `rimworld_event_history` | Full event log for the current session |
| **SSE Stream** | `/api/events/stream` — real-time push of events as they happen |
| **Event Watcher** | Background daemon that connects to SSE and alerts you |

The event feed captures:
- **All game letters** (raids, traders, quests, inspirations) via Harmony patch
- **Colonist deaths** detected by periodic state comparison
- **Food supply critical** warnings
- **Research completed** notifications

### 🎯 Goal Tracking
| Tool | Description |
|------|-------------|
| `rimworld_set_goal` | Track a resource/research/colonist/wealth goal |
| `rimworld_list_goals` | Check goal progress |
| `rimworld_remove_goal` | Remove a completed or stale goal |

Goals auto-update each tick. Hermes can check progress and notify when met.

### ⚡ Batch Execution
| Tool | Description |
|------|-------------|
| `rimworld_batch` | Execute multiple API calls in one round-trip |

Use for multi-step plans: spawn items, set skills, trigger events, inspect results — all in a single MCP tool call.

### 📖 Colony Narrative Journaling
Hermes can poll the event feed and compose narrative summaries:

```
"CryBaby colony, Spring 5505. A pirate raid of 8 was repelled —
  Alice took a grenade to the leg but Lumi's field surgery saved her.
  Research on microelectronics completed — turrets are online.
  Stockpiled 2,000 steel. Three new goals set for summer."
```

This is a Hermes-layer feature (not a mod feature). The event feed provides the raw data; Hermes formats it.

## Installation

### 1. Prerequisites
- RimWorld 1.6 installed (any path)
- Docker (for building the C# mod)
- Python 3.10+

### 2. Build and Install the RimWorld Mod

```bash
cd RimworldMcp
chmod +x build.sh
./build.sh
```

This produces a `build/` folder. To install:

1. Copy `build/` to `RimWorld/Mods/` and rename to `RimworldMcpBridge`
2. Enable **RimWorld MCP Bridge** in the RimWorld mod menu (after Harmony)
3. Start or load a game
4. Verify: `curl http://localhost:8765/api/health`

### 3. Install the MCP Server

```bash
cd mcp-server
python3 -m venv .venv
source .venv/bin/activate
pip install -e .
```

### 4. Configure Hermes

Add to `~/.hermes/config.yaml`:

```yaml
mcp:
  servers:
    rimworld:
      command: /path/to/rimworld-mcp/mcp-server/.venv/bin/python3
      args: ["-m", "rimworld_mcp.server"]
```

Restart Hermes. The `rimworld_*` tools appear when RimWorld is running.

### 5. (Optional) Run the Event Watcher

The event watcher connects to the SSE stream for real-time event dispatch:

```bash
# Basic — writes events to ~/rimworld-events.jsonl
rimworld-watcher

# With Telegram alerts
TELEGRAM_BOT_TOKEN=your_bot_token \
TELEGRAM_CHAT_ID=your_chat_id \
rimworld-watcher

# Test mode — poll once and exit
rimworld-watcher --once
```

Run as a background process:
```bash
nohup rimworld-watcher &
```

### 6. (Optional) Narrative Journal Cron

Add a Hermes cron job for periodic colony narrative summaries:

```yaml
# In hermes
cron:
  jobs:
    - name: "Colony Journal"
      schedule: "0 */2 * * *"   # every 2 hours
      prompt: "Read rimworld_even/feed, get colony overview, and write a narrative summary of what happened since my last check. Focus on major events, losses, achievements. Keep it to 3-5 sentences."
```

## API Reference

### REST Endpoints (port 8765)

#### GET Endpoints
| Endpoint | Description |
|----------|-------------|
| `/api/health` | Connection check |
| `/api/version` | Version info |
| `/api/pawns` | List colonists |
| `/api/pawns/{id}` | Single pawn by name/ID |
| `/api/colony/overview` | Colony stats |
| `/api/colony/resources` | Stockpiled resources |
| `/api/research` | Research status |
| `/api/map` | Weather, biome, season |
| `/api/map/bills` | Production bills |
| `/api/events/storyteller` | Storyteller info |
| `/api/chat/pending` | Unread player messages |
| `/api/chat/messages` | All chat messages |
| `/api/chat/pending_responses` | Unread Hermes responses |
| `/api/events/feed` | Pending events (clears queue) |
| `/api/events/history` | All events this session |

#### SSE Endpoint
| Endpoint | Description |
|----------|-------------|
| `/api/events/stream` | **Server-Sent Events** stream. Push-based, real-time. |

Connect with: `curl -N http://localhost:8765/api/events/stream`

Events arrive as:
```
event: rimworld_event
data: {"count":2,"events":[{"type":"RaidEnemy","description":"8 pirates approaching","severity":"critical","tick":12345,"timestamp":"14:23:01"}, ...]}
```

The stream sends keepalive pings every ~10 seconds. Reconnect on disconnect.

#### POST Endpoints
| Endpoint | Body Fields | Description |
|----------|-------------|-------------|
| `/api/pawns/skill` | `pawn, skill, level` | Set skill |
| `/api/pawns/trait` | `pawn, trait, [degree]` | Add trait |
| `/api/pawns/health` | `pawn, action, [body_part]` | Heal/injure |
| `/api/pawns/needs` | `pawn, need, value` | Set need |
| `/api/pawns/gear` | `pawn, item, [slot]` | Equip gear |
| `/api/pawns/inspire` | `pawn, [inspiration]` | Inspire |
| `/api/spawn/thing` | `thing, [count,x,z]` | Spawn items |
| `/api/spawn/pawn` | `[kind,faction]` | Spawn pawn |
| `/api/research/unlock` | `tech, [action]` | Complete research |
| `/api/events/trigger` | `event, [points]` | Trigger event |
| `/api/colony/stockpile` | `item, count` | Add resources |
| `/api/colony/command` | `command, target` | Draft/undraft |
| `/api/map/bills/add` | `recipe, [building]` | Add bill |
| `/api/save` | `[slot]` | Save game |
| `/api/chat/send` | `text` | Send player message |
| `/api/chat/respond` | `text` | Send Hermes response |
| `/api/goals/set` | `id, description, type, target, [target_item]` | Set goal |
| `/api/goals/remove` | `id` | Remove goal |
| `/api/batch` | `calls: [{method, path, body}]` | Batch execution |

### Event Types

Events captured by the mod include:

| Type | Severity | Description |
|------|----------|-------------|
| `RaidEnemy` | critical | Enemy raid incoming |
| `ManhunterPack` | critical | Manhunting animal pack |
| `ColonistDied` / `colonist_died` | critical | Colonist death |
| `MechCluster` | critical | Mechanoid cluster landed |
| `Infestation` | critical | Insect infestation |
| `FoodCritical` / `food_critical` | warning | Food supply low |
| `TraderCaravan` | info | Trade caravan arrived |
| `WandererJoin` | info | Wanderer joined colony |
| `ResearchCompleted` / `research_completed` | info | Research finished |
| `Eclipse` | info | Solar eclipse |
| `CropBlight` | warning | Blight on crops |
| `VolcanicWinter` | warning | Volcanic winter event |
| `ToxicFallout` | warning | Toxic fallout |
| `Flashstorm` | info | Flash storm |
| `ColdSnap` | warning | Cold snap |
| `HeatWave` | warning | Heat wave |

## Self-Improvement (for LLM agents)

The architecture enables three levels of autonomous colony management:

### Level 1 — Reactive Heuristics
Cron job checks heuristics every few minutes: mood < 30% → add recreation, food < 500 → suggest hunting, research idle → queue next project.

### Level 2 — LLM Analysis
A recurring Hermes cron job reads full colony state and identifies non-obvious bottlenecks the heuristics would miss (e.g., trait-based mood conflicts, room quality issues, work scheduling inefficiencies).

### Level 3 — Event-Driven Narrative
The SSE stream + watcher daemon replaces cron entirely. Events push in real-time. The watcher can trigger Hermes workflows on specific event types (raid, death, new research) with zero polling latency.

## Development

### Project Structure
```
rimworld-mcp/
├── RimworldMcp/                    # C# RimWorld mod (20 source files)
│   ├── About/About.xml             # Mod metadata
│   ├── Source/
│   │   ├── RimworldMcpMod.cs       # Mod entry point
│   │   ├── GameBridge.cs           # Main-thread marshalling
│   │   ├── HttpServer.cs           # HTTP API server (30+ routes, SSE)
│   │   ├── HarmonyPatches.cs       # Game lifecycle + chat hotkey + event intercept
│   │   ├── EventFeedManager.cs     # Rolling event buffer + periodic checks
│   │   ├── GoalManager.cs          # Goal tracking with progress
│   │   ├── ChatManager.cs          # Message queue system
│   │   ├── ChatWindow.cs           # In-game F12 chat UI
│   │   ├── ChatKeybinding.cs       # F12 hotkey (Harmony)
│   │   └── ApiHandlers/            # One handler class per domain
│   │       ├── PawnsHandler.cs
│   │       ├── ColonyHandler.cs
│   │       ├── ResearchHandler.cs
│   │       ├── SpawnHandler.cs
│   │       ├── EventsHandler.cs
│   │       ├── EventFeedHandler.cs  # Feed + history + SSE stream
│   │       ├── GoalHandler.cs
│   │       ├── MapHandler.cs
│   │       ├── BillsHandler.cs
│   │       ├── ChatHandler.cs
│   │       ├── BatchHandler.cs
│   │       └── HealthHandler.cs
│   └── build.sh                    # Docker-based build
├── mcp-server/                     # Python MCP server
│   ├── pyproject.toml
│   └── src/rimworld_mcp/
│       ├── __init__.py             # Config defaults
│       ├── server.py               # 26 MCP tools
│       ├── rimworld_client.py      # HTTP client to mod API
│       └── event_watcher.py        # SSE daemon + Telegram alerts
├── hermes-mcp-config.yaml          # Sample Hermes config
└── README.md
```

### Building
```bash
cd RimworldMcp && ./build.sh
```

The mod compiles inside a `mono:6.12` Docker container against the actual RimWorld 1.6 game assemblies. Only the game's own assemblies are referenced (no Mono SDK base libs) to avoid type conflicts.

### Adding a new API endpoint
1. Add the handler method in the appropriate `ApiHandlers/*.cs`
2. Register the route in `HttpServer.cs`
3. Add an MCP tool in `server.py`
4. Rebuild with `./build.sh`

## Safety Notes
- **Save first!** Use `rimworld_save` before major operations
- All game state access is marshalled to the main thread via `GameBridge`
- The SSE stream is read-only — it cannot inject game events (those use POST endpoints)
- Event watcher is fully optional; the mod works without it
