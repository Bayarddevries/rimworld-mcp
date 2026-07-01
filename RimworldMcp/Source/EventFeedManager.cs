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

        public static void Init()
        {
            _lastCheckTick = Find.TickManager.TicksGame;
            _lastColonistCount = GameBridge.GetAllColonists().Count;
            _lastFoodCount = 0;
            _lastResearchCompleted = DefDatabase<ResearchProjectDef>.AllDefsListForReading
                .Count(p => p.IsFinished);
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
