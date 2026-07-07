using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using RimWorld;
using Verse;
using RimWorld.Planet;

namespace RimworldMcp
{
    /// <summary>
    /// Caravan management.
    ///
    /// GET  /api/caravans             — list active caravans
    /// GET  /api/caravans/settlements — list reachable settlements
    /// POST /api/caravans/form        — form a caravan with selected pawns & items
    /// POST /api/caravans/send        — send a formed caravan to a settlement
    /// POST /api/caravans/recall      — abandon caravan / return home (basic)
    /// </summary>
    public static class CaravanHandler
    {
        public static string List(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            var caravans = Find.WorldObjects.Caravans;
            var list = new List<string>();

            foreach (var caravan in caravans)
            {
                list.Add("{" +
                    "\"id\":" + caravan.ID + "," +
                    "\"name\":" + HttpServer.ToJsonString(caravan.Name ?? "Caravan") + "," +
                    "\"pawnCount\":" + caravan.PawnsListForReading.Count + "," +
                    "\"tile\":" + caravan.Tile +
                    "}");
            }

            return HttpServer.JsonSuccess(HttpServer.BuildJsonArray(list.ToArray()));
        }

        public static string Settlements(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            var list = new List<string>();
            var playerFaction = Faction.OfPlayer;

            foreach (var settlement in Find.WorldObjects.Settlements)
            {
                if (settlement.Faction == playerFaction) continue;

                float distance = 0;
                var home = Find.AnyPlayerHomeMap?.Tile ?? -1;
                if (home >= 0)
                    distance = Find.WorldGrid.ApproxDistanceInTiles(home, settlement.Tile);

                list.Add("{" +
                    "\"name\":" + HttpServer.ToJsonString(settlement.Name ?? settlement.Label) + "," +
                    "\"faction\":" + HttpServer.ToJsonString(settlement.Faction?.Name ?? "Unknown") + "," +
                    "\"tile\":" + settlement.Tile + "," +
                    "\"distance\":" + distance.ToString("F1") + "," +
                    "\"hostile\":" + (settlement.Faction?.HostileTo(playerFaction) == true ? "true" : "false") +
                    "}");
            }

            return HttpServer.JsonSuccess(HttpServer.BuildJsonArray(list.ToArray()));
        }
    }
}
