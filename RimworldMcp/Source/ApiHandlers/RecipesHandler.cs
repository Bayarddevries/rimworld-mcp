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
    /// All recipes browser.
    ///
    /// GET  /api/recipes              — list all recipes with workbench, ingredients, work time
    /// GET  /api/recipes/{bench}      — filter by workbench name
    /// </summary>
    public static class RecipesHandler
    {
        public static string List(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            string path = req.Url.AbsolutePath.ToLowerInvariant();
            string filterBench = HttpServer.ExtractIdFromPath(path, "/api/recipes/");

            var list = new List<string>();
            var allRecipes = DefDatabase<RecipeDef>.AllDefsListForReading;

            foreach (var recipe in allRecipes)
            {
                // Skip non-production recipes
                if (recipe.products == null || recipe.products.Count == 0) continue;
                if (recipe.recipeUsers == null || recipe.recipeUsers.Count == 0) continue;

                // Get first workbench
                var bench = recipe.recipeUsers.FirstOrDefault();
                if (bench == null) continue;

                // Filter by bench name if specified
                if (!string.IsNullOrEmpty(filterBench))
                {
                    string lower = filterBench.ToLower();
                    if (!bench.defName.ToLower().Contains(lower) && !(bench.label?.ToLower().Contains(lower) ?? false))
                        continue;
                }

                // Product info
                var prod = recipe.products[0];
                string productName = prod.thingDef?.label ?? prod.thingDef?.defName ?? "Unknown";
                int productCount = prod.count;

                // Ingredients
                var ingredients = new List<string>();
                foreach (var ing in recipe.ingredients)
                {
                    if (ing.IsFixedIngredient && ing.FixedIngredient != null)
                        ingredients.Add(ing.FixedIngredient.label ?? ing.FixedIngredient.defName);
                    else
                        ingredients.Add(ing.ToString());
                }

                // Skills required
                string requiredSkill = "";
                int skillLevel = 0;
                try { var ws = recipe.GetType().GetField("requiredSkill", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.GetValue(recipe) as SkillDef; if (ws != null) requiredSkill = ws.label ?? ws.defName; } catch { }
                try { var sl = recipe.GetType().GetField("requiredSkillLevel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.GetValue(recipe); if (sl != null) skillLevel = (int)sl; } catch { }
                float workTime = recipe.workAmount / 400f; // Convert to approx seconds at 100% speed

                list.Add("{" +
                    "\"defName\":" + HttpServer.ToJsonString(recipe.defName) + "," +
                    "\"label\":" + HttpServer.ToJsonString(recipe.label ?? recipe.defName) + "," +
                    "\"product\":" + HttpServer.ToJsonString(productName) + "," +
                    "\"productCount\":" + productCount + "," +
                    "\"workBench\":" + HttpServer.ToJsonString(bench.label ?? bench.defName) + "," +
                    "\"workBenchDef\":" + HttpServer.ToJsonString(bench.defName) + "," +
                    "\"ingredients\":" + HttpServer.ToJsonString(string.Join(", ", ingredients)) + "," +
                    "\"workTime\":" + workTime.ToString("F1") + "," +
                    "\"requiredSkill\":" + HttpServer.ToJsonString(requiredSkill) + "," +
                    "\"skillLevel\":" + skillLevel +
                    "}");
            }

            return HttpServer.JsonSuccess("[" + string.Join(",", list) + "]");
        }
    }
}
