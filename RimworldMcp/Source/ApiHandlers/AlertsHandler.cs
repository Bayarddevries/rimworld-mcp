using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using RimWorld;
using Verse;

namespace RimworldMcp
{
    /// <summary>
    /// Exposes RimWorld's in-game alert system. Uses a manually-created AlertsReadout
    /// instance to get the same alerts the game shows on the right side of the HUD.
    ///
    /// GET /api/alerts — returns current active alerts with details
    /// </summary>
    public static class AlertsHandler
    {
        // Cached AlertsReadout instance
        private static object _alertsReadout = null;
        private static Type _alertsReadoutType = null;

        public static string List(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            var alertList = new List<string>();

            try
            {
                // Try to read from a real AlertsReadout instance
                if (ReadAlertsReadout(alertList) && alertList.Count > 0)
                    return HttpServer.JsonSuccess(HttpServer.BuildJsonArray(alertList.ToArray()));

                // Fallback: manual checks
                CheckCommonAlerts(alertList);
            }
            catch (Exception ex)
            {
                return HttpServer.JsonError($"Alert system error: {ex.Message}");
            }

            return HttpServer.JsonSuccess(HttpServer.BuildJsonArray(alertList.ToArray()));
        }

        private static bool ReadAlertsReadout(List<string> alerts)
        {
            try
            {
                // Lazily create an AlertsReadout instance
                if (_alertsReadout == null)
                {
                    _alertsReadoutType = typeof(AlertsReadout); // RimWorld.AlertsReadout
                    if (_alertsReadoutType == null) return false;

                    _alertsReadout = Activator.CreateInstance(_alertsReadoutType);
                    if (_alertsReadout == null) return false;
                }

                // Call AlertsReadoutUpdate (which refreshes the alert state)
                try
                {
                    var updateMethod = _alertsReadoutType.GetMethod("AlertsReadoutUpdate",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic);
                    updateMethod?.Invoke(_alertsReadout, null);
                }
                catch { }

                // Get the current alerts list
                IList currentAlerts = null;

                // Try CurrentAlerts property
                var prop = _alertsReadoutType.GetProperty("CurrentAlerts",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic);
                if (prop != null)
                {
                    currentAlerts = prop.GetValue(_alertsReadout) as IList;
                }

                // If no CurrentAlerts, check for alert-related properties
                if (currentAlerts == null || currentAlerts.Count == 0)
                {
                    var alertsField = _alertsReadoutType.GetField("alerts",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic);
                    if (alertsField != null)
                    {
                        currentAlerts = alertsField.GetValue(_alertsReadout) as IList;
                    }
                }

                if (currentAlerts == null || currentAlerts.Count == 0)
                    return false;

                foreach (var obj in currentAlerts)
                {
                    if (obj == null) continue;
                    var alert = obj as Alert;
                    if (alert == null) continue;

                    try
                    {
                        string label = alert.Label ?? "Alert";
                        string explanation = "";
                        string priority = "medium";
                        string affected = "";

                        try { explanation = alert.GetExplanation(); } catch { }
                        try { priority = alert.Priority.ToString(); } catch { }

                        // Get culprits via reflection (AllCulprits doesn't exist on Alert in 1.6)
                        try
                        {
                            var culpritsProp = alert.GetType().GetProperty("AllCulprits",
                                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
                                System.Reflection.BindingFlags.NonPublic);
                            if (culpritsProp != null)
                            {
                                var culprits = culpritsProp.GetValue(alert) as IList;
                                if (culprits != null)
                                {
                                    var names = new List<string>();
                                    foreach (var c in culprits)
                                    {
                                        var p = c as Pawn;
                                        if (p != null) names.Add(p.Name?.ToStringShort ?? p.LabelCap);
                                        else
                                        {
                                            var thing = c as Thing;
                                            if (thing != null) names.Add(thing.LabelCap ?? c.ToString());
                                            else names.Add(c.ToString());
                                        }
                                    }
                                    affected = string.Join(", ", names);
                                }
                            }
                        }
                        catch { }

                        string report = "";
                        try
                        {
                            var m = alert.GetType().GetMethod("GetReport",
                                System.Reflection.BindingFlags.Instance |
                                System.Reflection.BindingFlags.Public);
                            if (m != null)
                            {
                                var r = m.Invoke(alert, null);
                                if (r != null) report = r.ToString();
                            }
                        }
                        catch { }

                        string combined = (explanation + " " + report).Trim();
                        string prioStr = priority.ToLower();
                        if (prioStr.Contains("critical")) prioStr = "urgent";
                        else if (prioStr.Contains("high")) prioStr = "high";
                        else prioStr = "medium";

                        alerts.Add(HttpServer.BuildJsonObject(
                            ("type", HttpServer.ToJsonString("game_alert")),
                            ("label", HttpServer.ToJsonString(label)),
                            ("pawn", HttpServer.ToJsonString(affected)),
                            ("detail", HttpServer.ToJsonString(combined)),
                            ("priority", HttpServer.ToJsonString(prioStr))
                        ));
                    }
                    catch { }
                }

                return alerts.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        private static void CheckCommonAlerts(List<string> alerts)
        {
            var pawns = Find.CurrentMap.mapPawns.FreeColonists;

            foreach (var pawn in pawns)
            {
                if (pawn.apparel != null)
                {
                    foreach (var apparel in pawn.apparel.WornApparel)
                    {
                        if (apparel.def != null && (float)apparel.HitPoints < apparel.MaxHitPoints * 0.51f)
                        {
                            alerts.Add(MakeAlert("tattered_apparel", "Tattered apparel",
                                pawn.Name?.ToStringShort ?? "Unknown",
                                $"{apparel.def.label} ({apparel.HitPoints}/{apparel.MaxHitPoints} HP)", "medium"));
                            break;
                        }
                    }
                }

                if (pawn.health != null && pawn.health.HasHediffsNeedingTend())
                {
                    string detail = "";
                    try
                    {
                        var tendable = pawn.health.hediffSet?.hediffs?.Where(h => h.TendableNow());
                        if (tendable != null)
                            detail = string.Join(", ", tendable.Select(h => h.LabelBase ?? h.def?.label ?? "wound"));
                    }
                    catch { detail = "wounds"; }
                    alerts.Add(MakeAlert("needs_treatment", "Needs treatment",
                        pawn.Name?.ToStringShort ?? "Unknown", detail, "high"));
                }

                if (pawn.needs?.food != null && pawn.needs.food.CurCategory == HungerCategory.Starving)
                    alerts.Add(MakeAlert("starving", "Starving",
                        pawn.Name?.ToStringShort ?? "Unknown",
                        $"{pawn.needs.food.CurLevelPercentage:P0} food", "urgent"));

                if (pawn.needs?.mood != null && pawn.needs.mood.CurLevelPercentage < 0.25f)
                    alerts.Add(MakeAlert("low_mood", "Low mood — break risk",
                        pawn.Name?.ToStringShort ?? "Unknown",
                        $"Mood: {pawn.needs.mood.CurLevelPercentage:P0}", "high"));

                // Naked penalty / missing clothes
                if (pawn.apparel != null)
                {
                    float coverage = 0;
                    foreach (var ap in pawn.apparel.WornApparel)
                    {
                        if (ap.def?.apparel?.bodyPartGroups != null)
                            coverage += ap.def.apparel.bodyPartGroups.Count;
                    }
                    if (coverage < 2)
                        alerts.Add(MakeAlert("naked", "Missing clothing",
                            pawn.Name?.ToStringShort ?? "Unknown", "Pawn has little to no clothing", "medium"));
                }
            }

            // Research
            int unfinished = DefDatabase<ResearchProjectDef>.AllDefsListForReading
                .Count(p => !p.IsFinished && p.PrerequisitesCompleted);
            if (unfinished > 0)
            {
                string active = "none";
                try
                {
                    var f = typeof(ResearchManager).GetField("currentProj",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    if (f?.GetValue(Find.ResearchManager) is ResearchProjectDef rp)
                        active = rp.label;
                }
                catch { }
                alerts.Add(MakeAlert("research_available", $"{unfinished} research projects",
                    "", $"Active: {active}", "low"));
            }

            // Food
            float totalFood = 0f;
            foreach (var thing in Find.CurrentMap.spawnedThings)
            {
                if (thing.def?.EverHaulable == true && thing.def?.IsIngestible == true && thing.def?.ingestible != null)
                    totalFood += thing.GetStatValue(StatDefOf.Nutrition) * thing.stackCount;
            }
            int cc = Find.CurrentMap.mapPawns.FreeColonistsCount;
            if (cc > 0)
            {
                float days = totalFood / cc / 2f;
                if (days < 5f)
                    alerts.Add(MakeAlert("low_food", "Low food",
                        "", $"~{days:F1} days ({cc} colonists)", days < 2f ? "urgent" : "high"));
            }
        }

        private static string MakeAlert(string type, string label, string pawn, string detail, string priority)
        {
            return HttpServer.BuildJsonObject(
                ("type", HttpServer.ToJsonString(type)),
                ("label", HttpServer.ToJsonString(label)),
                ("pawn", HttpServer.ToJsonString(pawn)),
                ("detail", HttpServer.ToJsonString(detail)),
                ("priority", HttpServer.ToJsonString(priority))
            );
        }
    }
}
