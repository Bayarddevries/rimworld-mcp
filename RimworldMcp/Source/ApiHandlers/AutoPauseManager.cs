using System;
using System.Collections.Generic;
using System.Net;
using Verse;

namespace RimworldMcp
{
    /// <summary>
    /// Manages auto-pause on critical game events.
    /// When enabled, the game pauses automatically on raid, death, manhunter, etc.
    /// Hermes can toggle this remotely.
    /// 
    /// GET  /api/colony/autopause — returns current config
    /// POST /api/colony/autopause — set config { "enabled": true/false }
    /// </summary>
    public static class AutoPauseManager
    {
        private static bool _enabled = true;
        private static readonly object _lock = new object();

        // Event types that trigger auto-pause (matched against EventFeedManager event types)
        private static readonly HashSet<string> CriticalEventTypes = new HashSet<string>
        {
            "RaidEnemy",
            "ManhunterPack",
            "MechCluster",
            "Infestation",
            "colonist_died",
            "ColonistDied",
        };

        public static bool IsEnabled()
        {
            lock (_lock) return _enabled;
        }

        /// <summary>
        /// Check if an event should trigger auto-pause, and pause if so.
        /// Called from EventFeedManager.RecordEvent.
        /// </summary>
        public static bool CheckAndPause(string eventType, string severity)
        {
            if (!IsEnabled()) return false;
            if (severity != "critical") return false;
            if (!CriticalEventTypes.Contains(eventType)) return false;

            try
            {
                if (Find.TickManager != null && Find.TickManager.CurTimeSpeed != TimeSpeed.Paused)
                {
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    Log.Message($"[RimWorldMcp] Auto-paused on event: {eventType}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorldMcp] Auto-pause error: {ex.Message}");
            }
            return false;
        }

        // --- API Handlers ---

        public static string GetConfig(HttpListenerRequest req)
        {
            return HttpServer.JsonSuccess(HttpServer.BuildJsonObject(
                ("enabled", IsEnabled() ? "true" : "false"),
                ("critical_events", HttpServer.ToJsonString(string.Join(", ", CriticalEventTypes)))
            ));
        }

        public static string SetConfig(HttpListenerRequest req)
        {
            string body = HttpServer.ReadBody(req);
            var data = ParseSimpleJson(body);
            string enabledStr = GetValue(data, "enabled");

            if (enabledStr == null)
                return HttpServer.JsonError("Missing field: enabled");

            if (bool.TryParse(enabledStr, out bool enabled))
            {
                lock (_lock) _enabled = enabled;
                return HttpServer.JsonSuccess(HttpServer.BuildJsonObject(
                    ("enabled", IsEnabled() ? "true" : "false"),
                    ("message", HttpServer.ToJsonString(enabled ? "Auto-pause enabled" : "Auto-pause disabled"))
                ));
            }

            return HttpServer.JsonError("enabled must be true or false");
        }

        // --- Helpers ---

        private static Dictionary<string, string> ParseSimpleJson(string json)
        {
            var dict = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(json)) return dict;
            json = json.Trim();
            if (json.StartsWith("{")) json = json.Substring(1);
            if (json.EndsWith("}")) json = json.Substring(0, json.Length - 1);
            bool inString = false, escape = false;
            string currentKey = null;
            var sb = new System.Text.StringBuilder();
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
