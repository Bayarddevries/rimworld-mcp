using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using RimWorld;
using Verse;

namespace RimworldMcp
{
    public static class PrisonersHandler
    {
        public static string List(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            var prisoners = new List<string>();
            foreach (var pawn in Find.CurrentMap.mapPawns.AllPawns)
            {
                if (pawn.guest == null || !pawn.guest.IsPrisoner || pawn.guest.HostFaction != Faction.OfPlayer)
                    continue;

                float resist = pawn.guest.resistance;
                float startResist = Math.Max(resist, 0.01f);
                float pct = resist > 0.01f ? Math.Min(100f, (1f - resist / startResist) * 100f) : 100f;

                // Convert colonist works... won't work for prisoners, so use basic stats
                // Use a simple health estimate
                float healthPct = (float)pawn.health.summaryHealth.SummaryHealthPercent * 100f;
                int moodPct = (int)(pawn.needs?.mood?.CurLevelPercentage * 100f ?? 50f);

                prisoners.Add(HttpServer.BuildJsonObject(
                    ("id", HttpServer.ToJsonString(pawn.GetUniqueLoadID())),
                    ("name", HttpServer.ToJsonString(pawn.Name.ToStringShort)),
                    ("age", pawn.ageTracker?.AgeBiologicalYears.ToString() ?? "?"),
                    ("gender", HttpServer.ToJsonString(pawn.gender.ToString())),
                    ("health", healthPct.ToString("F0")),
                    ("mood", moodPct.ToString()),
                    ("resistance", pawn.guest?.resistance.ToString("F1") ?? "0"),
                    ("recruit_progress", pct.ToString("F0")),
                    ("kind", HttpServer.ToJsonString(pawn.kindDef?.label ?? "Prisoner"))
                ));
            }

            return HttpServer.JsonSuccess(HttpServer.BuildJsonArray(prisoners.ToArray()));
        }

        public static string Action(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            string body = HttpServer.ReadBody(req);
            var data = ParseSimpleJson(body);

            string prisonerId = GetValue(data, "prisoner");
            string action = GetValue(data, "action");

            if (prisonerId == null || action == null)
                return HttpServer.JsonError("Missing fields: prisoner, action");

            // Find prisoner by load ID
            Pawn prisoner = null;
            foreach (var pawn in Find.CurrentMap.mapPawns.AllPawns)
            {
                if (pawn.guest != null && pawn.guest.IsPrisoner && pawn.guest.HostFaction == Faction.OfPlayer
                    && pawn.GetUniqueLoadID().ToLower().Contains(prisonerId.ToLower()))
                {
                    prisoner = pawn;
                    break;
                }
            }

            if (prisoner == null)
                return HttpServer.JsonError($"Prisoner not found: {prisonerId}");

            switch (action.ToLower())
            {
                case "recruit":
                    if (prisoner.guest != null)
                    {
                        prisoner.guest.resistance = 0f;
                        return HttpServer.JsonSuccess("{\"message\":\"Resistance broken, prisoner will attempt recruit\"}");
                    }
                    return HttpServer.JsonError("Cannot recruit this prisoner");

                case "release":
                    if (prisoner.guest != null)
                    {
                        prisoner.SetFaction(Faction.OfPlayer);
                        if (prisoner.guest != null)
                            prisoner.guest.SetGuestStatus(null);
                        // Make them leave the map area
                        if (!prisoner.Dead && prisoner.Spawned)
                            prisoner.DeSpawn();
                        return HttpServer.JsonSuccess("{\"message\":\"Prisoner released\"}");
                    }
                    return HttpServer.JsonError("Cannot release");

                case "execute":
                    DamageInfo di = new DamageInfo(DamageDefOf.ExecutionCut, 9999f, 999f);
                    prisoner.TakeDamage(di);
                    if (!prisoner.Dead)
                        prisoner.Kill(null);
                    return HttpServer.JsonSuccess("{\"message\":\"Prisoner executed\"}");

                case "reduce_resistance":
                    if (prisoner.guest != null)
                    {
                        prisoner.guest.resistance = Math.Max(0, prisoner.guest.resistance - 10f);
                        return HttpServer.JsonSuccess($"{{\"message\":\"Resistance reduced to {prisoner.guest.resistance:F1}\"}}");
                    }
                    return HttpServer.JsonError("Cannot reduce resistance");

                default:
                    return HttpServer.JsonError($"Unknown action: {action}");
            }
        }

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
