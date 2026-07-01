"""MCP server for RimWorld colony control.

Connects to the RimWorld MCP Bridge mod (running inside the game) via HTTP
and exposes MCP tools for Hermes to read and control the colony.
"""

import json
from typing import Any

import mcp.server.stdio
from mcp import types
from mcp.server import NotificationOptions, Server
from mcp.server.models import InitializationOptions

from .rimworld_client import RimworldClient

server = Server("rimworld-mcp")
client = RimworldClient()


# ── Connection / Health ──────────────────────────────────────────────


@server.list_tools()
async def list_tools() -> list[types.Tool]:
    return [
        # Colony overview
        types.Tool(
            name="rimworld_colony_overview",
            description="Get an overview of the colony: colonist counts, weather, season, biome, wealth, storyteller info.",
            inputSchema={"type": "object", "properties": {}},
        ),
        types.Tool(
            name="rimworld_resources",
            description="List all stockpiled resources in the colony with counts.",
            inputSchema={"type": "object", "properties": {}},
        ),

        # Pawns
        types.Tool(
            name="rimworld_list_pawns",
            description="List all colonists with their skills, traits, health, mood, equipment, and needs.",
            inputSchema={"type": "object", "properties": {}},
        ),
        types.Tool(
            name="rimworld_get_pawn",
            description="Get detailed info about a specific colonist by name or ID.",
            inputSchema={
                "type": "object",
                "properties": {
                    "name": {
                        "type": "string",
                        "description": "Colonist name (partial match ok) or numeric pawn ID",
                    }
                },
                "required": ["name"],
            },
        ),
        types.Tool(
            name="rimworld_set_skill",
            description="Set a colonist's skill to a specific level (0-20).",
            inputSchema={
                "type": "object",
                "properties": {
                    "pawn": {"type": "string", "description": "Pawn name or ID"},
                    "skill": {"type": "string", "description": "Skill name (e.g. 'Shooting', 'Construction', 'Cooking')"},
                    "level": {"type": "integer", "description": "New skill level (0-20)", "minimum": 0, "maximum": 20},
                },
                "required": ["pawn", "skill", "level"],
            },
        ),
        types.Tool(
            name="rimworld_add_trait",
            description="Add a trait to a colonist.",
            inputSchema={
                "type": "object",
                "properties": {
                    "pawn": {"type": "string", "description": "Pawn name or ID"},
                    "trait": {"type": "string", "description": "Trait name (e.g. 'Industrious', 'Brawler', 'Fast Walker')"},
                    "degree": {"type": "integer", "description": "Trait degree (usually 0, sometimes 1 or -1)", "default": 0},
                },
                "required": ["pawn", "trait"],
            },
        ),
        types.Tool(
            name="rimworld_heal_pawn",
            description="Heal a colonist's injuries. Heals everything by default, or a specific body part.",
            inputSchema={
                "type": "object",
                "properties": {
                    "pawn": {"type": "string", "description": "Pawn name or ID"},
                    "body_part": {"type": "string", "description": "Optional: specific body part to heal (e.g. 'left leg', 'eye')"},
                },
                "required": ["pawn"],
            },
        ),
        types.Tool(
            name="rimworld_set_need",
            description="Set a colonist's need level (food, rest, mood, etc.). Value 0.0 to 1.0.",
            inputSchema={
                "type": "object",
                "properties": {
                    "pawn": {"type": "string", "description": "Pawn name or ID"},
                    "need": {"type": "string", "description": "Need name (e.g. 'Food', 'Rest', 'Mood', 'Recreation')"},
                    "value": {"type": "number", "description": "Value 0.0 (empty) to 1.0 (full)", "minimum": 0, "maximum": 1},
                },
                "required": ["pawn", "need", "value"],
            },
        ),
        types.Tool(
            name="rimworld_equip_gear",
            description="Equip an item (weapon or apparel) on a colonist.",
            inputSchema={
                "type": "object",
                "properties": {
                    "pawn": {"type": "string", "description": "Pawn name or ID"},
                    "item": {"type": "string", "description": "Item name to equip (partial match ok)"},
                    "slot": {"type": "string", "description": "Equipment slot: 'weapon', 'apparel', or 'inventory'", "default": "weapon"},
                },
                "required": ["pawn", "item"],
            },
        ),
        types.Tool(
            name="rimworld_inspire_pawn",
            description="Give a colonist an inspiration (or random one if not specified).",
            inputSchema={
                "type": "object",
                "properties": {
                    "pawn": {"type": "string", "description": "Pawn name or ID"},
                    "inspiration": {"type": "string", "description": "Optional: inspiration name (e.g. 'Inspired Surgery', 'Frenzy')"},
                },
                "required": ["pawn"],
            },
        ),
        types.Tool(
            name="rimworld_draft_pawn",
            description="Draft or undraft a colonist.",
            inputSchema={
                "type": "object",
                "properties": {
                    "pawn": {"type": "string", "description": "Pawn name or ID"},
                    "draft": {"type": "boolean", "description": "True to draft, false to undraft/release"},
                },
                "required": ["pawn", "draft"],
            },
        ),

        # Spawning
        types.Tool(
            name="rimworld_spawn_item",
            description="Spawn items or resources near the colony center.",
            inputSchema={
                "type": "object",
                "properties": {
                    "thing": {"type": "string", "description": "Item name (e.g. 'steel', 'food', 'medicine', 'component')"},
                    "count": {"type": "integer", "description": "How many to spawn", "default": 1, "minimum": 1},
                    "x": {"type": "integer", "description": "Optional X map coordinate"},
                    "z": {"type": "integer", "description": "Optional Z map coordinate"},
                },
                "required": ["thing"],
            },
        ),
        types.Tool(
            name="rimworld_spawn_colonist",
            description="Spawn a new colonist (randomly generated).",
            inputSchema={
                "type": "object",
                "properties": {
                    "kind": {"type": "string", "description": "Pawn kind (default: 'colonist')"},
                    "faction": {"type": "string", "description": "Faction name (optional, default: player)"},
                },
            },
        ),

        # Events
        types.Tool(
            name="rimworld_trigger_event",
            description="Trigger a story event. Common events: raid, manhunters, traders, mechanoids, infestation, eclipse, wanderer, blight, heat_wave, cold_snap, toxic_fallout, flashstorm, visitors, refugee.",
            inputSchema={
                "type": "object",
                "properties": {
                    "event": {"type": "string", "description": "Event name (e.g. 'raid', 'traders', 'infestation', 'eclipse')"},
                    "points": {
                        "type": "number",
                        "description": "Threat points for the event (higher = harder). Default 500.",
                        "default": 500,
                    },
                },
                "required": ["event"],
            },
        ),

        # Research
        types.Tool(
            name="rimworld_list_research",
            description="List all research projects with their status (completed/in-progress/available).",
            inputSchema={"type": "object", "properties": {}},
        ),
        types.Tool(
            name="rimworld_complete_research",
            description="Instantly complete a research project.",
            inputSchema={
                "type": "object",
                "properties": {
                    "tech": {"type": "string", "description": "Technology name or defName"},
                },
                "required": ["tech"],
            },
        ),

        # Bills
        types.Tool(
            name="rimworld_list_bills",
            description="List all production bills currently set at work tables.",
            inputSchema={"type": "object", "properties": {}},
        ),
        types.Tool(
            name="rimworld_add_bill",
            description="Add a production bill to a work table.",
            inputSchema={
                "type": "object",
                "properties": {
                    "recipe": {"type": "string", "description": "Recipe name (e.g. 'Make components', 'Smelt steel', 'Butcher')"},
                    "building": {"type": "string", "description": "Work table name (optional, auto-finds if omitted)"},
                },
                "required": ["recipe"],
            },
        ),

        # Save & Map
        types.Tool(
            name="rimworld_save",
            description="Save the game and create a backup snapshot.",
            inputSchema={
                "type": "object",
                "properties": {
                    "slot": {"type": "string", "description": "Save slot name (optional, default: 'mcp_autosave')"},
                },
            },
        ),
        types.Tool(
            name="rimworld_map_info",
            description="Get current map info: biome, weather, temperature, season, time of day.",
            inputSchema={"type": "object", "properties": {}},
        ),
        types.Tool(
            name="rimworld_status",
            description="Check if the RimWorld bridge is connected and healthy.",
            inputSchema={"type": "object", "properties": {}},
        ),

        # Chat
        types.Tool(
            name="rimworld_check_messages",
            description="Check if the player has sent any messages from the in-game chat window. Returns pending messages and clears the queue.",
            inputSchema={"type": "object", "properties": {}},
        ),
        types.Tool(
            name="rimworld_chat_respond",
            description="Send a response back to the player's in-game chat window.",
            inputSchema={
                "type": "object",
                "properties": {
                    "text": {"type": "string", "description": "The response text to show in the player's chat window"},
                },
                "required": ["text"],
            },
        ),

        # Events
        types.Tool(
            name="rimworld_check_events",
            description="Check for new game events since last check (raids, deaths, research, notifications, etc.). Returns and clears the pending event queue.",
            inputSchema={"type": "object", "properties": {}},
        ),
        types.Tool(
            name="rimworld_event_history",
            description="Get the full event history for this session.",
            inputSchema={"type": "object", "properties": {}},
        ),

        # Goals
        types.Tool(
            name="rimworld_list_goals",
            description="List all active goals and their progress.",
            inputSchema={"type": "object", "properties": {}},
        ),
        types.Tool(
            name="rimworld_set_goal",
            description="Set a colony goal. Hermes can track progress and notify when completed. Types: 'resource', 'research', 'colonists', 'wealth'.",
            inputSchema={
                "type": "object",
                "properties": {
                    "id": {"type": "string", "description": "Unique goal identifier"},
                    "description": {"type": "string", "description": "Human-readable description"},
                    "type": {"type": "string", "description": "Goal type: resource, research, colonists, or wealth"},
                    "target": {"type": "number", "description": "Target value to reach"},
                    "target_item": {"type": "string", "description": "For resource goals: the item name (e.g. 'steel', 'silver')"},
                },
                "required": ["id", "description", "type", "target"],
            },
        ),
        types.Tool(
            name="rimworld_remove_goal",
            description="Remove a goal by ID.",
            inputSchema={
                "type": "object",
                "properties": {
                    "id": {"type": "string", "description": "Goal ID to remove"},
                },
                "required": ["id"],
            },
        ),

        # Batch
        types.Tool(
            name="rimworld_batch",
            description="Execute multiple API calls in one round-trip. Accepts an array of calls, each with method, path, and body.",
            inputSchema={
                "type": "object",
                "properties": {
                    "calls": {
                        "type": "array",
                        "description": "Array of API calls to execute sequentially",
                        "items": {
                            "type": "object",
                            "properties": {
                                "method": {"type": "string", "description": "GET or POST"},
                                "path": {"type": "string", "description": "API path e.g. /api/spawn/thing"},
                                "body": {"type": "object", "description": "JSON body for POST calls"},
                            },
                        },
                    },
                },
                "required": ["calls"],
            },
        ),
    ]


# ── Tool handlers ────────────────────────────────────────────────────


def _call(method: str, path: str, body: dict | None = None) -> list[types.TextContent]:
    """Helper to make API calls and return MCP text content."""
    try:
        if method == "GET":
            result = client.get(path)
        else:
            result = client.post(path, body or {})
        return [types.TextContent(type="text", text=json.dumps(result, indent=2))]
    except Exception as e:
        return [types.TextContent(type="text", text=f"Error: {e!s}")]


@server.call_tool()
async def call_tool(name: str, arguments: dict) -> list[types.TextContent]:
    try:
        match name:
            # Colony
            case "rimworld_colony_overview":
                return _call("GET", "colony/overview")
            case "rimworld_resources":
                return _call("GET", "colony/resources")

            # Pawns
            case "rimworld_list_pawns":
                return _call("GET", "pawns")
            case "rimworld_get_pawn":
                return _call("GET", f"pawns/{arguments['name']}")
            case "rimworld_set_skill":
                return _call("POST", "pawns/skill", arguments)
            case "rimworld_add_trait":
                return _call("POST", "pawns/trait", arguments)
            case "rimworld_heal_pawn":
                body = {"pawn": arguments["pawn"], "action": "heal"}
                if "body_part" in arguments and arguments["body_part"]:
                    body["body_part"] = arguments["body_part"]
                return _call("POST", "pawns/health", body)
            case "rimworld_set_need":
                return _call("POST", "pawns/needs", arguments)
            case "rimworld_equip_gear":
                return _call("POST", "pawns/gear", arguments)
            case "rimworld_inspire_pawn":
                return _call("POST", "pawns/inspire", arguments)
            case "rimworld_draft_pawn":
                return _call("POST", "colony/command", {
                    "command": "draft" if arguments.get("draft") else "undraft",
                    "target": arguments["pawn"],
                })

            # Spawning
            case "rimworld_spawn_item":
                return _call("POST", "spawn/thing", arguments)
            case "rimworld_spawn_colonist":
                return _call("POST", "spawn/pawn", arguments)

            # Events
            case "rimworld_trigger_event":
                return _call("POST", "events/trigger", arguments)

            # Research
            case "rimworld_list_research":
                return _call("GET", "research")
            case "rimworld_complete_research":
                return _call("POST", "research/unlock", {**arguments, "action": "unlock"})

            # Bills
            case "rimworld_list_bills":
                return _call("GET", "map/bills")
            case "rimworld_add_bill":
                return _call("POST", "map/bills/add", arguments)

            # Save & Map
            case "rimworld_save":
                return _call("POST", "save", arguments)
            case "rimworld_map_info":
                return _call("GET", "map")
            case "rimworld_status":
                return _call("GET", "health")

            # Chat
            case "rimworld_check_messages":
                return _call("GET", "chat/pending")
            case "rimworld_chat_respond":
                return _call("POST", "chat/respond", arguments)

            # Events
            case "rimworld_check_events":
                return _call("GET", "events/feed")
            case "rimworld_event_history":
                return _call("GET", "events/history")

            # Goals
            case "rimworld_list_goals":
                return _call("GET", "goals")
            case "rimworld_set_goal":
                return _call("POST", "goals/set", arguments)
            case "rimworld_remove_goal":
                return _call("POST", "goals/remove", arguments)

            # Batch
            case "rimworld_batch":
                return _call("POST", "batch", arguments)

            case _:
                return [types.TextContent(type="text", text=f"Unknown tool: {name}")]

    except Exception as e:
        return [types.TextContent(type="text", text=f"Error calling {name}: {e!s}")]


# ── Run ──────────────────────────────────────────────────────────────


async def main():
    async with mcp.server.stdio.stdio_server() as (read_stream, write_stream):
        await server.run(
            read_stream,
            write_stream,
            InitializationOptions(
                server_name="rimworld-mcp",
                server_version="1.0.0",
                capabilities=server.get_capabilities(
                    notification_options=NotificationOptions(),
                    experimental_capabilities={},
                ),
            ),
        )


def main_sync():
    """Synchronous entry point for script-based launch."""
    import asyncio
    asyncio.run(main())
