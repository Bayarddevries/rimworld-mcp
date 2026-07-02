using System.Collections.Generic;
using System.Net;
using RimWorld;
using Verse;

namespace RimworldMcp
{
    /// <summary>
    /// Handles time control: pause, play, speed changes.
    /// POST /api/colony/time { "action": "pause" | "play" | "fast" | "superfast" | "ultrafast" }
    /// GET  /api/colony/paused  — returns current pause state + speed
    /// </summary>
    public static class TimeHandler
    {
        public static string SetTime(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            string body = HttpServer.ReadBody(req);
            var data = ParseSimpleJson(body);
            string action = GetValue(data, "action");

            if (action == null)
                return HttpServer.JsonError("Missing field: action");

            TickManager tick = Find.TickManager;

            switch (action.ToLower())
            {
                case "pause":
                    tick.CurTimeSpeed = TimeSpeed.Paused;
                    return HttpServer.JsonSuccess("{\"message\":\"Game paused\",\"paused\":true,\"speed\":0}");

                case "play":
                    tick.CurTimeSpeed = TimeSpeed.Normal;
                    return HttpServer.JsonSuccess("{\"message\":\"Game resumed at normal speed\",\"paused\":false,\"speed\":1}");

                case "fast":
                    tick.CurTimeSpeed = TimeSpeed.Fast;
                    return HttpServer.JsonSuccess("{\"message\":\"Speed set to fast (2x)\",\"paused\":false,\"speed\":2}");

                case "superfast":
                    tick.CurTimeSpeed = TimeSpeed.Superfast;
                    return HttpServer.JsonSuccess("{\"message\":\"Speed set to superfast (3x)\",\"paused\":false,\"speed\":3}");

                case "ultrafast":
                    tick.CurTimeSpeed = TimeSpeed.Ultrafast;
                    return HttpServer.JsonSuccess("{\"message\":\"Speed set to ultrafast (4x)\",\"paused\":false,\"speed\":4}");

                default:
                    return HttpServer.JsonError(string.Format("Unknown action: {0}. Use: pause, play, fast, superfast, ultrafast", action));
            }
        }

        public static string GetPauseState(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            TickManager tick = Find.TickManager;
            bool paused = tick.CurTimeSpeed == TimeSpeed.Paused;
            int speed = (int)tick.CurTimeSpeed;

            string speedName = "unknown";
            if (speed == 0) speedName = "paused";
            else if (speed == 1) speedName = "normal";
            else if (speed == 2) speedName = "fast";
            else if (speed == 3) speedName = "superfast";
            else if (speed == 4) speedName = "ultrafast";

            return HttpServer.JsonSuccess(HttpServer.BuildJsonObject(
                ("paused", paused ? "true" : "false"),
                ("speed", speed.ToString()),
                ("speed_name", HttpServer.ToJsonString(speedName)),
                ("tick", tick.TicksGame.ToString())
            ));
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
