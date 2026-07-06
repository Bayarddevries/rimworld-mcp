using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using RimWorld;
using Verse;

namespace RimworldMcp
{
    public static class BillsHandler
    {
        public static string List(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            var map = Find.CurrentMap;
            var bills = new List<string>();

            foreach (var thing in map.spawnedThings)
            {
                var workTable = thing as Building_WorkTable;
                if (workTable == null) continue;

                var billStack = workTable.BillStack;
                if (billStack == null || billStack.Count == 0) continue;

                foreach (var bill in billStack)
                {
                    bills.Add(HttpServer.BuildJsonObject(
                        ("building", HttpServer.ToJsonString(thing.LabelCap)),
                        ("bill", HttpServer.ToJsonString(bill.LabelCap)),
                        ("recipe", HttpServer.ToJsonString(bill.recipe?.label ?? "Unknown")),
                        ("suspended", bill.suspended ? "true" : "false")
                    ));
                }
            }

            return HttpServer.JsonSuccess(HttpServer.BuildJsonArray(bills.ToArray()));
        }

        public static string AddBill(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            string body = HttpServer.ReadBody(req);
            var data = ParseSimpleJson(body);

            string recipeName = GetValue(data, "recipe");
            string buildingName = GetValue(data, "building");

            if (recipeName == null)
                return HttpServer.JsonError("Missing field: recipe");

            // Find recipe
            RecipeDef recipe = DefDatabase<RecipeDef>.AllDefsListForReading
                .FirstOrDefault(r => r.label.ToLower() == recipeName.ToLower() ||
                                     r.defName.ToLower() == recipeName.ToLower() ||
                                     r.label?.ToLower().Contains(recipeName.ToLower()) == true);

            if (recipe == null)
                return HttpServer.JsonError($"Recipe not found: {recipeName}");

            // Find target work table
            Building_WorkTable workTable = null;
            if (buildingName != null)
            {
                foreach (var thing in Find.CurrentMap.spawnedThings)
                {
                    if (thing is Building_WorkTable wt &&
                        (thing.LabelCap.ToLower().Contains(buildingName.ToLower()) ||
                         thing.def.defName.ToLower().Contains(buildingName.ToLower())))
                    {
                        workTable = wt;
                        break;
                    }
                }
            }

            if (workTable == null)
            {
                // Try to find any work table that can use this recipe
                foreach (var thing in Find.CurrentMap.spawnedThings)
                {
                    if (thing is Building_WorkTable wt)
                    {
                        workTable = wt;
                        break;
                    }
                }
            }

            if (workTable == null)
                return HttpServer.JsonError($"No work table found for recipe: {recipe.label}");

            // Add bill using Bill_Production
            var bill = new Bill_Production(recipe);
            workTable.BillStack.AddBill(bill);

            return HttpServer.JsonSuccess($"{{\"message\":\"Added bill '{recipe.label}' to {workTable.LabelCap}\"}}");
        }

        // ─── Remove Bill ───
        public static string RemoveBill(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            string body = HttpServer.ReadBody(req);
            var data = ParseSimpleJson(body);
            string buildingName = GetValue(data, "building");
            string recipeName = GetValue(data, "recipe");

            if (buildingName == null || recipeName == null)
                return HttpServer.JsonError("Missing fields: building, recipe");

            foreach (var thing in Find.CurrentMap.spawnedThings)
            {
                var wt = thing as Building_WorkTable;
                if (wt == null) continue;
                if (!thing.LabelCap.ToString().ToLower().Contains(buildingName.ToLower())
                    && !thing.def.defName.ToLower().Contains(buildingName.ToLower()))
                    continue;

                for (int i = wt.BillStack.Count - 1; i >= 0; i--)
                {
                    var bill = wt.BillStack[i];
                    if (bill.recipe?.label.ToLower().Contains(recipeName.ToLower()) == true
                        || bill.recipe?.defName.ToLower().Contains(recipeName.ToLower()) == true)
                    {
                        wt.BillStack.Delete(bill);
                        return HttpServer.JsonSuccess($"{{\"message\":\"Removed '{bill.LabelCap}' from {thing.LabelCap}\"}}");
                    }
                }
            }

            return HttpServer.JsonError($"No matching bill found: {recipeName} at {buildingName}");
        }

        // ─── Suspend/Resume Bill ───
        public static string SuspendBill(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            string body = HttpServer.ReadBody(req);
            var data = ParseSimpleJson(body);
            string buildingName = GetValue(data, "building");
            string recipeName = GetValue(data, "recipe");
            string suspendStr = GetValue(data, "suspend");

            if (buildingName == null || recipeName == null)
                return HttpServer.JsonError("Missing fields: building, recipe");

            bool suspend = true;
            if (suspendStr != null) bool.TryParse(suspendStr, out suspend);

            foreach (var thing in Find.CurrentMap.spawnedThings)
            {
                var wt = thing as Building_WorkTable;
                if (wt == null) continue;
                if (!thing.LabelCap.ToString().ToLower().Contains(buildingName.ToLower())
                    && !thing.def.defName.ToLower().Contains(buildingName.ToLower()))
                    continue;

                foreach (var bill in wt.BillStack)
                {
                    if (bill.recipe?.label.ToLower().Contains(recipeName.ToLower()) == true
                        || bill.recipe?.defName.ToLower().Contains(recipeName.ToLower()) == true)
                    {
                        bill.suspended = suspend;
                        return HttpServer.JsonSuccess($"{{\"message\":\"{(suspend ? "Suspended" : "Resumed")} '{bill.LabelCap}'\"}}");
                    }
                }
            }

            return HttpServer.JsonError($"No matching bill found: {recipeName} at {buildingName}");
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
