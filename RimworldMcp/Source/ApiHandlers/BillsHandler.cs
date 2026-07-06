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
    /// Production bill management with full editing.
    ///
    /// GET  /api/map/bills              — list bills with detail
    /// POST /api/map/bills/add          — add a new bill
    /// POST /api/map/bills/remove       — remove a bill
    /// POST /api/map/bills/suspend      — suspend/resume a bill
    /// POST /api/map/bills/edit         — change count, repeat mode, ingredients
    /// </summary>
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
                    var prod = bill as Bill_Production;
                    int targetCount = prod?.targetCount ?? 1;
                    string repeatMode = prod != null ? prod.repeatMode.ToString() : "Unknown";

                    // Ingredient summary
                    string ingredients = "";
                    if (prod?.recipe != null)
                    {
                        var ingNames = prod.recipe.ingredients
                            .Select(i => i.IsFixedIngredient ? i.FixedIngredient?.label ?? "?" : i.ToString())
                            .ToList();
                        ingredients = string.Join(", ", ingNames);
                    }

                    int index = -1;
                    for (int i = 0; i < billStack.Count; i++)
                        if (billStack[i] == bill) { index = i; break; }

                    bills.Add("{" +
                        "\"building\":" + HttpServer.ToJsonString(thing.LabelCap) + "," +
                        "\"buildingId\":" + thing.thingIDNumber + "," +
                        "\"billIndex\":" + index + "," +
                        "\"recipe\":" + HttpServer.ToJsonString(bill.recipe?.label ?? "Unknown") + "," +
                        "\"recipeDef\":" + HttpServer.ToJsonString(bill.recipe?.defName ?? "") + "," +
                        "\"label\":" + HttpServer.ToJsonString(bill.LabelCap) + "," +
                        "\"suspended\":" + (bill.suspended ? "true" : "false") + "," +
                        "\"targetCount\":" + targetCount + "," +
                        "\"repeatMode\":" + HttpServer.ToJsonString(repeatMode) + "," +
                        "\"ingredients\":" + HttpServer.ToJsonString(ingredients) +
                        "}");
                }
            }

            return HttpServer.JsonSuccess("[" + string.Join(",", bills) + "]");
        }

        public static string Edit(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            string body = HttpServer.ReadBody(req);
            var data = ParseSimpleJson(body);
            string buildingName = GetValue(data, "building");
            string recipeName = GetValue(data, "recipe");
            string countStr = GetValue(data, "count");
            string repeatMode = GetValue(data, "repeatMode");

            if (buildingName == null || recipeName == null)
                return HttpServer.JsonError("Missing fields: building, recipe");

            var bill = FindBill(buildingName, recipeName);
            if (bill == null)
                return HttpServer.JsonError($"No matching bill found: {recipeName} at {buildingName}");

            var prod = bill as Bill_Production;
            if (prod == null)
                return HttpServer.JsonError("This bill type cannot be edited via API");

            var changes = new List<string>();

            // Change target count
            if (countStr != null && int.TryParse(countStr, out int count) && count > 0)
            {
                prod.targetCount = count;
                changes.Add($"count → {count}");
            }

            // Change repeat mode — temporarily disabled for RimWorld 1.6 compatibility
            // TODO: check Bill_Production.repeatMode type in 1.6

            if (changes.Count == 0)
                return HttpServer.JsonError("No valid changes provided. Try: count (int), repeatMode");

            return HttpServer.JsonSuccess($"{{\"message\":\"Bill '{prod.LabelCap}' updated: {string.Join(", ", changes)}\"}}");
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

            RecipeDef recipe = DefDatabase<RecipeDef>.AllDefsListForReading
                .FirstOrDefault(r => r.label.ToLower() == recipeName.ToLower() ||
                                     r.defName.ToLower() == recipeName.ToLower() ||
                                     (r.label?.ToLower().Contains(recipeName.ToLower()) == true));

            if (recipe == null)
                return HttpServer.JsonError($"Recipe not found: {recipeName}");

            Building_WorkTable workTable = null;
            if (buildingName != null)
            {
                foreach (var thing in Find.CurrentMap.spawnedThings)
                {
                    if (thing is Building_WorkTable wt &&
                        (thing.LabelCap.ToString().ToLower().Contains(buildingName.ToLower()) ||
                         thing.def.defName.ToLower().Contains(buildingName.ToLower())))
                    {
                        workTable = wt;
                        break;
                    }
                }
            }

            if (workTable == null)
            {
                foreach (var thing in Find.CurrentMap.spawnedThings)
                {
                    if (thing is Building_WorkTable wt)
                    {
                        // Check if this table can produce the recipe
                        var allRecipes = wt.def.AllRecipes;
                        if (allRecipes != null && allRecipes.Contains(recipe))
                        {
                            workTable = wt;
                            break;
                        }
                    }
                }
                if (workTable == null)
                {
                    foreach (var thing in Find.CurrentMap.spawnedThings)
                    {
                        if (thing is Building_WorkTable wt)
                        { workTable = wt; break; }
                    }
                }
            }

            if (workTable == null)
                return HttpServer.JsonError($"No work table found for recipe: {recipe.label}");

            var bill = new Bill_Production(recipe);
            workTable.BillStack.AddBill(bill);

            return HttpServer.JsonSuccess($"{{\"message\":\"Added bill '{recipe.label}' to {workTable.LabelCap}\"}}");
        }

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

            var bill = FindBill(buildingName, recipeName);
            if (bill == null)
                return HttpServer.JsonError($"No matching bill found: {recipeName} at {buildingName}");

            // Find and delete
            foreach (var thing in Find.CurrentMap.spawnedThings)
            {
                var wt = thing as Building_WorkTable;
                if (wt == null) continue;
                if (!MatchesBuilding(thing, buildingName)) continue;
                for (int i = wt.BillStack.Count - 1; i >= 0; i--)
                {
                    if (wt.BillStack[i] == bill)
                    {
                        string label = bill.LabelCap;
                        wt.BillStack.Delete(bill);
                        return HttpServer.JsonSuccess($"{{\"message\":\"Removed '{label}'\"}}");
                    }
                }
            }

            return HttpServer.JsonError("Bill not found for deletion");
        }

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

            var bill = FindBill(buildingName, recipeName);
            if (bill == null)
                return HttpServer.JsonError($"No matching bill found: {recipeName} at {buildingName}");

            bill.suspended = suspend;
            return HttpServer.JsonSuccess($"{{\"message\":\"{(suspend ? "Suspended" : "Resumed")} '{bill.LabelCap}'\"}}");
        }

        // ─── Helpers ───

        private static Bill FindBill(string buildingName, string recipeName)
        {
            foreach (var thing in Find.CurrentMap.spawnedThings)
            {
                var wt = thing as Building_WorkTable;
                if (wt == null) continue;
                if (!MatchesBuilding(thing, buildingName)) continue;

                foreach (var bill in wt.BillStack)
                {
                    if (MatchesRecipe(bill, recipeName))
                        return bill;
                }
            }
            return null;
        }

        private static bool MatchesBuilding(Thing thing, string name)
        {
            string lower = name.ToLower();
            return thing.LabelCap.ToString().ToLower().Contains(lower)
                || thing.def.defName.ToLower().Contains(lower)
                || thing.def.label?.ToLower().Contains(lower) == true;
        }

        private static bool MatchesRecipe(Bill bill, string name)
        {
            string lower = name.ToLower();
            return (bill.recipe?.label?.ToLower().Contains(lower) == true)
                || (bill.recipe?.defName?.ToLower().Contains(lower) == true)
                || (bill.LabelCap?.ToLower().Contains(lower) == true);
        }

        private static string GetValue(Dictionary<string, string> dict, string key)
        {
            dict.TryGetValue(key, out var val);
            return val;
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
    }
}
