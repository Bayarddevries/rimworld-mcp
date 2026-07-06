using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using RimWorld;
using Verse;

namespace RimworldMcp
{
    public static class ZonesHandler
    {
        public static string List(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            var zones = new List<string>();
            foreach (var zone in Find.CurrentMap.zoneManager.AllZones)
            {
                var sz = zone as Zone_Stockpile;
                if (sz == null) continue;

                int cellCount = sz.Cells.Count;
                string label = sz.label ?? "Unnamed";

                zones.Add(HttpServer.BuildJsonObject(
                    ("id", HttpServer.ToJsonString(sz.GetUniqueLoadID())),
                    ("label", HttpServer.ToJsonString(label)),
                    ("cells", cellCount.ToString()),
                    ("x", sz.Position.x.ToString()),
                    ("z", sz.Position.z.ToString())
                ));
            }

            return HttpServer.JsonSuccess(HttpServer.BuildJsonArray(zones.ToArray()));
        }
    }
}
