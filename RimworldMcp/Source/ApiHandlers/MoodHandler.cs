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
    /// Pawn mood and thought breakdown.
    ///
    /// GET  /api/mood                  — mood overview for all colonists
    /// GET  /api/mood/{id}             — detailed thought breakdown for one pawn
    /// </summary>
    public static class MoodHandler
    {
        public static string Overview(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            var list = new List<string>();
            var colonists = GameBridge.GetAllColonists();

            foreach (var pawn in colonists)
            {
                float mood = pawn.needs?.mood?.CurLevel ?? 0.5f;
                float threshold = 0;
                string breakRisk = "stable";

                try
                {
                    var breaker = pawn.mindState?.mentalBreaker;
                    if (breaker != null && pawn.needs?.mood != null)
                    {
                        var btField = typeof(Verse.AI.MentalBreaker).GetField("BreakThreshold", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                        if (btField != null) threshold = (float)btField.GetValue(breaker);
                        else threshold = 0.25f;

                        float moodPct = pawn.needs.mood.CurLevelPercentage;
                        if (moodPct < threshold * 0.7f) breakRisk = "critical";
                        else if (moodPct < threshold * 0.9f) breakRisk = "risky";
                        else if (moodPct < threshold) breakRisk = "unstable";
                    }
                }
                catch { }

                // Get top negative and positive thought labels
                string topNeg = "";
                string topPos = "";
                try
                {
                    var thoughts = pawn.needs?.mood?.thoughts;
                    if (thoughts != null)
                    {
                        // Use reflection to get the mood thoughts list
                        var thoughtListField = typeof(ThoughtHandler).GetField("MoodThoughts", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                        var thoughtList = thoughtListField?.GetValue(thoughts) as System.Collections.IList;
                        if (thoughtList != null)
                        {
                            float worstOffset = 0, bestOffset = 0;
                            foreach (var tObj in thoughtList)
                            {
                                var thought = tObj as Thought;
                                if (thought == null) continue;
                                float offset = 0;
                                try
                                {
                                    var stage = thought.CurStage;
                                    if (stage != null) offset = stage.baseMoodEffect;
                                }
                                catch { }

                                string label = thought.LabelCap ?? thought.def?.label ?? "Thought";
                                if (offset < worstOffset)
                                {
                                    worstOffset = offset;
                                    topNeg = label;
                                }
                                if (offset > bestOffset)
                                {
                                    bestOffset = offset;
                                    topPos = label;
                                }
                            }
                        }
                    }
                }
                catch { }

                list.Add("{" +
                    "\"id\":" + pawn.thingIDNumber + "," +
                    "\"name\":" + HttpServer.ToJsonString(pawn.Name?.ToStringShort ?? pawn.LabelCap) + "," +
                    "\"mood\":" + (mood * 100).ToString("F0") + "," +
                    "\"breakThreshold\":" + (threshold * 100).ToString("F0") + "," +
                    "\"breakRisk\":" + HttpServer.ToJsonString(breakRisk) + "," +
                    "\"topNegative\":" + HttpServer.ToJsonString(topNeg) + "," +
                    "\"topPositive\":" + HttpServer.ToJsonString(topPos) +
                    "}");
            }

            return HttpServer.JsonSuccess("[" + string.Join(",", list) + "]");
        }

        public static string Detail(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            string path = req.Url.AbsolutePath.ToLowerInvariant();
            string idStr = HttpServer.ExtractIdFromPath(path, "/api/mood/");
            if (!int.TryParse(idStr, out int pawnId))
                return HttpServer.JsonError("Invalid pawn ID");

            var pawn = FindPawnById(pawnId);
            if (pawn == null)
                return HttpServer.JsonError($"Colonist not found: {pawnId}");

            float mood = pawn.needs?.mood?.CurLevel ?? 0.5f;
            float threshold = 0;
            try {
                var breaker = pawn.mindState?.mentalBreaker;
                if (breaker != null)
                {
                    var btField = typeof(Verse.AI.MentalBreaker).GetField("BreakThreshold", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                    if (btField != null) threshold = (float)btField.GetValue(breaker);
                }
            }
            catch { }

            // Get all active thoughts
            var thoughtList = new List<string>();
            try
            {
                var thoughts = pawn.needs?.mood?.thoughts;
                if (thoughts != null)
                {
                    // Direct thoughts (mood thoughts the pawn actively has)
                    var allThoughtsField = typeof(ThoughtHandler).GetField("MoodThoughts", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                    var allThoughts = allThoughtsField?.GetValue(thoughts) as System.Collections.IList;
                    if (allThoughts != null)
                    {
                        foreach (var tObj in allThoughts)
                        {
                            var thought = tObj as Thought;
                            if (thought == null) continue;
                            float offset = 0;
                            string label = thought.LabelCap ?? thought.def?.label ?? "Thought";
                            try
                            {
                                var stage = thought.CurStage;
                                if (stage != null) offset = stage.baseMoodEffect;
                            }
                            catch { }

                            thoughtList.Add("{" +
                                "\"label\":" + HttpServer.ToJsonString(label) + "," +
                                "\"offset\":" + offset.ToString("F1") +
                                "}");
                        }
                    }
                }
            }
            catch { }

            // Sort by offset ascending (worst first)
            thoughtList.Sort((a, b) =>
            {
                float offA = 0, offB = 0;
                var m1 = System.Text.RegularExpressions.Regex.Match(a, "\"offset\":([-\\d.]+)");
                var m2 = System.Text.RegularExpressions.Regex.Match(b, "\"offset\":([-\\d.]+)");
                if (m1.Success) float.TryParse(m1.Groups[1].Value, out offA);
                if (m2.Success) float.TryParse(m2.Groups[1].Value, out offB);
                return offA.CompareTo(offB);
            });

            return HttpServer.JsonSuccess("{" +
                "\"id\":" + pawn.thingIDNumber + "," +
                "\"name\":" + HttpServer.ToJsonString(pawn.Name?.ToStringShort ?? pawn.LabelCap) + "," +
                "\"mood\":" + (mood * 100).ToString("F0") + "," +
                "\"breakThreshold\":" + (threshold * 100).ToString("F0") + "," +
                "\"thoughts\":" + (thoughtList.Count > 0 ? "[" + string.Join(",", thoughtList) + "]" : "[]") +
                "}");
        }

        private static Pawn FindPawnById(int id)
        {
            foreach (var pawn in Find.CurrentMap.mapPawns.FreeColonists)
                if (pawn.thingIDNumber == id) return pawn;
            return null;
        }
    }
}
