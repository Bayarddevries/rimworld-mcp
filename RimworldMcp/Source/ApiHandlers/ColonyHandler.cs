using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using RimWorld;
using Verse;

namespace RimworldMcp
{
    public static class ColonyHandler
    {
        public static string Resources(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            var resourceCounts = new Dictionary<string, float>();
            foreach (var thing in Find.CurrentMap.spawnedThings)
            {
                if (thing.def.EverStorable(true) && thing.def.category == ThingCategory.Item)
                {
                    string key = thing.def.label;
                    if (!resourceCounts.ContainsKey(key))
                        resourceCounts[key] = 0;
                    resourceCounts[key] += thing.stackCount;
                }
            }

            var resources = new List<string>();
            foreach (var kvp in resourceCounts.OrderByDescending(k => k.Value).Take(50))
            {
                resources.Add(HttpServer.BuildJsonObject(
                    ("name", HttpServer.ToJsonString(kvp.Key)),
                    ("count", kvp.Value.ToString("F0"))
                ));
            }

            return HttpServer.JsonSuccess(HttpServer.BuildJsonArray(resources.ToArray()));
        }

        public static string Overview(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            var map = Find.CurrentMap;

            int colonists = map.mapPawns.FreeColonistsCount;
            int prisoners = map.mapPawns.PrisonersOfColonyCount;
            int animals = map.mapPawns.SpawnedColonyMechs?.Count() ?? 0;

            string weather = map.weatherManager?.curWeather?.label ?? "Unknown";
            string season = GenLocalDate.Season(map).ToString();
            int day = GenLocalDate.DayOfSeason(map);

            float wealthTotal = map.wealthWatcher.WealthTotal;
            float wealthItems = map.wealthWatcher.WealthItems;
            float wealthBuildings = map.wealthWatcher.WealthBuildings;

            string storyteller = Find.Storyteller?.def?.label ?? "Unknown";
            string difficulty = Find.Storyteller?.difficulty?.ToString() ?? "Unknown";

            return HttpServer.JsonSuccess(HttpServer.BuildJsonObject(
                ("colony_name", HttpServer.ToJsonString(Faction.OfPlayer?.Name ?? "Unknown")),
                ("colonists", colonists.ToString()),
                ("prisoners", prisoners.ToString()),
                ("animals", animals.ToString()),
                ("weather", HttpServer.ToJsonString(weather)),
                ("season", HttpServer.ToJsonString(season)),
                ("day_of_season", day.ToString()),
                ("biome", HttpServer.ToJsonString(map.Biome?.label ?? "Unknown")),
                ("storyteller", HttpServer.ToJsonString(storyteller)),
                ("difficulty", HttpServer.ToJsonString(difficulty)),
                ("wealth_total", $"{wealthTotal:F0}"),
                ("wealth_items", $"{wealthItems:F0}"),
                ("wealth_buildings", $"{wealthBuildings:F0}"),
                ("tick", Find.TickManager.TicksGame.ToString()),
                ("day_of_quadrum", GenLocalDate.DayOfQuadrum(map).ToString()),
                ("year", GenLocalDate.Year(map).ToString())
            ));
        }

        public static string AddResources(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            string body = HttpServer.ReadBody(req);
            var data = ParseSimpleJson(body);

            string itemName = GetValue(data, "item");
            string countStr = GetValue(data, "count");

            if (itemName == null || countStr == null)
                return HttpServer.JsonError("Missing fields: item, count");

            if (!int.TryParse(countStr, out int count) || count < 1)
                return HttpServer.JsonError("count must be a positive integer");

            ThingDef thingDef = DefDatabase<ThingDef>.AllDefsListForReading
                .FirstOrDefault(d => d.label.ToLower() == itemName.ToLower() ||
                                     d.defName.ToLower() == itemName.ToLower() ||
                                     d.label.ToLower().Contains(itemName.ToLower()));

            if (thingDef == null)
                return HttpServer.JsonError($"Item not found: {itemName}");

            if (!thingDef.EverStorable(true))
                return HttpServer.JsonError($"{thingDef.label} is not a storable item");

            var thing = ThingMaker.MakeThing(thingDef);
            thing.stackCount = count;

            IntVec3 dropPos = DropCellFinder.TradeDropSpot(Find.CurrentMap);
            GenPlace.TryPlaceThing(thing, dropPos, Find.CurrentMap, ThingPlaceMode.Near);

            return HttpServer.JsonSuccess($"{{\"message\":\"Added {count}x {thingDef.label} to colony\"}}");
        }

        public static string IssueCommand(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            string body = HttpServer.ReadBody(req);
            var data = ParseSimpleJson(body);

            string command = GetValue(data, "command");
            string target = GetValue(data, "target");

            if (command == null)
                return HttpServer.JsonError("Missing field: command");

            if (command.ToLower() == "draft")
            {
                var pawn = GameBridge.FindColonist(target);
                if (pawn == null) return HttpServer.JsonError($"Pawn not found: {target}");
                pawn.drafter.Drafted = true;
                return HttpServer.JsonSuccess($"{{\"message\":\"Drafted {pawn.LabelCap}\"}}");
            }

            if (command.ToLower() == "undraft" || command.ToLower() == "release")
            {
                var pawn = GameBridge.FindColonist(target);
                if (pawn == null) return HttpServer.JsonError($"Pawn not found: {target}");
                pawn.drafter.Drafted = false;
                return HttpServer.JsonSuccess($"{{\"message\":\"Released {pawn.LabelCap}\"}}");
            }

            return HttpServer.JsonError($"Unknown command: {command}. Use 'draft', 'undraft'.");
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
            var sb = new StringBuilder();
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
