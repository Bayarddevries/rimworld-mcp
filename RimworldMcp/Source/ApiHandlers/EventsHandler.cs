using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using RimWorld;
using Verse;

namespace RimworldMcp
{
    public static class EventsHandler
    {
        public static string StorytellerInfo(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            var storyteller = Find.Storyteller;
            if (storyteller == null)
                return HttpServer.JsonError("No storyteller active");

            return HttpServer.JsonSuccess(HttpServer.BuildJsonObject(
                ("storyteller", HttpServer.ToJsonString(storyteller.def?.label ?? "Unknown")),
                ("difficulty", HttpServer.ToJsonString(storyteller.difficulty?.ToString() ?? "Unknown")),
                ("ticks_game", Find.TickManager.TicksGame.ToString()),
                ("storyteller_enabled", "true")
            ));
        }

        public static string Trigger(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            string body = HttpServer.ReadBody(req);
            var data = ParseSimpleJson(body);

            string eventName = GetValue(data, "event") ?? "raid";
            string pointsStr = GetValue(data, "points");

            float points = 500;
            if (pointsStr != null) float.TryParse(pointsStr, out points);

            // Look up incident def by defName (cross-version safe)
            string lower = eventName.ToLower();

            // Common RimWorld incident defNames for version 1.6
            var knownIncidents = new Dictionary<string, string>
            {
                {"raid", "RaidEnemy"},
                {"raid_enemy", "RaidEnemy"},
                {"manhunters", "ManhunterPack"},
                {"manhunter", "ManhunterPack"},
                {"traders", "TraderCaravan"},
                {"trader", "TraderCaravan"},
                {"trade_caravan", "TraderCaravan"},
                {"visitors", "VisitorGroup"},
                {"friendly_visitors", "VisitorGroup"},
                {"refugee", "RefugeePod"},
                {"refugees", "RefugeePod"},
                {"wanderer", "WandererJoin"},
                {"join_wanderer", "WandererJoin"},
                {"ship_part", "PsychicEmanatorShipPart"},
                {"psychic_ship", "PsychicEmanatorShipPart"},
                {"eclipse", "Eclipse"},
                {"solar_eclipse", "Eclipse"},
                {"toxic_fallout", "ToxicFallout"},
                {"fallout", "ToxicFallout"},
                {"volcanic_winter", "VolcanicWinter"},
                {"flashstorm", "Flashstorm"},
                {"mechanoids", "MechCluster"},
                {"mech_cluster", "MechCluster"},
                {"infestation", "Infestation"},
                {"bugs", "Infestation"},
                {"cold_snap", "ColdSnap"},
                {"cold", "ColdSnap"},
                {"heat_wave", "HeatWave"},
                {"heat", "HeatWave"},
                {"blight", "CropBlight"},
                {"crop_blight", "CropBlight"},
                {"ambrosia", "AmbrosiaSprout"},
                {"ambrosia_sprout", "AmbrosiaSprout"},
            };

            IncidentDef incidentDef = null;

            if (knownIncidents.TryGetValue(lower, out string defName))
            {
                incidentDef = DefDatabase<IncidentDef>.GetNamedSilentFail(defName);
            }

            if (incidentDef == null)
            {
                // Fall back to partial defName match
                incidentDef = DefDatabase<IncidentDef>.AllDefsListForReading
                    .FirstOrDefault(d => d.defName.ToLower().Contains(lower) ||
                                         d.label?.ToLower().Contains(lower) == true);
            }

            if (incidentDef == null)
            {
                var allIncidents = DefDatabase<IncidentDef>.AllDefsListForReading
                    .Select(d => d.defName).OrderBy(n => n).Take(20);
                return HttpServer.JsonError($"Event not found: {eventName}");
            }

            // Fire the incident
            var parms = new IncidentParms
            {
                target = Find.CurrentMap,
                points = points,
                forced = true
            };

            // Determine faction for the incident
            if (incidentDef.category == IncidentCategoryDefOf.ThreatBig ||
                incidentDef.category == IncidentCategoryDefOf.ThreatSmall)
            {
                parms.faction = Find.FactionManager.RandomEnemyFaction();
            }
            else if (incidentDef.category == IncidentCategoryDefOf.Misc)
            {
                parms.faction = Faction.OfPlayer;
            }

            bool success = incidentDef.Worker.TryExecute(parms);

            if (success)
                return HttpServer.JsonSuccess($"{{\"message\":\"Triggered event: {incidentDef.label}\"}}");
            else
                return HttpServer.JsonError($"Failed to trigger {incidentDef.label}. Event conditions may not be met.");
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
