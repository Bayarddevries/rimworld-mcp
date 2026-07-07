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
    /// Colonist schedule management.
    ///
    /// GET  /api/schedule       — get all colonists' timetables
    /// POST /api/schedule       — set a time block for a pawn
    /// </summary>
    public static class ScheduleHandler
    {
        public static string Get(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            var list = new List<string>();
            var colonists = GameBridge.GetAllColonists();

            foreach (var pawn in colonists)
            {
                var tt = pawn.timetable;
                if (tt == null) continue;

                // Build 24-hour slot array (0=Sleep, 1=Work, 2=Joy, 3=Anything)
                int[] hours = new int[24];
                var hoursStr = new StringBuilder();
                for (int h = 0; h < 24; h++)
                {
                    var assignment = tt.GetAssignment(h);
                    int val = 3; // default Anything
                    if (assignment == TimeAssignmentDefOf.Sleep) val = 0;
                    else if (assignment == TimeAssignmentDefOf.Work) val = 1;
                    else if (assignment == TimeAssignmentDefOf.Joy) val = 2;
                    hours[h] = val;
                    hoursStr.Append(val);
                }

                var areas = new List<string>();

                list.Add("{" +
                    "\"id\":" + pawn.thingIDNumber + "," +
                    "\"name\":" + HttpServer.ToJsonString(pawn.Name?.ToStringShort ?? pawn.LabelCap) + "," +
                    "\"hours\":" + HttpServer.ToJsonString(hoursStr.ToString()) + "," +
                    "\"areas\":" + (areas.Count > 0 ? "[" + string.Join(",", areas) + "]" : "[]") +
                    "}");
            }

            return HttpServer.JsonSuccess("[" + string.Join(",", list) + "]");
        }

        public static string Set(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            string body = HttpServer.ReadBody(req);
            var data = ParseSimpleJson(body);
            string pawnId = GetValue(data, "pawn");
            string hourStr = GetValue(data, "hour");
            string activity = GetValue(data, "activity"); // sleep, work, joy, anything

            if (pawnId == null || hourStr == null || activity == null)
                return HttpServer.JsonError("Missing fields: pawn, hour, activity");

            if (!int.TryParse(pawnId, out int id))
                return HttpServer.JsonError("pawn must be numeric ID");

            if (!int.TryParse(hourStr, out int hour) || hour < 0 || hour > 23)
                return HttpServer.JsonError("hour must be 0-23");

            var pawn = FindPawnById(id);
            if (pawn == null)
                return HttpServer.JsonError($"Colonist not found: {pawnId}");

            var tt = pawn.timetable;
            if (tt == null)
                return HttpServer.JsonError($"{pawn.Name?.ToStringShort} has no timetable");

            TimeAssignmentDef assignment;
            switch (activity.ToLower())
            {
                case "sleep": assignment = TimeAssignmentDefOf.Sleep; break;
                case "work": assignment = TimeAssignmentDefOf.Work; break;
                case "joy": assignment = TimeAssignmentDefOf.Joy; break;
                case "anything":
                case "any": assignment = TimeAssignmentDefOf.Anything; break;
                default: return HttpServer.JsonError($"Invalid activity: {activity}. Use: sleep, work, joy, anything");
            }

            tt.times[hour] = assignment;

            return HttpServer.JsonSuccess($"{{\"message\":\"{pawn.Name?.ToStringShort} hour {hour} set to {activity}\"}}");
        }

        public static string SetAllHours(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            string body = HttpServer.ReadBody(req);
            var data = ParseSimpleJson(body);
            string pawnId = GetValue(data, "pawn");
            string hoursStr = GetValue(data, "hours"); // 24-char string like "000111333..."

            if (pawnId == null || hoursStr == null)
                return HttpServer.JsonError("Missing fields: pawn, hours");

            if (!int.TryParse(pawnId, out int id))
                return HttpServer.JsonError("pawn must be numeric ID");

            if (hoursStr.Length != 24)
                return HttpServer.JsonError("hours must be exactly 24 characters (0=Sleep, 1=Work, 2=Joy, 3=Anything)");

            var pawn = FindPawnById(id);
            if (pawn == null)
                return HttpServer.JsonError($"Colonist not found: {pawnId}");

            var tt = pawn.timetable;
            if (tt == null)
                return HttpServer.JsonError($"{pawn.Name?.ToStringShort} has no timetable");

            var lookup = new Dictionary<char, TimeAssignmentDef>
            {
                {'0', TimeAssignmentDefOf.Sleep},
                {'1', TimeAssignmentDefOf.Work},
                {'2', TimeAssignmentDefOf.Joy},
                {'3', TimeAssignmentDefOf.Anything}
            };

            for (int h = 0; h < 24; h++)
            {
                if (lookup.TryGetValue(hoursStr[h], out var def))
                    tt.times[h] = def;
            }

            return HttpServer.JsonSuccess($"{{\"message\":\"{pawn.Name?.ToStringShort} schedule updated\"}}");
        }

        // ─── Helpers ───

        private static Pawn FindPawnById(int id)
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
