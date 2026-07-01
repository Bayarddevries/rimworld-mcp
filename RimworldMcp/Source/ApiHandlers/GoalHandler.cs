using System;
using System.Net;
using Verse;

namespace RimworldMcp
{
    /// <summary>
    /// HTTP handler for goal tracking API.
    /// </summary>
    public static class GoalHandler
    {
        /// <summary>
        /// GET /api/goals — list all active goals with progress.
        /// </summary>
        public static string ListGoals(HttpListenerRequest req)
        {
            return HttpServer.JsonSuccess(GoalManager.GoalsToJson());
        }

        /// <summary>
        /// POST /api/goals/set — create or update a goal.
        /// Body: {"id": "...", "description": "...", "type": "resource|research|colonists|wealth",
        ///        "target": 10000, "target_item": "steel"}
        /// </summary>
        public static string SetGoal(HttpListenerRequest req)
        {
            string body = HttpServer.ReadBody(req);
            var data = ParseSimpleJson(body);

            string id = GetValue(data, "id");
            string description = GetValue(data, "description");
            string type = GetValue(data, "type") ?? "custom";
            string targetStr = GetValue(data, "target");
            string targetItem = GetValue(data, "target_item");

            if (id == null)
                return HttpServer.JsonError("Missing field: id");
            if (description == null)
                description = id;

            if (!float.TryParse(targetStr, out float target))
                target = 1;

            GoalManager.SetGoal(id, description, type, target, targetItem);

            return HttpServer.JsonSuccess($"{{\"message\":\"Goal '{id}' set\",\"type\":\"{type}\",\"target\":{target}}}");
        }

        /// <summary>
        /// POST /api/goals/remove — remove a goal.
        /// Body: {"id": "..."}
        /// </summary>
        public static string RemoveGoal(HttpListenerRequest req)
        {
            string body = HttpServer.ReadBody(req);
            var data = ParseSimpleJson(body);

            string id = GetValue(data, "id");
            if (id == null)
                return HttpServer.JsonError("Missing field: id");

            GoalManager.RemoveGoal(id);
            return HttpServer.JsonSuccess($"{{\"message\":\"Goal '{id}' removed\"}}");
        }

        // -- Helpers --
        private static System.Collections.Generic.Dictionary<string, string> ParseSimpleJson(string json)
        {
            var dict = new System.Collections.Generic.Dictionary<string, string>();
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

        private static string GetValue(System.Collections.Generic.Dictionary<string, string> dict, string key)
        {
            dict.TryGetValue(key, out var val);
            return val;
        }
    }
}
