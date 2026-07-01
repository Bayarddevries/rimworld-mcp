using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using RimWorld;
using Verse;

namespace RimworldMcp
{
    public static class ResearchHandler
    {
        public static string List(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            var researchList = new List<string>();

            foreach (var projDef in DefDatabase<ResearchProjectDef>.AllDefsListForReading)
            {
                bool isFinished = projDef.IsFinished;
                bool isAvailable = projDef.PrerequisitesCompleted;
                float progress = Find.ResearchManager?.GetProgress(projDef) ?? 0;
                float cost = projDef.baseCost;

                researchList.Add(HttpServer.BuildJsonObject(
                    ("id", HttpServer.ToJsonString(projDef.defName)),
                    ("label", HttpServer.ToJsonString(projDef.label)),
                    ("cost", cost.ToString("F0")),
                    ("progress", progress.ToString("F0")),
                    ("progress_pct", (progress / Math.Max(cost, 1)).ToString("F2")),
                    ("is_finished", isFinished ? "true" : "false"),
                    ("is_available", isAvailable ? "true" : "false"),
                    ("description", HttpServer.ToJsonString(projDef.description?.Truncate(200) ?? ""))
                ));
            }

            return HttpServer.JsonSuccess(HttpServer.BuildJsonArray(researchList.ToArray()));
        }

        public static string Unlock(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            string body = HttpServer.ReadBody(req);
            var data = ParseSimpleJson(body);

            string techId = GetValue(data, "tech");
            string action = GetValue(data, "action") ?? "unlock";

            if (techId == null)
                return HttpServer.JsonError("Missing field: tech");

            ResearchProjectDef project = DefDatabase<ResearchProjectDef>.AllDefsListForReading
                .FirstOrDefault(r => r.defName.ToLower() == techId.ToLower() ||
                                     r.label.ToLower() == techId.ToLower());

            if (project == null)
                return HttpServer.JsonError($"Research project not found: {techId}");

            if (action == "unlock" || action == "complete")
            {
                // Complete instantly
                Find.ResearchManager.FinishProject(project);
                return HttpServer.JsonSuccess($"{{\"message\":\"Completed research: {project.label}\"}}");
        }

        return HttpServer.JsonError("Unknown action. Use 'unlock' or 'complete'.");
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
