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
    /// DLC support — Ideology and Biotech.
    ///
    /// GET  /api/dlc/ideology         — ideo info, memes, rituals
    /// GET  /api/dlc/genes            — xenotype info for all colonists
    /// GET  /api/dlc/xenotypes        — available xenotype list
    /// </summary>
    public static class DlcHandler
    {
        public static string Ideology(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            if (!ModsConfig.IdeologyActive)
                return HttpServer.JsonSuccess("{\"ideology_active\":false,\"message\":\"Ideology DLC not active\"}");

            // Get player ideo
            var playerIdeo = Find.IdeoManager?.IdeosListForReading?.FirstOrDefault();
            if (playerIdeo == null)
                return HttpServer.JsonSuccess("{\"ideology_active\":true,\"message\":\"No player ideology found\"}");

            // Memes
            var memeList = new List<string>();
            foreach (var meme in playerIdeo.memes)
            {
                memeList.Add("{" +
                    "\"label\":" + HttpServer.ToJsonString(meme.label ?? meme.defName) + "," +
                    "\"defName\":" + HttpServer.ToJsonString(meme.defName) +
                    "}");
            }

            // Precepts
            var preceptList = new List<string>();
            var precepts = playerIdeo.GetType().GetField("precepts", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)?.GetValue(playerIdeo) as System.Collections.IEnumerable;
            if (precepts != null)
            {
                foreach (var precept in precepts)
                {
                    var pDef = precept.GetType().GetProperty("def")?.GetValue(precept) as PreceptDef;
                    if (pDef != null)
                    {
                        preceptList.Add("{" +
                            "\"label\":" + HttpServer.ToJsonString(pDef.label ?? pDef.defName) + "," +
                            "\"impact\":" + HttpServer.ToJsonString(pDef.impact.ToString()) +
                            "}");
                    }
                }
            }

            return HttpServer.JsonSuccess("{" +
                "\"ideology_active\":true," +
                "\"name\":" + HttpServer.ToJsonString(playerIdeo.name ?? "Unnamed") + "," +
                "\"memes\":" + (memeList.Count > 0 ? "[" + string.Join(",", memeList) + "]" : "[]") + "," +
                "\"precepts\":" + (preceptList.Count > 0 ? "[" + string.Join(",", preceptList) + "]" : "[]") +
                "}");
        }

        public static string Genes(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            if (!ModsConfig.BiotechActive)
                return HttpServer.JsonError("Biotech DLC not active");

            var list = new List<string>();
            var colonists = GameBridge.GetAllColonists();

            foreach (var pawn in colonists)
            {
                var xeno = pawn.genes?.Xenotype;
                var geneList = new List<string>();

                if (pawn.genes?.GenesListForReading != null)
                {
                    foreach (var gene in pawn.genes.GenesListForReading)
                    {
                        if (gene.def != null)
                        {
                            geneList.Add("{" +
                                "\"label\":" + HttpServer.ToJsonString(gene.def.label ?? gene.def.defName) + "," +
                                "\"defName\":" + HttpServer.ToJsonString(gene.def.defName) + "," +
                                "\"type\":" + (gene.def.biostatCpx > 0 ? "germline" : gene.def.biostatArc > 0 ? "xenogene" : "endogene") +
                                "}");
                        }
                    }
                }

                list.Add("{" +
                    "\"pawn\":" + HttpServer.ToJsonString(pawn.Name?.ToStringShort ?? pawn.LabelCap) + "," +
                    "\"id\":" + pawn.thingIDNumber + "," +
                    "\"xenotype\":" + HttpServer.ToJsonString(xeno?.label ?? xeno?.defName ?? "Baseliner") + "," +
                    "\"genes\":" + (geneList.Count > 0 ? "[" + string.Join(",", geneList) + "]" : "[]") +
                    "}");
            }

            return HttpServer.JsonSuccess(HttpServer.BuildJsonArray(list.ToArray()));
        }

        public static string Xenotypes(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            var list = new List<string>();

            if (ModsConfig.BiotechActive)
            {
                var xenotypes = DefDatabase<XenotypeDef>.AllDefsListForReading;
                foreach (var xeno in xenotypes)
                {
                    list.Add("{" +
                        "\"label\":" + HttpServer.ToJsonString(xeno.label ?? xeno.defName) + "," +
                        "\"defName\":" + HttpServer.ToJsonString(xeno.defName) + "," +
                        "\"description\":" + HttpServer.ToJsonString((xeno.description ?? "").Truncate(200)) +
                        "}");
                }
            }

            return HttpServer.JsonSuccess(HttpServer.BuildJsonArray(list.ToArray()));
        }
    }
}
