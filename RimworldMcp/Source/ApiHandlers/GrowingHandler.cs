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
    /// Growing zone management.
    ///
    /// GET  /api/growing            — list growing zones
    /// POST /api/growing/harvest    — force harvest a zone
    /// POST /api/growing/sow        — set plant to sow in a zone
    /// POST /api/growing/priority   — set sow priority
    /// </summary>
    public static class GrowingHandler
    {
        public static string List(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            var list = new List<string>();
            var map = Find.CurrentMap;

            foreach (var zone in map.zoneManager.AllZones)
            {
                var gz = zone as Zone_Growing;
                if (gz == null) continue;

                int cellCount = gz.Cells.Count;
                int growingCount = 0;
                int readyCount = 0;
                var plantTypes = new HashSet<string>();

                foreach (var cell in gz.Cells)
                {
                    var plant = cell.GetPlant(map);
                    if (plant != null && !plant.DestroyedOrNull())
                    {
                        growingCount++;
                        plantTypes.Add(plant.def.label);
                        if (plant.HarvestableNow)
                            readyCount++;
                    }
                }

                list.Add("{" +
                    "\"id\":" + HttpServer.ToJsonString(gz.GetUniqueLoadID()) + "," +
                    "\"label\":" + HttpServer.ToJsonString(gz.label ?? "Growing Zone") + "," +
                    "\"cells\":" + cellCount + "," +
                    "\"growing\":" + growingCount + "," +
                    "\"ready\":" + readyCount + "," +
                    "\"sowDef\":" + HttpServer.ToJsonString(GetSowDefName(gz)) + "," +
                    "\"allowSow\":" + (gz.allowSow ? "true" : "false") + "," +
                    "\"plantTypes\":" + HttpServer.ToJsonString(string.Join(", ", plantTypes)) +
                    "}");
            }

            return HttpServer.JsonSuccess(HttpServer.BuildJsonArray(list.ToArray()));
        }

        private static string GetSowDefName(Zone_Growing gz)
        {
            try
            {
                var field = typeof(Zone_Growing).GetField("plantDefToSow", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                if (field != null)
                {
                    var def = field.GetValue(gz) as ThingDef;
                    if (def != null) return def.label ?? def.defName;
                }
            }
            catch { }
            return "Any";
        }

        public static string Harvest(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            string body = HttpServer.ReadBody(req);
            var data = ParseSimpleJson(body);
            string zoneId = GetValue(data, "zone");

            if (zoneId == null)
                return HttpServer.JsonError("Missing field: zone");

            var gz = FindZone(zoneId);
            if (gz == null)
                return HttpServer.JsonError($"Growing zone not found: {zoneId}");

            var map = Find.CurrentMap;
            var colonists = GameBridge.GetAllColonists();
            if (colonists.Count == 0)
                return HttpServer.JsonError("No colonists available");

            int harvested = 0;
            foreach (var cell in gz.Cells)
            {
                var plant = cell.GetPlant(map);
                if (plant != null && plant.HarvestableNow && !plant.DestroyedOrNull())
                {
                    // Designate harvest on this plant
                    map.designationManager.AddDesignation(new Designation(plant, DesignationDefOf.HarvestPlant));
                    harvested++;
                }
            }

            return HttpServer.JsonSuccess($"{{\"message\":\"Marked {harvested} plants for harvest\"}}");
        }

        public static string Sow(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            string body = HttpServer.ReadBody(req);
            var data = ParseSimpleJson(body);
            string zoneId = GetValue(data, "zone");
            string plantDef = GetValue(data, "plant");

            if (zoneId == null || plantDef == null)
                return HttpServer.JsonError("Missing fields: zone, plant");

            var gz = FindZone(zoneId);
            if (gz == null)
                return HttpServer.JsonError($"Growing zone not found: {zoneId}");

            // Find the plant def
            var def = DefDatabase<ThingDef>.AllDefsListForReading
                .FirstOrDefault(d => d.plant != null &&
                    (d.defName.Equals(plantDef, StringComparison.OrdinalIgnoreCase) ||
                     d.defName.ToLower().Contains(plantDef.ToLower()) ||
                     (d.label ?? "").ToLower().Contains(plantDef.ToLower())));

            if (def == null)
            {
                // Try matching by category
                var cat = DefDatabase<ThingCategoryDef>.AllDefsListForReading
                    .FirstOrDefault(c => c.label?.ToLower().Contains(plantDef.ToLower()) == true);
                if (cat != null)
                {
                    var plants = DefDatabase<ThingDef>.AllDefsListForReading
                        .Where(d => d.plant != null && d.plant.sowTags != null)
                        .ToList();
                    if (plants.Count > 0)
                        def = plants.FirstOrDefault();
                }
            }

            if (def == null)
                return HttpServer.JsonError($"Plant not found: {plantDef}. Try: potato, rice, corn, cotton, smokeleaf, healroot");

            // Set via reflection for 1.6 compat
            var setField = typeof(Zone_Growing).GetField("plantDefToSow", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            if (setField != null)
                setField.SetValue(gz, def);
            gz.allowSow = true;

            return HttpServer.JsonSuccess($"{{\"message\":\"Zone now set to grow {def.label}\"}}");
        }

        public static string SetAllowSow(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            string body = HttpServer.ReadBody(req);
            var data = ParseSimpleJson(body);
            string zoneId = GetValue(data, "zone");
            string allowStr = GetValue(data, "allow");

            if (zoneId == null || allowStr == null)
                return HttpServer.JsonError("Missing fields: zone, allow");

            var gz = FindZone(zoneId);
            if (gz == null)
                return HttpServer.JsonError($"Growing zone not found: {zoneId}");

            bool allow = true;
            if (allowStr != null) bool.TryParse(allowStr, out allow);

            gz.allowSow = allow;

            return HttpServer.JsonSuccess($"{{\"message\":\"Zone sowing {(allow ? "enabled" : "disabled")}\"}}");
        }

        private static Zone_Growing FindZone(string identifier)
        {
            string lower = identifier.ToLower();
            return Find.CurrentMap.zoneManager.AllZones
                .OfType<Zone_Growing>()
                .FirstOrDefault(z => z.GetUniqueLoadID().ToLower().Contains(lower) ||
                                     (z.label ?? "").ToLower().Contains(lower));
        }

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
