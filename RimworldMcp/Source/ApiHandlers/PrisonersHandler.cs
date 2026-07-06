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
    /// Extended prisoner management with detail view and recruit mode control.
    ///
    /// GET  /api/prisoners/{id}      — detailed prisoner info (traits, health, skills)
    /// POST /api/prisoners/mode      — set recruit mode: recruit/hold/execute
    /// </summary>
    public static class PrisonersHandler
    {
        // ─── List (existing) ───

        public static string List(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            var prisonerList = new List<string>();
            foreach (var pawn in Find.CurrentMap.mapPawns.AllPawns)
            {
                if (pawn.guest != null && pawn.guest.IsPrisoner && pawn.guest.HostFaction == Faction.OfPlayer)
                {
                    float health = pawn.health.summaryHealth.SummaryHealthPercent * 100f;
                    float mood = pawn.needs?.mood?.CurLevelPercentage ?? 50;
                    float resist = pawn.guest.resistance;
                    float recruitPct = pawn.guest.resistance;

                    prisonerList.Add(HttpServer.BuildJsonObject(
                        ("id", HttpServer.ToJsonString(pawn.GetUniqueLoadID())),
                        ("name", HttpServer.ToJsonString(pawn.Name?.ToStringShort ?? pawn.LabelCap)),
                        ("age", pawn.ageTracker?.AgeBiologicalYears.ToString() ?? "?"),
                        ("gender", HttpServer.ToJsonString(pawn.gender.ToString())),
                        ("health", health.ToString("F0")),
                        ("mood", mood.ToString("F0")),
                        ("resistance", resist.ToString("F1")),
                        ("recruit_progress", (recruitPct * 100f / 100f).ToString("F2")),
                        ("kind", HttpServer.ToJsonString(pawn.kindDef?.label ?? "prisoner"))
                    ));
                }
            }
            return HttpServer.JsonSuccess(HttpServer.BuildJsonArray(prisonerList.ToArray()));
        }

        // ─── Detail ───

        public static string Detail(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            string id = HttpServer.ExtractIdFromPath(req.Url.AbsolutePath, "/api/prisoners/");
            if (id == null)
                return HttpServer.JsonError("Missing prisoner ID");

            var pawn = Find.CurrentMap.mapPawns.AllPawns.FirstOrDefault(p =>
                p.GetUniqueLoadID() == id ||
                (p.Name?.ToStringShort?.ToLower() == id.ToLower()));

            if (pawn == null || pawn.guest == null || !pawn.guest.IsPrisoner)
                return HttpServer.JsonError("Prisoner not found");

            // Calculate recruit mode
            string mode = "hold";
            if (pawn.guest.Recruitable) mode = "recruit";
            if (pawn.health != null && pawn.health.Dead) mode = "dead";

            // Determine interaction mode string
            string interactionMode = "";
            try
            {
                var modeField = typeof(Pawn_GuestTracker).GetField("interactionMode",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (modeField != null)
                {
                    var modeObj = modeField.GetValue(pawn.guest);
                    if (modeObj != null)
                        interactionMode = modeObj.ToString();
                }
            }
            catch { }

            // Traits
            var traits = new List<string>();
            if (pawn.story?.traits != null)
            {
                foreach (var t in pawn.story.traits.allTraits)
                {
                    traits.Add(HttpServer.BuildJsonObject(
                        ("label", HttpServer.ToJsonString(t.Label)),
                        ("degree", t.Degree.ToString()),
                        ("description", HttpServer.ToJsonString(t.LabelCap))
                    ));
                }
            }

            // Health hediffs (wounds, ailments)
            var hediffs = new List<string>();
            if (pawn.health?.hediffSet != null)
            {
                foreach (var h in pawn.health.hediffSet.hediffs)
                {
                    hediffs.Add(HttpServer.BuildJsonObject(
                        ("label", HttpServer.ToJsonString(h.LabelBase ?? h.def?.label ?? "unknown")),
                        ("severity", h.Severity.ToString("F2")),
                        ("is_tendable", (h.TendableNow() ? "true" : "false"))
                    ));
                }
            }

            // Skills (top 5)
            var skills = new List<string>();
            if (pawn.skills != null)
            {
                foreach (var s in pawn.skills.skills.OrderByDescending(sk => sk.Level).Take(5))
                {
                    skills.Add(HttpServer.BuildJsonObject(
                        ("label", HttpServer.ToJsonString(s.def?.label ?? "?")),
                        ("level", s.Level.ToString()),
                        ("passion", HttpServer.ToJsonString(s.passion.ToString()))
                    ));
                }
            }

            string healthPct = (pawn.health.summaryHealth.SummaryHealthPercent * 100f).ToString("F0");
            string moodPct = (pawn.needs?.mood?.CurLevelPercentage ?? 50).ToString("F0");
            string resist = (pawn.guest.resistance).ToString("F1");
            string recruitPct = "0";
            try
            {
                var rpField = typeof(Pawn_GuestTracker).GetField("recruitProgress",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (rpField != null)
                {
                    object rpVal = rpField.GetValue(pawn.guest);
                    recruitPct = (Convert.ToSingle(rpVal) * 100f).ToString("F1");
                }
            } catch { }

            return HttpServer.JsonSuccess(HttpServer.BuildJsonObject(
                ("id", HttpServer.ToJsonString(pawn.GetUniqueLoadID())),
                ("name", HttpServer.ToJsonString(pawn.Name?.ToStringShort ?? pawn.LabelCap)),
                ("full_name", HttpServer.ToJsonString(pawn.Name?.ToStringFull ?? pawn.LabelCap)),
                ("age", pawn.ageTracker?.AgeBiologicalYears.ToString() ?? "?"),
                ("gender", HttpServer.ToJsonString(pawn.gender.ToString())),
                ("health", healthPct),
                ("mood", moodPct),
                ("resistance", resist),
                ("recruit_progress", recruitPct),
                ("mode", HttpServer.ToJsonString(mode)),
                ("kind", HttpServer.ToJsonString(pawn.kindDef?.label ?? "prisoner")),
                ("traits", HttpServer.BuildJsonArray(traits.ToArray())),
                ("hediffs", HttpServer.BuildJsonArray(hediffs.ToArray())),
                ("skills", HttpServer.BuildJsonArray(skills.ToArray()))
            ));
        }

        // ─── Recruit Mode ───

        public static string SetMode(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            string body = HttpServer.ReadBody(req);
            var data = ParseSimpleJson(body);

            string prisonerId = GetValue(data, "prisoner");
            string mode = GetValue(data, "mode") ?? "recruit";

            if (prisonerId == null)
                return HttpServer.JsonError("Missing field: prisoner");

            var pawn = Find.CurrentMap.mapPawns.AllPawns.FirstOrDefault(p =>
                p.GetUniqueLoadID() == prisonerId ||
                (p.Name?.ToStringShort?.ToLower() == prisonerId.ToLower()));

            if (pawn == null || pawn.guest == null || !pawn.guest.IsPrisoner)
                return HttpServer.JsonError("Prisoner not found");

            switch (mode.ToLower())
            {
                case "recruit":
                    pawn.guest.Recruitable = true;
                    SetInteractionMode(pawn, RimWorld.PrisonerInteractionModeDefOf.AttemptRecruit);
                    return HttpServer.JsonSuccess($"{{\"message\":\"{pawn.Name.ToStringShort} set to recruit mode\"}}");

                case "hold":
                    pawn.guest.Recruitable = true;
                    SetInteractionMode(pawn, RimWorld.PrisonerInteractionModeDefOf.ReduceResistance);
                    return HttpServer.JsonSuccess($"{{\"message\":\"{pawn.Name.ToStringShort} set to hold/reduce resistance\"}}");

                case "execute":
                    SetInteractionMode(pawn, RimWorld.PrisonerInteractionModeDefOf.Execution);
                    return HttpServer.JsonSuccess($"{{\"message\":\"{pawn.Name.ToStringShort} set to execution\"}}");

                default:
                    return HttpServer.JsonError("Unknown mode. Use 'recruit', 'hold', or 'execute'.");
            }
        }

        // ─── Legacy Action ───

        public static string Action(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            string body = HttpServer.ReadBody(req);
            var data = ParseSimpleJson(body);

            string prisonerId = GetValue(data, "prisoner");
            string action = GetValue(data, "action") ?? "recruit";

            var pawn = Find.CurrentMap.mapPawns.AllPawns.FirstOrDefault(p =>
                p.GetUniqueLoadID() == prisonerId ||
                (p.Name?.ToStringShort?.ToLower() == prisonerId.ToLower()));

            if (pawn == null || pawn.guest == null || !pawn.guest.IsPrisoner)
                return HttpServer.JsonError("Prisoner not found");

            switch (action.ToLower())
            {
                case "recruit":
                    pawn.guest.Recruitable = true;
                    SetInteractionMode(pawn, RimWorld.PrisonerInteractionModeDefOf.AttemptRecruit);
                    pawn.guest.resistance = Math.Max(0, (pawn.guest.resistance) - 5);
                    return HttpServer.JsonSuccess($"{{\"message\":\"Recruiting {pawn.Name.ToStringShort}\"}}");

                case "release":
                    pawn.guest.SetGuestStatus(null);
                    return HttpServer.JsonSuccess($"{{\"message\":\"Released {pawn.Name.ToStringShort}\"}}");

                case "reduce_resistance":
                    float current = pawn.guest.resistance;
                    pawn.guest.resistance = Math.Max(0, current - 10);
                    return HttpServer.JsonSuccess($"{{\"message\":\"Resistance reduced to {pawn.guest.resistance:F1}\"}}");

                default:
                    return HttpServer.JsonError("Unknown action. Use 'recruit', 'release', or 'reduce_resistance'.");
            }
        }

        private static void SetInteractionMode(Pawn pawn, PrisonerInteractionModeDef mode)
        {
            try
            {
                var field = typeof(Pawn_GuestTracker).GetField("interactionMode",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (field != null)
                    field.SetValue(pawn.guest, mode);
            }
            catch { }
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
