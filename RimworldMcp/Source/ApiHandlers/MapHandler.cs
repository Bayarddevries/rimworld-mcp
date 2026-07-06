using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using RimWorld;
using Verse;

namespace RimworldMcp
{
    public static class MapHandler
    {
        public static string Get(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            var map = Find.CurrentMap;

            return HttpServer.JsonSuccess(HttpServer.BuildJsonObject(
                ("biome", HttpServer.ToJsonString(map.Biome?.label ?? "Unknown")),
                ("biome_description", HttpServer.ToJsonString(map.Biome?.description?.Truncate(200) ?? "")),
                ("tiles", (map.Size.x * map.Size.z).ToString()),
                ("size_x", map.Size.x.ToString()),
                ("size_z", map.Size.z.ToString()),
                ("season", HttpServer.ToJsonString(GenLocalDate.Season(map).ToString())),
                ("year", GenLocalDate.Year(map).ToString()),
                ("day", GenLocalDate.DayOfSeason(map).ToString()),
                ("hour", GenLocalDate.HourInteger(map).ToString()),
                ("weather", HttpServer.ToJsonString(map.weatherManager?.curWeather?.label ?? "Unknown")),
                ("temperature", map.mapTemperature?.OutdoorTemp.ToString("F1") ?? "0")
            ));
        }
    }

    public static class HealthHandler
    {
        public static string Check(HttpListenerRequest req)
        {
            bool ready = GameBridge.IsGameReady();
            return HttpServer.JsonSuccess(HttpServer.BuildJsonObject(
                ("status", HttpServer.ToJsonString(ready ? "ok" : "no_game")),
                ("game_loaded", ready ? "true" : "false"),
                ("service", HttpServer.ToJsonString("RimWorld MCP Bridge"))
            ));
        }
    }

    public static class SaveHandler
    {
        public static string Save(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            string body = HttpServer.ReadBody(req);
            var data = ParseSimpleJson(body);
            string slotName = GetValue(data, "slot") ?? "mcp_autosave";

            try
            {
                // Save the game
                string fileName = slotName + ".mcp";
                GameDataSaveLoader.SaveGame(fileName);

                // Also do an autosave
                Find.Autosaver?.DoAutosave();

                return HttpServer.JsonSuccess($"{{\"message\":\"Game saved as {fileName}\",\"slot\":{HttpServer.ToJsonString(slotName)},\"timestamp\":{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}}}");
            }
            catch (Exception ex)
            {
                return HttpServer.JsonError($"Save failed: {ex.Message}");
            }
        }

        // -- Helpers --
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
