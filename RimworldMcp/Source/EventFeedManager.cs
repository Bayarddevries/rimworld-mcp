using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimworldMcp
{
    /// <summary>
    /// Captures notable game events (raids, research, deaths, etc.) via Harmony patches
    /// and periodic checks. Hermes polls /api/events/feed to get what changed.
    /// </summary>
    public static class EventFeedManager
    {
        private static readonly List<GameEvent> _allEvents = new List<GameEvent>();
        private static readonly Queue<GameEvent> _pendingEvents = new Queue<GameEvent>();
        private static readonly object _lock = new object();
        private static int _lastCheckTick;

        // Periodic state snapshots for detecting silent changes (resources, needs)
        private static int _lastColonistCount;
        private static float _lastFoodCount;
        private static int _lastResearchCompleted;
        private static int _lastLetterCount;
        private static readonly Dictionary<string, (float resistance, float recruitProgress)> _lastPrisonerStates =
            new Dictionary<string, (float resistance, float recruitProgress)>();
        private static readonly HashSet<string> _seenAutoPauseAlerts = new HashSet<string>();

        public static void Init()
        {
            // This runs on the background poller thread — only sets a flag.
            // Actual init (Find.TickManager etc.) happens in InitGameThread() on the game thread.
        }

        public static void InitGameThread()
        {
            _lastCheckTick = Find.TickManager.TicksGame;
            _lastColonistCount = GameBridge.GetAllColonists().Count;
            _lastFoodCount = 0;
            _lastResearchCompleted = DefDatabase<ResearchProjectDef>.AllDefsListForReading
                .Count(p => p.IsFinished);
            _lastLetterCount = Find.LetterStack?.LettersListForReading?.Count ?? 0;
        }

        /// <summary>
        /// Record a game event as it happens (called from Harmony patches).
        /// </summary>
        public static void RecordEvent(string type, string description, string severity = "info")
        {
            var evt = new GameEvent
            {
                Type = type,
                Description = description,
                Severity = severity,
                Tick = Find.TickManager.TicksGame,
                Timestamp = DateTime.Now
            };

            lock (_lock)
            {
                _allEvents.Add(evt);
                _pendingEvents.Enqueue(evt);
            }

            // Also push to chat as system notification
            string icon = severity == "critical" ? "[!]" : severity == "warning" ? "[*]" : "[i]";
            ChatManager.AddMessage("System", $"{icon} {description}");

            // Check auto-pause for critical events
            bool paused = AutoPauseManager.CheckAndPause(type, severity);
            if (paused)
                ChatManager.AddMessage("System", "[!] Game auto-paused — critical event detected");

            Log.Message($"[RimWorldMcp] Event: [{severity}] {description}");
        }

        /// <summary>
        /// Get all events since last check and clear the pending queue.
        /// </summary>
        public static List<GameEvent> GetPendingEvents()
        {
            var result = new List<GameEvent>();
            lock (_lock)
            {
                while (_pendingEvents.Count > 0)
                    result.Add(_pendingEvents.Dequeue());
            }
            return result;
        }

        /// <summary>
        /// Get all recorded events (for full history).
        /// </summary>
        public static List<GameEvent> GetAllEvents()
        {
            lock (_lock)
                return new List<GameEvent>(_allEvents);
        }

        /// <summary>
        /// Run periodic silent-change detection. Call this from a tick patch.
        /// </summary>
        public static void PeriodicCheck()
        {
            if (!GameBridge.IsGameReady()) return;

            int currentTick = Find.TickManager.TicksGame;
            // Check every 500 ticks (~8 seconds)
            if (currentTick - _lastCheckTick < 500) return;
            _lastCheckTick = currentTick;

            try
            {
                // Check colonist deaths
                var currentColonists = GameBridge.GetAllColonists();
                if (currentColonists.Count < _lastColonistCount)
                {
                    int died = _lastColonistCount - currentColonists.Count;
                    RecordEvent("colonist_died", $"{died} colonist(s) lost", "critical");
                }
                _lastColonistCount = currentColonists.Count;

                // Check food levels (approximate: count meal-type items)
                float food = 0;
                foreach (var thing in Find.CurrentMap.spawnedThings)
                {
                    if (thing.def.IsNutritionGivingIngestible || thing.def.ingestible != null)
                        food += thing.stackCount;
                }
                if (_lastFoodCount > 0 && food < 10 && _lastFoodCount >= 10)
                    RecordEvent("food_critical", "Food supply critically low", "warning");
                _lastFoodCount = food;

                // Check new research
                int completed = DefDatabase<ResearchProjectDef>.AllDefsListForReading
                    .Count(p => p.IsFinished);
                if (completed > _lastResearchCompleted)
                {
                    int newOnes = completed - _lastResearchCompleted;
                    RecordEvent("research_completed", $"{newOnes} research project(s) completed", "info");
                }
                _lastResearchCompleted = completed;

                // Check for new letters (raids, traders, quests, etc.) via LetterStack
                try
                {
                    var ls = Find.LetterStack;
                    if (ls != null)
                    {
                        List<Letter> letters = null;
                        try { letters = ls.LettersListForReading; } catch { }
                        if (letters != null && letters.Count > _lastLetterCount)
                        {
                            for (int i = _lastLetterCount; i < letters.Count; i++)
                            {
                                var let = letters[i];
                                if (let == null) continue;
                                string defName = let.def?.defName ?? "letter";
                                string label = let.Label.ToString();
                                string severity = "info";
                                if (defName.Contains("Death") || defName.Contains("Attack") || defName.Contains("Raid") || defName.Contains("Threat"))
                                    severity = "critical";
                                else if (defName.Contains("Threat") || defName.Contains("Danger"))
                                    severity = "warning";
                                RecordEvent(defName, label, severity);
                            }
                            _lastLetterCount = letters.Count;
                        }
                    }
                }
                catch { }

                // Track prisoner recruitment progress
                try
                {
                    foreach (var pawn in Find.CurrentMap.mapPawns.AllPawns)
                    {
                        if (pawn.guest == null || !pawn.guest.IsPrisoner || pawn.guest.HostFaction != Faction.OfPlayer)
                            continue;
                        string id = pawn.GetUniqueLoadID();
                        float resist = pawn.guest.resistance;
                        float recruitPct = 0;
                        try
                        {
                            var rpField = typeof(Pawn_GuestTracker).GetField("recruitProgress",
                                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                            if (rpField != null)
                            {
                                object rpVal = rpField.GetValue(pawn.guest);
                                recruitPct = Convert.ToSingle(rpVal);
                            }
                        }
                        catch { }

                        if (_lastPrisonerStates.TryGetValue(id, out var last))
                        {
                            // Resistance dropped (someone's been working them)
                            if (resist < last.resistance - 0.5f)
                                RecordEvent("prisoner_resistance", $"{pawn.Name.ToStringShort}: resistance {last.resistance:F1}→{resist:F1}", "info");
                            // Recruit progress increased
                            if (recruitPct > last.recruitProgress + 0.05f)
                                RecordEvent("prisoner_recruit_progress", $"{pawn.Name.ToStringShort}: recruiting! ({recruitPct*100f:F0}%)", "info");
                        }
                        _lastPrisonerStates[id] = (resist, recruitPct);
                    }

                    // Remove prisoners no longer present (recruited, released, or died)
                    var activeIds = new HashSet<string>();
                    foreach (var pawn in Find.CurrentMap.mapPawns.AllPawns)
                    {
                        if (pawn.guest != null && pawn.guest.IsPrisoner && pawn.guest.HostFaction == Faction.OfPlayer)
                            activeIds.Add(pawn.GetUniqueLoadID());
                    }
                    // Check for prisoners that disappeared since last tick
                    foreach (var id in _lastPrisonerStates.Keys.ToList())
                    {
                        if (!activeIds.Contains(id))
                        {
                            // Try to find name from our stored state
                            RecordEvent("prisoner_gone", $"Prisoner no longer in custody (recruited/released/executed)", "info");
                            _lastPrisonerStates.Remove(id);
                        }
                    }
                }
                catch { }

                // Check AlertsReadout for urgent alerts (starving, mental break, etc.)
                try
                {
                    var alerts = CheckAlertsForAutoPause();
                    // Clear seen set of alerts that are no longer active
                    var activeLabels = new HashSet<string>(alerts.Select(a => a.Label));
                    _seenAutoPauseAlerts.RemoveWhere(s => !activeLabels.Contains(s));
                    // Only trigger for NEW critical alerts
                    foreach (var alert in alerts)
                    {
                        if (!_seenAutoPauseAlerts.Contains(alert.Label))
                        {
                            _seenAutoPauseAlerts.Add(alert.Label);
                            RecordEvent($"alert_{alert.Type.Replace(' ', '_')}", alert.Label, "critical");
                        }
                    }
                }
                catch { }
            }
            catch (Exception ex)
            {
                // Don't let periodic check failures spam logs
            }
        }

        /// <summary>
        /// Serialize events to JSON.
        /// </summary>
        public static string EventsToJson(List<GameEvent> events)
        {
            var items = new List<string>();
            foreach (var evt in events)
            {
                items.Add("{" +
                    $"\"type\":\"{HttpServer.EscapeJson(evt.Type)}\"," +
                    $"\"description\":\"{HttpServer.EscapeJson(evt.Description)}\"," +
                    $"\"severity\":\"{HttpServer.EscapeJson(evt.Severity)}\"," +
                    $"\"tick\":{evt.Tick}," +
                    $"\"timestamp\":\"{evt.Timestamp:HH:mm:ss}\"" +
                    "}");
            }
            return "[" + string.Join(",", items) + "]";
        }

        /// <summary>
        /// Check AlertsReadout for alerts that should trigger auto-pause.
        /// Returns alerts with Critical or High priority.
        /// </summary>
        private static object _cachedAlertsReadout = null;
        private static Type _alertsReadoutType = null;

        public static List<(string Type, string Label)> CheckAlertsForAutoPause()
        {
            var result = new List<(string Type, string Label)>();
            try
            {
                // Guard: only check when map is fully loaded and game has had time to initialize
                if (Find.CurrentMap == null) return result;
                if (Find.TickManager.TicksGame < 3000) return result; // ~50 seconds grace period

                if (_alertsReadoutType == null)
                    _alertsReadoutType = typeof(AlertsReadout);

                if (_cachedAlertsReadout == null)
                    _cachedAlertsReadout = Activator.CreateInstance(_alertsReadoutType);

                if (_cachedAlertsReadout == null) return result;

                // Refresh alerts
                var updateMethod = _alertsReadoutType.GetMethod("AlertsReadoutUpdate",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Public);
                updateMethod?.Invoke(_cachedAlertsReadout, null);

                // Get current alerts
                var prop = _alertsReadoutType.GetProperty("CurrentAlerts",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic);
                if (prop == null) return result;

                var alertList = prop.GetValue(_cachedAlertsReadout) as System.Collections.IList;
                if (alertList == null || alertList.Count == 0) return result;

                foreach (var obj in alertList)
                {
                    if (obj == null) continue;
                    var alert = obj as Alert;
                    if (alert == null) continue;

                    try
                    {
                        var priority = alert.Priority;
                        if (priority == AlertPriority.Critical || priority == AlertPriority.High)
                        {
                            result.Add((alert.Label, alert.Label));
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return result;
        }
    }

    public class GameEvent
    {
        public string Type { get; set; }
        public string Description { get; set; }
        public string Severity { get; set; } // info, warning, critical
        public int Tick { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
