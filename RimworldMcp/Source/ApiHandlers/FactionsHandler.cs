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
    /// Faction and diplomacy endpoints.
    ///
    /// GET  /api/factions         — list all factions with goodwill
    /// POST /api/factions/gift    — gift silver to improve relations
    /// </summary>
    public static class FactionsHandler
    {
        public static string List(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            var factions = Find.FactionManager.AllFactions;
            var list = new List<string>();
            var playerFaction = Faction.OfPlayer;

            foreach (var f in factions)
            {
                if (f == Faction.OfPlayer) continue; // skip player faction itself

                float goodwill = f.GoodwillWith(playerFaction);
                bool hostile = f.HostileTo(playerFaction);

                // Count settlements
                int settlements = 0;
                int pawns = 0;
                foreach (var settlement in Find.WorldObjects.Settlements)
                {
                    if (settlement.Faction == f)
                    {
                        settlements++;
                        pawns += settlement.Name != null ? 1 : 0;
                    }
                }

                list.Add("{" +
                    "\"defName\":" + HttpServer.ToJsonString(f.def.defName) + "," +
                    "\"name\":" + HttpServer.ToJsonString(f.Name ?? f.def.label ?? f.def.defName) + "," +
                    "\"pawnSingular\":" + HttpServer.ToJsonString(f.def.pawnSingular ?? f.def.label ?? f.def.defName) + "," +
                    "\"color\":" + HttpServer.ToJsonString(
                        ((int)(f.Color.r * 255)).ToString("X2") +
                        ((int)(f.Color.g * 255)).ToString("X2") +
                        ((int)(f.Color.b * 255)).ToString("X2")) + "," +
                    "\"goodwill\":" + goodwill.ToString("F0") + "," +
                    "\"hostile\":" + (hostile ? "true" : "false") + "," +
                    "\"hidden\":" + (f.Hidden ? "true" : "false") + "," +
                    "\"permanentEnemy\":" + (f.def.permanentEnemy ? "true" : "false") + "," +
                    "\"settlements\":" + settlements +
                    "}");
            }

            return HttpServer.JsonSuccess("{\"factions\":[" + string.Join(",", list) + "],\"count\":" + list.Count + "}");
        }

        public static string Gift(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            string body = HttpServer.ReadBody(req);
            var data = ParseSimpleJson(body);
            string factionName = GetValue(data, "faction");
            string amountStr = GetValue(data, "amount");

            if (factionName == null)
                return HttpServer.JsonError("Missing field: faction");

            int amount = 100;
            if (amountStr != null)
                int.TryParse(amountStr, out amount);

            if (amount < 1)
                return HttpServer.JsonError("amount must be positive");

            var faction = Find.FactionManager.AllFactions
                .FirstOrDefault(f => f != Faction.OfPlayer &&
                    (f.Name?.ToLower().Contains(factionName.ToLower()) == true ||
                     f.def.defName.ToLower().Contains(factionName.ToLower()) ||
                     f.def.label?.ToLower().Contains(factionName.ToLower()) == true));

            if (faction == null)
                return HttpServer.JsonError($"Faction not found: {factionName}");

            // Check if player has enough silver
            float silverAvailable = 0;
            foreach (var thing in Find.CurrentMap.spawnedThings)
            {
                if (thing.def == ThingDefOf.Silver)
                    silverAvailable += thing.stackCount;
            }

            if (silverAvailable < amount)
                return HttpServer.JsonError($"Not enough silver. Have {silverAvailable:F0}, need {amount}");

            // Consume silver
            int remaining = amount;
            var toRemove = new List<Thing>();
            foreach (var thing in Find.CurrentMap.spawnedThings)
            {
                if (thing.def == ThingDefOf.Silver && remaining > 0)
                {
                    int take = Math.Min(remaining, thing.stackCount);
                    remaining -= take;
                    if (take >= thing.stackCount)
                        toRemove.Add(thing);
                    else
                        thing.stackCount -= take;
                }
            }
            foreach (var t in toRemove)
                t.Destroy();

            // Apply goodwill (capped at 100)
            float goodwillBefore = faction.GoodwillWith(Faction.OfPlayer);
            int goodwillGain = amount / 10; // roughly 10 silver per goodwill point, like vanilla
            faction.TryAffectGoodwillWith(Faction.OfPlayer, goodwillGain);

            float goodwillAfter = faction.GoodwillWith(Faction.OfPlayer);

            return HttpServer.JsonSuccess("{\"message\":" +
                HttpServer.ToJsonString($"Gifted {amount} silver to {faction.Name}. Goodwill: {goodwillBefore:F0} → {goodwillAfter:F0}") + "," +
                "\"goodwill_before\":" + goodwillBefore.ToString("F0") + "," +
                "\"goodwill_after\":" + goodwillAfter.ToString("F0") +
                "}");
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
