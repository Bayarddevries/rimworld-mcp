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
    /// Exposes RimWorld's in-game alert system — the messages shown on the right side
    /// of the screen ("Tattered apparel", "Colonist needs treatment", etc.)
    ///
    /// Instead of duplicating RimWorld's Alert subclasses, we directly check common
    /// game conditions that the player would want to see.
    ///
    /// GET /api/alerts — returns current active alerts with details
    /// </summary>
    public static class AlertsHandler
    {
        public static string List(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            var alertList = new List<string>();

            try
            {
                CheckTatteredApparel(alertList);
                CheckNeedsTreatment(alertList);
                CheckStarving(alertList);
                CheckUnfinishedResearch(alertList);
                CheckUnforbiddenItems(alertList);
                CheckLowFood(alertList);
            }
            catch (Exception ex)
            {
                return HttpServer.JsonError($"Alert system error: {ex.Message}");
            }

            return HttpServer.JsonSuccess(HttpServer.BuildJsonArray(alertList.ToArray()));
        }

        private static void CheckTatteredApparel(List<string> alerts)
        {
            foreach (var pawn in Find.CurrentMap.mapPawns.FreeColonists)
            {
                if (pawn.apparel == null) continue;
                foreach (var apparel in pawn.apparel.WornApparel)
                {
                    if (apparel.def != null && (float)apparel.HitPoints < apparel.MaxHitPoints * 0.51f)
                    {
                        alerts.Add(HttpServer.BuildJsonObject(
                            ("type", HttpServer.ToJsonString("tattered_apparel")),
                            ("label", HttpServer.ToJsonString($"Tattered apparel")),
                            ("pawn", HttpServer.ToJsonString(pawn.Name?.ToStringShort ?? "Unknown")),
                            ("detail", HttpServer.ToJsonString($"{apparel.def.label} ({apparel.HitPoints}/{apparel.MaxHitPoints} HP)")),
                            ("priority", HttpServer.ToJsonString("medium"))
                        ));
                        break;
                    }
                }
            }
        }

        private static void CheckNeedsTreatment(List<string> alerts)
        {
            foreach (var pawn in Find.CurrentMap.mapPawns.FreeColonists)
            {
                if (pawn.health != null && pawn.health.HasHediffsNeedingTend())
                {
                    string detail = "";
                    try
                    {
                        var hediffs = pawn.health.hediffSet?.hediffs;
                        if (hediffs != null)
                        {
                            var tendable = hediffs.Where(h => h.TendableNow());
                            detail = string.Join(", ", tendable.Select(h => h.LabelBase ?? h.def?.label ?? "wound"));
                        }
                    }
                    catch { detail = "wounds"; }

                    alerts.Add(HttpServer.BuildJsonObject(
                        ("type", HttpServer.ToJsonString("needs_treatment")),
                        ("label", HttpServer.ToJsonString("Needs treatment")),
                        ("pawn", HttpServer.ToJsonString(pawn.Name?.ToStringShort ?? "Unknown")),
                        ("detail", HttpServer.ToJsonString(detail)),
                        ("priority", HttpServer.ToJsonString("high"))
                    ));
                }
            }
        }

        private static void CheckStarving(List<string> alerts)
        {
            foreach (var pawn in Find.CurrentMap.mapPawns.FreeColonists)
            {
                if (pawn.needs?.food == null) continue;
                if (pawn.needs.food.CurCategory == HungerCategory.Starving)
                {
                    alerts.Add(HttpServer.BuildJsonObject(
                        ("type", HttpServer.ToJsonString("starving")),
                        ("label", HttpServer.ToJsonString("Starving")),
                        ("pawn", HttpServer.ToJsonString(pawn.Name?.ToStringShort ?? "Unknown")),
                        ("detail", HttpServer.ToJsonString($"{pawn.needs.food.CurLevelPercentage:P0} food")),
                        ("priority", HttpServer.ToJsonString("urgent"))
                    ));
                }
            }
        }

        private static void CheckUnfinishedResearch(List<string> alerts)
        {
            var unfinished = DefDatabase<ResearchProjectDef>.AllDefsListForReading
                .Where(p => !p.IsFinished && p.PrerequisitesCompleted);
            int count = unfinished.Count();
            if (count > 0)
            {
                string active = "unknown";
                var activeField = typeof(ResearchManager).GetField("currentProj",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (activeField != null)
                {
                    var val = activeField.GetValue(Find.ResearchManager);
                    if (val is ResearchProjectDef rp)
                        active = rp.label;
                }
                alerts.Add(HttpServer.BuildJsonObject(
                    ("type", HttpServer.ToJsonString("research_available")),
                    ("label", HttpServer.ToJsonString($"{count} research projects available")),
                    ("pawn", HttpServer.ToJsonString("")),
                    ("detail", HttpServer.ToJsonString($"Set active research in the Research tab. Active projects: {active}")),
                    ("priority", HttpServer.ToJsonString("low"))
                ));
            }
        }

        private static void CheckUnforbiddenItems(List<string> alerts)
        {
            // Quick check for items that need hauling
            int count = 0;
            foreach (var thing in Find.CurrentMap.spawnedThings)
            {
                if (count > 5) break;
                if (thing.def?.EverHaulable == true && !thing.IsForbidden(Faction.OfPlayer) && !thing.IsInValidStorage())
                    count++;
            }
            if (count > 0)
            {
                alerts.Add(HttpServer.BuildJsonObject(
                    ("type", HttpServer.ToJsonString("items_to_haul")),
                    ("label", HttpServer.ToJsonString($"{count} items to haul")),
                    ("pawn", HttpServer.ToJsonString("")),
                    ("detail", HttpServer.ToJsonString("Items waiting to be hauled to stockpiles")),
                    ("priority", HttpServer.ToJsonString("low"))
                ));
            }
        }

        private static void CheckLowFood(List<string> alerts)
        {
            // Count food items in stockpiles
            float totalFood = 0f;
            foreach (var thing in Find.CurrentMap.spawnedThings)
            {
                if (thing.def?.EverHaulable == true && thing.def?.IsIngestible == true && thing.def?.ingestible != null)
                    totalFood += thing.GetStatValue(StatDefOf.Nutrition) * thing.stackCount;
            }

            int colonistCount = Find.CurrentMap.mapPawns.FreeColonistsCount;
            if (colonistCount > 0)
            {
                float mealsPerColonist = totalFood / colonistCount / 2f; // ~2 nutrition per meal, 2 meals/day
                if (mealsPerColonist < 3f) // Less than ~3 days of food
                {
                    alerts.Add(HttpServer.BuildJsonObject(
                        ("type", HttpServer.ToJsonString("low_food")),
                        ("label", HttpServer.ToJsonString("Low food")),
                        ("pawn", HttpServer.ToJsonString("")),
                        ("detail", HttpServer.ToJsonString($"~{mealsPerColonist:F1} days of food remaining for {colonistCount} colonists")),
                        ("priority", HttpServer.ToJsonString("high"))
                    ));
                }
            }
        }
    }
}
