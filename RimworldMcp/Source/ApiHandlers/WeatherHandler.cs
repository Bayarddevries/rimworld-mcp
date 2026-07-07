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
    /// Weather and environment control.
    ///
    /// GET  /api/weather              — current weather info + available weather defs
    /// POST /api/weather              — force a weather type
    /// POST /api/weather/event        — trigger a weather event (eclipse, toxic fallout, etc.)
    /// </summary>
    public static class WeatherHandler
    {
        public static string Get(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            var map = Find.CurrentMap;
            var curWeather = map.weatherManager?.curWeather;

            var weatherList = new List<string>();
            foreach (var w in DefDatabase<WeatherDef>.AllDefsListForReading)
            {
                weatherList.Add("{" +
                    "\"defName\":" + HttpServer.ToJsonString(w.defName) + "," +
                    "\"label\":" + HttpServer.ToJsonString(w.label ?? w.defName) +
                    "}");
            }

            return HttpServer.JsonSuccess("{" +
                "\"current\":" + HttpServer.ToJsonString(curWeather?.label ?? curWeather?.defName ?? "None") + "," +
                "\"temperature\":" + map.mapTemperature.OutdoorTemp + "," +
                "\"season\":" + HttpServer.ToJsonString(SeasonUtility.GetReportedSeason(map.Tile, Find.TickManager.TicksGame).ToString()) + "," +
                "\"weatherDefs\":" + (weatherList.Count > 0 ? "[" + string.Join(",", weatherList) + "]" : "[]") +
                "}");
        }

        public static string Set(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            string body = HttpServer.ReadBody(req);
            var data = ParseSimpleJson(body);
            string weather = GetValue(data, "weather");

            if (weather == null)
                return HttpServer.JsonError("Missing field: weather");

            var def = DefDatabase<WeatherDef>.GetNamedSilentFail(weather);
            if (def == null)
            {
                // Try matching by label
                def = DefDatabase<WeatherDef>.AllDefsListForReading
                    .FirstOrDefault(w => (w.label ?? "").ToLower().Contains(weather.ToLower()));
            }

            if (def == null)
                return HttpServer.JsonError($"Weather not found: {weather}. Use: Clear, Rain, Fog, Snow, etc.");

            Find.CurrentMap.weatherManager?.TransitionTo(def);

            return HttpServer.JsonSuccess($"{{\"message\":\"Weather changed to {def.label}\"}}");
        }

        public static string SetEvent(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            string body = HttpServer.ReadBody(req);
            var data = ParseSimpleJson(body);
            string eventName = GetValue(data, "event");

            if (eventName == null)
                return HttpServer.JsonError("Missing field: event");

            // Find matching incident def
            string lower = eventName.ToLower();
            var incident = DefDatabase<IncidentDef>.AllDefsListForReading
                .FirstOrDefault(i => i.defName.ToLower().Contains(lower) ||
                                     (i.label ?? "").ToLower().Contains(lower) ||
                                     (i.category?.defName?.ToLower().Contains(lower) ?? false));

            if (incident == null)
                return HttpServer.JsonError($"Event not found: {eventName}. Try: eclipse, toxic_fallout, flashstorm, thunderstorm, aurora");

            // Queue the incident
            var parms = StorytellerUtility.DefaultParmsNow(incident.category, Find.CurrentMap);
            parms.target = Find.CurrentMap;
            parms.points = StorytellerUtility.DefaultThreatPointsNow(Find.CurrentMap);
            Find.Storyteller.incidentQueue.Add(incident, Find.TickManager.TicksGame + 60); // 1 second delay

            return HttpServer.JsonSuccess($"{{\"message\":\"Queued {incident.label ?? incident.defName} — will trigger shortly\"}}");
        }

        private static string GetValue(Dictionary<string, string> dict, string key)
        {
            dict.TryGetValue(key, out var val);
            return val;
        }

        private static Dictionary<string, string> ParseSimpleJson(string json)
        {
            var result = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(json)) return result;
            int i = 0;
            while (i < json.Length)
            {
                int keyStart = json.IndexOf('"', i);
                if (keyStart < 0) break;
                int keyEnd = json.IndexOf('"', keyStart + 1);
                if (keyEnd < 0) break;
                string key = json.Substring(keyStart + 1, keyEnd - keyStart - 1);
                int colon = json.IndexOf(':', keyEnd + 1);
                if (colon < 0) break;
                int valStart = colon + 1;
                while (valStart < json.Length && json[valStart] == ' ') valStart++;
                if (valStart >= json.Length) break;
                string val;
                if (json[valStart] == '"')
                {
                    int valEnd = json.IndexOf('"', valStart + 1);
                    if (valEnd < 0) break;
                    val = json.Substring(valStart + 1, valEnd - valStart - 1);
                    i = valEnd + 1;
                }
                else
                {
                    int valEnd = json.IndexOfAny(new[] { ',', '}' }, valStart);
                    if (valEnd < 0) valEnd = json.Length;
                    val = json.Substring(valStart, valEnd - valStart).Trim();
                    i = valEnd;
                }
                result[key] = val;
                if (json[i] == '}') break;
                i++;
            }
            return result;
        }
    }
}
