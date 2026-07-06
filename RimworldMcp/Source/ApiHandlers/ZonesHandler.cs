using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using RimWorld;
using Verse;

namespace RimworldMcp
{
    /// <summary>
    /// Stockpile zone management with configurable settings.
    ///
    /// GET  /api/zones              — list zones
    /// GET  /api/zones/{id}         — zone detail with settings
    /// POST /api/zones/priority     — set zone storage priority
    /// POST /api/zones/filter       — apply a filter preset to a zone
    /// </summary>
    public static class ZonesHandler
    {
        public static string List(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            var zones = new List<string>();
            foreach (var zone in Find.CurrentMap.zoneManager.AllZones)
            {
                var sz = zone as Zone_Stockpile;
                if (sz == null) continue;

                zones.Add(SerializeZoneBrief(sz));
            }

            return HttpServer.JsonSuccess(HttpServer.BuildJsonArray(zones.ToArray()));
        }

        public static string Detail(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            string path = req.Url.AbsolutePath.ToLowerInvariant();
            string identifier = HttpServer.ExtractIdFromPath(path, "/api/zones/");

            var zone = Find.CurrentMap.zoneManager.AllZones
                .OfType<Zone_Stockpile>()
                .FirstOrDefault(z => z.GetUniqueLoadID().ToLower().Contains(identifier.ToLower()) ||
                                     (z.label ?? "").ToLower().Contains(identifier.ToLower()));

            if (zone == null)
                return HttpServer.JsonError($"Zone not found: {identifier}");

            return HttpServer.JsonSuccess(SerializeZoneFull(zone));
        }

        public static string SetPriority(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            string body = HttpServer.ReadBody(req);
            var data = ParseSimpleJson(body);
            string zoneId = GetValue(data, "zone");
            string priorityStr = GetValue(data, "priority");

            if (zoneId == null || priorityStr == null)
                return HttpServer.JsonError("Missing fields: zone, priority");

            var zone = FindZone(zoneId);
            if (zone == null)
                return HttpServer.JsonError($"Zone not found: {zoneId}");

            StoragePriority priority;
            if (int.TryParse(priorityStr, out int prioNum))
            {
                if (prioNum < 0 || prioNum > 3)
                    return HttpServer.JsonError("Priority must be 0 (Unimportant), 1 (Normal), 2 (Preferred), or 3 (Critical)");
                priority = (StoragePriority)prioNum;
            }
            else if (!Enum.TryParse(priorityStr, true, out priority))
            {
                return HttpServer.JsonError($"Invalid priority: {priorityStr}. Use 0-3 or Unimportant/Normal/Preferred/Critical");
            }

            zone.settings.Priority = priority;
            return HttpServer.JsonSuccess($"{{\"message\":\"Zone '{zone.label}' priority set to {priority}\",\"priority\":\"{priority}\"}}");
        }

        public static string SetFilter(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            string body = HttpServer.ReadBody(req);
            var data = ParseSimpleJson(body);
            string zoneId = GetValue(data, "zone");
            string filterPreset = GetValue(data, "filter");

            if (zoneId == null || filterPreset == null)
                return HttpServer.JsonError("Missing fields: zone, filter");

            var zone = FindZone(zoneId);
            if (zone == null)
                return HttpServer.JsonError($"Zone not found: {zoneId}");

            string presetName = filterPreset.ToLower();

            if (presetName == "everything" || presetName == "all")
            {
                // Allow all items — iterate each ThingDef
                var allDefs = DefDatabase<ThingDef>.AllDefsListForReading;
                foreach (var def in allDefs)
                {
                    if (def.category == ThingCategory.Item)
                        zone.settings.filter.SetAllow(def, true);
                }
                return HttpServer.JsonSuccess($"{{\"message\":\"Zone '{zone.label}' now accepts everything\"}}");
            }
            else if (presetName == "nothing" || presetName == "clear" || presetName == "empty")
            {
                var allDefs = DefDatabase<ThingDef>.AllDefsListForReading;
                foreach (var def in allDefs)
                {
                    if (def.category == ThingCategory.Item)
                        zone.settings.filter.SetAllow(def, false);
                }
                return HttpServer.JsonSuccess($"{{\"message\":\"Zone '{zone.label}' cleared — accepts nothing\"}}");
            }
            else
            {
                // Try matching a category
                var cat = DefDatabase<ThingCategoryDef>.AllDefsListForReading
                    .FirstOrDefault(c => c.label?.ToLower().Contains(presetName) == true ||
                                         c.defName.ToLower().Contains(presetName));
                if (cat != null && cat.DescendantThingDefs != null)
                {
                    // Clear all, then allow only this category's items
                    var allDefs = DefDatabase<ThingDef>.AllDefsListForReading;
                    foreach (var def in allDefs)
                        if (def.category == ThingCategory.Item)
                            zone.settings.filter.SetAllow(def, false);

                    foreach (var td in cat.DescendantThingDefs)
                        zone.settings.filter.SetAllow(td, true);

                    return HttpServer.JsonSuccess($"{{\"message\":\"Zone '{zone.label}' filter set to {cat.label}\"}}");
                }

                // Try matching individual item
                var thingDef = DefDatabase<ThingDef>.AllDefsListForReading
                    .FirstOrDefault(d => d.label?.ToLower().Contains(presetName) == true ||
                                         d.defName.ToLower().Contains(presetName));
                if (thingDef != null && thingDef.category == ThingCategory.Item)
                {
                    var allDefs = DefDatabase<ThingDef>.AllDefsListForReading;
                    foreach (var def in allDefs)
                        if (def.category == ThingCategory.Item)
                            zone.settings.filter.SetAllow(def, false);

                    zone.settings.filter.SetAllow(thingDef, true);
                    return HttpServer.JsonSuccess($"{{\"message\":\"Zone '{zone.label}' filter set to {thingDef.label}\"}}");
                }

                return HttpServer.JsonError($"Filter preset not found: {filterPreset}. Try: everything, nothing, food, manufactured, stone, metal, weapons, apparel, or a specific item name");
            }
        }

        // ─── Serialization ───

        private static string SerializeZoneBrief(Zone_Stockpile sz)
        {
            int cellCount = sz.Cells.Count;
            string label = sz.label ?? "Unnamed";
            string prio = sz.settings.Priority.ToString();

            return "{" +
                "\"id\":" + HttpServer.ToJsonString(sz.GetUniqueLoadID()) + "," +
                "\"label\":" + HttpServer.ToJsonString(label) + "," +
                "\"cells\":" + cellCount + "," +
                "\"priority\":" + HttpServer.ToJsonString(prio) + "," +
                "\"x\":" + sz.Position.x + "," +
                "\"z\":" + sz.Position.z +
                "}";
        }

        private static string SerializeZoneFull(Zone_Stockpile sz)
        {
            int cellCount = sz.Cells.Count;
            string label = sz.label ?? "Unnamed";
            string prio = sz.settings.Priority.ToString();
            int prioNum = (int)sz.settings.Priority;

            // Count items currently in the zone
            int itemCount = 0;
            var itemTypes = new HashSet<string>();
            var thingGrid = Find.CurrentMap.thingGrid;
            foreach (var cell in sz.Cells)
            {
                var things = thingGrid.ThingsAt(cell);
                if (things == null) continue;
                foreach (var t in things)
                {
                    if (t.def.category == ThingCategory.Item)
                    {
                        itemCount++;
                        itemTypes.Add(t.def.label);
                    }
                }
            }

            return "{" +
                "\"id\":" + HttpServer.ToJsonString(sz.GetUniqueLoadID()) + "," +
                "\"label\":" + HttpServer.ToJsonString(label) + "," +
                "\"cells\":" + cellCount + "," +
                "\"priority\":" + HttpServer.ToJsonString(prio) + "," +
                "\"priorityNum\":" + prioNum + "," +
                "\"x\":" + sz.Position.x + "," +
                "\"z\":" + sz.Position.z + "," +
                "\"itemCount\":" + itemCount + "," +
                "\"itemTypes\":" + HttpServer.ToJsonString(string.Join(", ", itemTypes.Take(10))) +
                "}";
        }

        private static Zone_Stockpile FindZone(string identifier)
        {
            string lower = identifier.ToLower();
            return Find.CurrentMap.zoneManager.AllZones
                .OfType<Zone_Stockpile>()
                .FirstOrDefault(z => z.GetUniqueLoadID().ToLower().Contains(lower) ||
                                     (z.label ?? "").ToLower().Contains(lower));
        }

        // ─── Helpers ───

        private static string GetValue(Dictionary<string, string> dict, string key)
        {
            dict.TryGetValue(key, out var val);
            return val;
        }

        private static Dictionary<string, string> ParseSimpleJson(string json)
        {
            var result = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(json)) return result;
            int i = 0;
            while (i < json.Length)
            {
                int keyStart = json.IndexOf('"', i);
                if (keyStart < 0) break;
                int keyEnd = json.IndexOf('"', keyStart + 1);
                if (keyEnd < 0) break;
                string key = json.Substring(keyStart + 1, keyEnd - keyStart - 1);
                int colon = json.IndexOf(':', keyEnd + 1);
                if (colon < 0) break;
                int valStart = colon + 1;
                while (valStart < json.Length && json[valStart] == ' ') valStart++;
                if (valStart >= json.Length) break;
                string val;
                if (json[valStart] == '"')
                {
                    int valEnd = json.IndexOf('"', valStart + 1);
                    if (valEnd < 0) break;
                    val = json.Substring(valStart + 1, valEnd - valStart - 1);
                    i = valEnd + 1;
                }
                else
                {
                    int valEnd = json.IndexOfAny(new[] { ',', '}' }, valStart);
                    if (valEnd < 0) valEnd = json.Length;
                    val = json.Substring(valStart, valEnd - valStart).Trim();
                    i = valEnd;
                }
                result[key] = val;
                if (json[i] == '}') break;
                i++;
            }
            return result;
        }
    }
}
