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
    /// Architect / construction management.
    ///
    /// GET  /api/architect/structures    — list buildable structures
    /// GET  /api/architect/blueprints    — list current blueprints/frames
    /// POST /api/architect/build         — place a blueprint
    /// POST /api/architect/deconstruct   — mark for deconstruction
    /// POST /api/architect/cancel        — cancel a blueprint
    /// POST /api/architect/mine          — mark for mining
    /// </summary>
    public static class ArchitectHandler
    {
        public static string Structures(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            var list = new List<string>();
            var allDefs = DefDatabase<ThingDef>.AllDefsListForReading
                .Where(d => d.category == ThingCategory.Building
                         && d.blueprintDef != null
                         && d.designationCategory != null)
                .OrderBy(d => (d.designationCategory?.label ?? "ZZZ") + (d.label ?? "")).ToList();

            // Group by architect tab
            var groups = new Dictionary<string, List<ThingDef>>();
            foreach (var def in allDefs)
            {
                string cat = def.designationCategory?.label ?? "Other";
                if (!groups.ContainsKey(cat))
                    groups[cat] = new List<ThingDef>();
                groups[cat].Add(def);
            }

            foreach (var kvp in groups)
            {
                foreach (var def in kvp.Value)
                {
                    list.Add("{" +
                        "\"defName\":" + HttpServer.ToJsonString(def.defName) + "," +
                        "\"label\":" + HttpServer.ToJsonString(def.label ?? def.defName) + "," +
                        "\"category\":" + HttpServer.ToJsonString(kvp.Key) + "," +
                        "\"cost\":" + (def.CostList?.Count > 0 ? def.CostList.Sum(c => c.count).ToString() : "0") + "," +
                        "\"description\":" + HttpServer.ToJsonString(def.description?.Truncate(150) ?? "") +
                        "}");
                }
            }

            return HttpServer.JsonSuccess(HttpServer.BuildJsonArray(list.ToArray()));
        }

        public static string Blueprints(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            var list = new List<string>();
            var map = Find.CurrentMap;

            foreach (var thing in map.spawnedThings)
            {
                if (thing is Blueprint || thing is Frame)
                {
                    float progress = 0;
                    string status = thing is Blueprint ? "blueprint" : "frame";
                    if (thing is Frame frame)
                        progress = (float)(frame.workDone) / Math.Max((float)(frame.def?.GetStatValueAbstract(StatDefOf.WorkToBuild) ?? 100f), 1f);

                    list.Add("{" +
                        "\"id\":" + thing.thingIDNumber + "," +
                        "\"label\":" + HttpServer.ToJsonString(thing.LabelCap) + "," +
                        "\"defName\":" + HttpServer.ToJsonString(thing.def.defName) + "," +
                        "\"status\":" + HttpServer.ToJsonString(status) + "," +
                        "\"progress\":" + progress.ToString("F2") + "," +
                        "\"x\":" + thing.Position.x + "," +
                        "\"z\":" + thing.Position.z +
                        "}");
                }
            }

            return HttpServer.JsonSuccess(HttpServer.BuildJsonArray(list.ToArray()));
        }

        public static string Build(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            string body = HttpServer.ReadBody(req);
            var data = ParseSimpleJson(body);
            string structure = GetValue(data, "structure");  // defName
            string xStr = GetValue(data, "x");
            string zStr = GetValue(data, "z");

            if (structure == null || xStr == null || zStr == null)
                return HttpServer.JsonError("Missing fields: structure (defName), x, z");

            if (!int.TryParse(xStr, out int x) || !int.TryParse(zStr, out int z))
                return HttpServer.JsonError("x and z must be integers");

            var map = Find.CurrentMap;
            var cell = new IntVec3(x, 0, z);
            if (!cell.InBounds(map))
                return HttpServer.JsonError($"Position ({x},{z}) is out of bounds");

            // Find the ThingDef
            var thingDef = DefDatabase<ThingDef>.AllDefsListForReading
                .FirstOrDefault(d => d.defName.Equals(structure, StringComparison.OrdinalIgnoreCase)
                                  || (d.label ?? "").ToLower().Contains(structure.ToLower()));

            if (thingDef == null)
                return HttpServer.JsonError($"Structure not found: {structure}");

            if (thingDef.blueprintDef == null)
                return HttpServer.JsonError($"{thingDef.label} has no blueprint — may not be buildable");

            // Check if cell is clear
            var thingsAtCell = map.thingGrid.ThingsListAt(cell);
            bool occupied = thingsAtCell != null && thingsAtCell.Any(t => t.def != null);
            if (occupied)
                return HttpServer.JsonError($"Cell ({x},{z}) is occupied");

            // Place blueprint
            var blueprint = GenSpawn.Spawn(thingDef.blueprintDef, cell, map);
            blueprint.SetFaction(Faction.OfPlayer);

            return HttpServer.JsonSuccess($"{{\"message\":\"Blueprint for {thingDef.label} placed at ({x},{z})\",\"id\":{blueprint.thingIDNumber}}}");
        }

        public static string Deconstruct(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            string body = HttpServer.ReadBody(req);
            var data = ParseSimpleJson(body);
            string xStr = GetValue(data, "x");
            string zStr = GetValue(data, "z");
            string thingId = GetValue(data, "id");

            var map = Find.CurrentMap;

            Thing target = null;
            if (thingId != null && int.TryParse(thingId, out int id))
                target = map.spawnedThings.FirstOrDefault(t => t.thingIDNumber == id);

            if (target == null && xStr != null && zStr != null)
            {
                if (int.TryParse(xStr, out int x) && int.TryParse(zStr, out int z))
                {
                    var cell = new IntVec3(x, 0, z);
                    if (cell.InBounds(map))
                        target = map.thingGrid.ThingsListAt(cell)?.FirstOrDefault();
                }
            }

            if (target == null)
                return HttpServer.JsonError("No target found. Provide id or x/z coordinates.");

            var designator = new Designator_Deconstruct();
            designator.DesignateSingleCell(target.Position);

            return HttpServer.JsonSuccess($"{{\"message\":\"Marked {target.LabelCap} for deconstruction\"}}");
        }

        public static string Cancel(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            string body = HttpServer.ReadBody(req);
            var data = ParseSimpleJson(body);
            string thingId = GetValue(data, "id");

            if (thingId == null || !int.TryParse(thingId, out int id))
                return HttpServer.JsonError("Missing field: id (thing ID number)");

            var map = Find.CurrentMap;
            var target = map.spawnedThings.FirstOrDefault(t => t.thingIDNumber == id
                                                             && (t is Blueprint || t is Frame));

            if (target == null)
                return HttpServer.JsonError($"No blueprint/frame with id {id} found");

            var designator = new Designator_Cancel();
            designator.DesignateSingleCell(target.Position);

            return HttpServer.JsonSuccess($"{{\"message\":\"Cancelled {target.LabelCap}\"}}");
        }

        public static string Mine(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            string body = HttpServer.ReadBody(req);
            var data = ParseSimpleJson(body);
            string xStr = GetValue(data, "x");
            string zStr = GetValue(data, "z");

            if (xStr == null || zStr == null)
                return HttpServer.JsonError("Missing fields: x, z");

            if (!int.TryParse(xStr, out int x) || !int.TryParse(zStr, out int z))
                return HttpServer.JsonError("x and z must be integers");

            var map = Find.CurrentMap;
            var cell = new IntVec3(x, 0, z);
            if (!cell.InBounds(map))
                return HttpServer.JsonError($"Position ({x},{z}) is out of bounds");

            var designator = new Designator_Mine();
            designator.DesignateSingleCell(cell);

            return HttpServer.JsonSuccess($"{{\"message\":\"Marked cell ({x},{z}) for mining\"}}");
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
