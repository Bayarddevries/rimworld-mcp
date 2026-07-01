using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using RimWorld;
using Verse;

namespace RimworldMcp
{
    public static class SpawnHandler
    {
        public static string SpawnThing(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            string body = HttpServer.ReadBody(req);
            var data = ParseSimpleJson(body);

            string thingName = GetValue(data, "thing");
            string countStr = GetValue(data, "count") ?? "1";
            string xStr = GetValue(data, "x");
            string zStr = GetValue(data, "z");

            if (thingName == null)
                return HttpServer.JsonError("Missing field: thing");

            if (!int.TryParse(countStr, out int count) || count < 1)
                count = 1;

            // Find the ThingDef
            ThingDef thingDef = DefDatabase<ThingDef>.AllDefsListForReading
                .FirstOrDefault(d =>
                    d.label.ToLower() == thingName.ToLower() ||
                    d.defName.ToLower() == thingName.ToLower() ||
                    d.label.ToLower().Contains(thingName.ToLower()));

            if (thingDef == null)
                return HttpServer.JsonError($"Thing not found: {thingName}. Try searching by partial name.");

            // Determine position
            IntVec3 pos;
            if (xStr != null && zStr != null && int.TryParse(xStr, out int x) && int.TryParse(zStr, out int z))
            {
                pos = new IntVec3(x, 0, z);
                if (!pos.InBounds(Find.CurrentMap))
                    return HttpServer.JsonError($"Position ({x}, {z}) is out of map bounds");
            }
            else
            {
                pos = DropCellFinder.TradeDropSpot(Find.CurrentMap);
            }

            // Make the thing(s)
            var thing = ThingMaker.MakeThing(thingDef);
            thing.stackCount = count;

            GenPlace.TryPlaceThing(thing, pos, Find.CurrentMap, ThingPlaceMode.Near);

            return HttpServer.JsonSuccess($"{{\"message\":\"Spawned {count}x {thingDef.label} at ({pos.x}, {pos.z})\"}}");
        }

        public static string SpawnPawn(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            string body = HttpServer.ReadBody(req);
            var data = ParseSimpleJson(body);

            string kind = GetValue(data, "kind") ?? "colonist";
            string factionName = GetValue(data, "faction");

            // Determine pawn kind
            PawnKindDef kindDef = null;

            if (kind.ToLower() == "colonist" || kind.ToLower() == "wildman")
            {
                kindDef = PawnKindDefOf.Colonist;
            }
            else
            {
                kindDef = DefDatabase<PawnKindDef>.AllDefsListForReading
                    .FirstOrDefault(k => k.defName.ToLower().Contains(kind.ToLower()) ||
                                         k.label?.ToLower().Contains(kind.ToLower()) == true);
            }

            if (kindDef == null)
                return HttpServer.JsonError($"Pawn kind not found: {kind}");

            // Determine faction
            Faction faction;
            if (factionName != null)
            {
                faction = Find.FactionManager.AllFactions
                    .FirstOrDefault(f => f.Name.ToLower().Contains(factionName.ToLower()) ||
                                         f.def.defName.ToLower().Contains(factionName.ToLower()));
                if (faction == null)
                    return HttpServer.JsonError($"Faction not found: {factionName}");
            }
            else
            {
                faction = Faction.OfPlayer;
            }

            // Generate and spawn pawn
            var request = new PawnGenerationRequest(kindDef, faction,
                canGeneratePawnRelations: true,
                colonistRelationChanceFactor: 1.0f,
                forceGenerateNewPawn: true);

            Pawn pawn = PawnGenerator.GeneratePawn(request);

            IntVec3 spawnPos = DropCellFinder.TradeDropSpot(Find.CurrentMap);
            GenSpawn.Spawn(pawn, spawnPos, Find.CurrentMap);

            // Add to colony if player faction
            if (faction == Faction.OfPlayer)
            {
                pawn.SetFaction(Faction.OfPlayer);
            }

            return HttpServer.JsonSuccess($"{{\"message\":\"Spawned {pawn.LabelCap} at ({spawnPos.x}, {spawnPos.z})\",\"pawn_name\":{HttpServer.ToJsonString(pawn.LabelCap)},\"pawn_id\":{pawn.thingIDNumber}}}");
        }

        // -- Helpers --
        private static Dictionary<string, string> ParseSimpleJson(string json)
        {
            var dict = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(json)) return dict;
            json = json.Trim();
            if (json.StartsWith("{")) json = json.Substring(1);
            if (json.EndsWith("}")) json = json.Substring(0, json.Length - 1);
            bool inString = false, escape = false;
            string currentKey = null;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (escape) { sb.Append(c); escape = false; continue; }
                if (c == '\\') { escape = true; continue; }
                if (c == '"') { inString = !inString; continue; }
                if (!inString)
                {
                    if (c == ':' && currentKey == null)
                    {
                        currentKey = sb.ToString().Trim().Trim('"');
                        sb.Clear(); continue;
                    }
                    if (c == ',' || c == '}')
                    {
                        if (currentKey != null) { dict[currentKey] = sb.ToString().Trim().Trim('"'); currentKey = null; sb.Clear(); }
                        continue;
                    }
                    if (c == '{' || c == '}' || c == '[' || c == ']' || char.IsWhiteSpace(c)) continue;
                }
                sb.Append(c);
            }
            if (currentKey != null) dict[currentKey] = sb.ToString().Trim().Trim('"');
            return dict;
        }

        private static string GetValue(Dictionary<string, string> dict, string key)
        {
            dict.TryGetValue(key, out var val);
            return val;
        }
    }
}
