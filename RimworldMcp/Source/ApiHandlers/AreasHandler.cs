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
    /// Area restrictions and zone assignment.
    ///
    /// GET  /api/areas              — list all allowed areas
    /// GET  /api/areas/pawns        — list pawns with their assigned area
    /// POST /api/areas/assign       — assign a pawn to an area
    /// POST /api/areas/unassign     — remove area restriction from a pawn
    /// </summary>
    public static class AreasHandler
    {
        public static string List(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            var list = new List<string>();
            var areas = Find.CurrentMap.areaManager.AllAreas;

            foreach (var area in areas)
            {
                var labelField = typeof(Area).GetField("label", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                string areaLabel = labelField?.GetValue(area) as string ?? "Unnamed";

                list.Add("{" +
                    "\"id\":" + area.ID + "," +
                    "\"label\":" + HttpServer.ToJsonString(areaLabel) + "," +
                    "\"cellCount\":" + area.ActiveCells.Count() +
                    "}");
            }

            return HttpServer.JsonSuccess(HttpServer.BuildJsonArray(list.ToArray()));
        }

        public static string Pawns(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            var list = new List<string>();
            var colonists = GameBridge.GetAllColonists();

            foreach (var pawn in colonists)
            {
                var settings = pawn.playerSettings;
                int areaId = -1;
                string areaLabel = "(No restriction)";

                if (settings != null)
                {
                    // Reflection-safe access to areaRestriction field (RimWorld 1.6 compat)
                    var areaField = typeof(Pawn_PlayerSettings).GetField("areaRestriction",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                    if (areaField != null)
                    {
                        var area = areaField.GetValue(settings) as Area;
                        if (area != null)
                        {
                            areaId = area.ID;
                            var labelField = typeof(Area).GetField("label", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                            areaLabel = labelField?.GetValue(area) as string ?? "Area";
                        }
                    }
                }

                list.Add("{" +
                    "\"id\":" + pawn.thingIDNumber + "," +
                    "\"name\":" + HttpServer.ToJsonString(pawn.Name?.ToStringShort ?? pawn.LabelCap) + "," +
                    "\"areaId\":" + areaId + "," +
                    "\"areaLabel\":" + HttpServer.ToJsonString(areaLabel) +
                    "}");
            }

            return HttpServer.JsonSuccess(HttpServer.BuildJsonArray(list.ToArray()));
        }

        public static string Assign(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            string body = HttpServer.ReadBody(req);
            var data = ParseSimpleJson(body);
            string pawnId = GetValue(data, "pawn");
            string areaIdStr = GetValue(data, "areaId");

            if (pawnId == null || areaIdStr == null)
                return HttpServer.JsonError("Missing fields: pawn, areaId");

            if (!int.TryParse(pawnId, out int pId) || !int.TryParse(areaIdStr, out int aId))
                return HttpServer.JsonError("pawn and areaId must be integers");

            var pawn = FindPawn(pId);
            if (pawn == null)
                return HttpServer.JsonError($"Colonist not found: {pawnId}");

            if (pawn.playerSettings == null)
                return HttpServer.JsonError($"{pawn.Name?.ToStringShort} has no player settings");

            var area = Find.CurrentMap.areaManager.AllAreas.FirstOrDefault(a => a.ID == aId);
            if (area == null)
                return HttpServer.JsonError($"Area with id {areaIdStr} not found");

            // Set area via reflection for 1.6 compat
            var areaField = typeof(Pawn_PlayerSettings).GetField("areaRestriction",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            if (areaField == null)
                return HttpServer.JsonError("Cannot set area restriction on this game version");

            areaField.SetValue(pawn.playerSettings, area);

            var labelField = typeof(Area).GetField("label", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            string areaLabel = labelField?.GetValue(area) as string ?? "Area";

            return HttpServer.JsonSuccess($"{{\"message\":\"{pawn.Name?.ToStringShort} assigned to {areaLabel}\"}}");
        }

        public static string Unassign(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            string body = HttpServer.ReadBody(req);
            var data = ParseSimpleJson(body);
            string pawnId = GetValue(data, "pawn");

            if (pawnId == null)
                return HttpServer.JsonError("Missing field: pawn");

            if (!int.TryParse(pawnId, out int pId))
                return HttpServer.JsonError("pawn must be integer");

            var pawn = FindPawn(pId);
            if (pawn == null)
                return HttpServer.JsonError($"Colonist not found: {pawnId}");

            if (pawn.playerSettings == null)
                return HttpServer.JsonError($"{pawn.Name?.ToStringShort} has no player settings");

            var areaField = typeof(Pawn_PlayerSettings).GetField("areaRestriction",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            if (areaField == null)
                return HttpServer.JsonError("Cannot unset area restriction on this game version");

            areaField.SetValue(pawn.playerSettings, null);

            return HttpServer.JsonSuccess($"{{\"message\":\"{pawn.Name?.ToStringShort} area restriction removed\"}}");
        }

        private static Pawn FindPawn(int id)
        {
            foreach (var pawn in Find.CurrentMap.mapPawns.FreeColonists)
                if (pawn.thingIDNumber == id) return pawn;
            return null;
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
