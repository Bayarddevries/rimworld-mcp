using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace RimworldMcp
{
    /// <summary>
    /// Simple goal tracking system. Goals are session-scoped (not persisted).
    /// Hermes sets goals and checks progress later.
    /// </summary>
    public static class GoalManager
    {
        private static readonly List<ColonyGoal> _goals = new List<ColonyGoal>();
        private static readonly object _lock = new object();

        public static void SetGoal(string id, string description, string type, float targetValue, string targetItem = null)
        {
            lock (_lock)
            {
                // Remove existing goal with same ID
                _goals.RemoveAll(g => g.Id == id);

                _goals.Add(new ColonyGoal
                {
                    Id = id,
                    Description = description,
                    Type = type,
                    TargetValue = targetValue,
                    CurrentValue = 0,
                    TargetItem = targetItem,
                    CreatedTick = Find.TickManager.TicksGame,
                    Completed = false
                });
            }
        }

        public static void UpdateGoal(string id, float currentValue)
        {
            lock (_lock)
            {
                var goal = _goals.FirstOrDefault(g => g.Id == id);
                if (goal != null)
                {
                    goal.CurrentValue = currentValue;
                    if (currentValue >= goal.TargetValue && !goal.Completed)
                    {
                        goal.Completed = true;
                        goal.CompletedTick = Find.TickManager.TicksGame;
                        EventFeedManager.RecordEvent("goal_completed",
                            $"Goal completed: {goal.Description}", "info");
                    }
                }
            }
        }

        public static void RemoveGoal(string id)
        {
            lock (_lock)
                _goals.RemoveAll(g => g.Id == id);
        }

        public static List<ColonyGoal> GetAllGoals()
        {
            lock (_lock)
                return new List<ColonyGoal>(_goals);
        }

        /// <summary>
        /// Recalculate current values for resource-type goals.
        /// </summary>
        public static void RefreshResourceGoals()
        {
            if (!GameBridge.IsGameReady()) return;

            lock (_lock)
            {
                foreach (var goal in _goals)
                {
                    if (goal.Type == "resource" && goal.TargetItem != null)
                    {
                        float total = 0;
                        foreach (var thing in Find.CurrentMap.spawnedThings)
                        {
                            if (thing.def.defName.ToLower() == goal.TargetItem.ToLower() ||
                                thing.def.label.ToLower().Contains(goal.TargetItem.ToLower()))
                            {
                                total += thing.stackCount;
                            }
                        }
                        goal.CurrentValue = total;
                        if (total >= goal.TargetValue && !goal.Completed)
                        {
                            goal.Completed = true;
                            goal.CompletedTick = Find.TickManager.TicksGame;
                        }
                    }
                    else if (goal.Type == "research")
                    {
                        var proj = DefDatabase<ResearchProjectDef>.AllDefsListForReading
                            .FirstOrDefault(p => p.defName.ToLower() == goal.TargetItem?.ToLower());
                        if (proj != null && proj.IsFinished)
                        {
                            goal.CurrentValue = goal.TargetValue;
                            if (!goal.Completed)
                            {
                                goal.Completed = true;
                                goal.CompletedTick = Find.TickManager.TicksGame;
                            }
                        }
                    }
                    else if (goal.Type == "colonists")
                    {
                        goal.CurrentValue = Find.CurrentMap.mapPawns.FreeColonistsCount;
                        if (goal.CurrentValue >= goal.TargetValue && !goal.Completed)
                        {
                            goal.Completed = true;
                            goal.CompletedTick = Find.TickManager.TicksGame;
                        }
                    }
                    else if (goal.Type == "wealth")
                    {
                        goal.CurrentValue = Find.CurrentMap.wealthWatcher.WealthTotal;
                        if (goal.CurrentValue >= goal.TargetValue && !goal.Completed)
                        {
                            goal.Completed = true;
                            goal.CompletedTick = Find.TickManager.TicksGame;
                        }
                    }
                }
            }
        }

        public static string GoalsToJson()
        {
            RefreshResourceGoals();

            var items = new List<string>();
            lock (_lock)
            {
                foreach (var goal in _goals)
                {
                    items.Add("{" +
                        $"\"id\":\"{HttpServer.EscapeJson(goal.Id)}\"," +
                        $"\"description\":\"{HttpServer.EscapeJson(goal.Description)}\"," +
                        $"\"type\":\"{HttpServer.EscapeJson(goal.Type)}\"," +
                        $"\"target\":{goal.TargetValue}," +
                        $"\"current\":{goal.CurrentValue}," +
                        $"\"progress_pct\":{(goal.TargetValue > 0 ? (goal.CurrentValue / goal.TargetValue * 100).ToString("F0") : "0")}," +
                        $"\"completed\":{goal.Completed.ToString().ToLower()}," +
                        $"\"target_item\":\"{HttpServer.EscapeJson(goal.TargetItem ?? "")}\"" +
                        "}");
                }
            }
            return "[" + string.Join(",", items) + "]";
        }
    }

    public class ColonyGoal
    {
        public string Id { get; set; }
        public string Description { get; set; }
        public string Type { get; set; }    // resource, research, colonists, wealth, custom
        public float TargetValue { get; set; }
        public float CurrentValue { get; set; }
        public string TargetItem { get; set; }
        public int CreatedTick { get; set; }
        public bool Completed { get; set; }
        public int CompletedTick { get; set; }
    }
}
