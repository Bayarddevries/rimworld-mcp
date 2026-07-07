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
    /// Pawn relationships and social connections.
    ///
    /// GET  /api/pawns/relations/{id}   — all relations for a pawn
    /// GET  /api/social                 — social overview for all colonists
    /// </summary>
    public static class RelationsHandler
    {
        public static string PawnRelations(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            string path = req.Url.AbsolutePath.ToLowerInvariant();
            string idStr = HttpServer.ExtractIdFromPath(path, "/api/pawns/relations/");
            if (!int.TryParse(idStr, out int pawnId))
                return HttpServer.JsonError("Invalid pawn ID");

            var pawn = FindPawnById(pawnId);
            if (pawn == null)
                return HttpServer.JsonError($"Pawn not found: {pawnId}");

            var relations = new List<string>();

            try
            {
                if (pawn.relations != null)
                {
                    foreach (var rel in pawn.relations.DirectRelations)
                    {
                        if (rel.otherPawn == null || rel.def == null) continue;
                        string label = rel.def.label ?? rel.def.defName;
                        float opinion = 0;
                        try { opinion = rel.def.opinionOffset; } catch { }

                        relations.Add("{" +
                            "\"relation\":" + HttpServer.ToJsonString(label) + "," +
                            "\"with\":" + HttpServer.ToJsonString(rel.otherPawn.Name?.ToStringShort ?? rel.otherPawn.LabelCap) + "," +
                            "\"withId\":" + rel.otherPawn.thingIDNumber + "," +
                            "\"opinion\":" + opinion.ToString("F0") + "," +
                            "\"injured\":" + (rel.otherPawn.health?.HasHediffsNeedingTend() == true ? "true" : "false") + "," +
                            "\"dead\":" + (rel.otherPawn.Dead ? "true" : "false") +
                            "}");
                    }
                }
            }
            catch { }

            // Get opinion of other pawns too
            var opinions = new List<string>();
            try
            {
                foreach (var other in Find.CurrentMap.mapPawns.FreeColonists)
                {
                    if (other == pawn) continue;
                    float op = pawn.relations?.OpinionOf(other) ?? 0;
                    var relDef = PawnRelationUtility.GetMostImportantRelation(pawn, other);
                    opinions.Add("{" +
                        "\"with\":" + HttpServer.ToJsonString(other.Name?.ToStringShort ?? other.LabelCap) + "," +
                        "\"withId\":" + other.thingIDNumber + "," +
                        "\"opinion\":" + op.ToString("F0") + "," +
                        "\"relation\":" + HttpServer.ToJsonString(relDef?.label ?? "None") +
                        "}");
                }
            }
            catch { }

            return HttpServer.JsonSuccess("{" +
                "\"id\":" + pawnId + "," +
                "\"name\":" + HttpServer.ToJsonString(pawn.Name?.ToStringShort ?? pawn.LabelCap) + "," +
                "\"directRelations\":" + (relations.Count > 0 ? "[" + string.Join(",", relations) + "]" : "[]") + "," +
                "\"opinions\":" + (opinions.Count > 0 ? "[" + string.Join(",", opinions) + "]" : "[]") +
                "}");
        }

        public static string SocialOverview(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            var list = new List<string>();
            var colonists = GameBridge.GetAllColonists();

            foreach (var pawn in colonists)
            {
                int lovers = 0, rivals = 0, friends = 0, family = 0, enemies = 0;
                try
                {
                    if (pawn.relations != null)
                    {
                        foreach (var rel in pawn.relations.DirectRelations)
                        {
                            if (rel.otherPawn == null || rel.def == null) continue;
                            string label = rel.def.defName?.ToLower() ?? "";
                            if (label.Contains("lover") || label.Contains("spouse") || label.Contains("fiance")) lovers++;
                            else if (label.Contains("rival")) rivals++;
                            else if (label.Contains("friend")) friends++;
                            else if (label.Contains("parent") || label.Contains("child") || label.Contains("sibling") || label.Contains("cousin")) family++;
                            else if (label.Contains("enemy") || label.Contains("hated")) enemies++;
                        }
                    }
                }
                catch { }

                int totalOps = 0, positiveOps = 0, negativeOps = 0;
                try
                {
                    foreach (var other in colonists)
                    {
                        if (other == pawn) continue;
                        float op = pawn.relations?.OpinionOf(other) ?? 0;
                        totalOps++;
                        if (op > 20) positiveOps++;
                        else if (op < -20) negativeOps++;
                    }
                }
                catch { }

                list.Add("{" +
                    "\"id\":" + pawn.thingIDNumber + "," +
                    "\"name\":" + HttpServer.ToJsonString(pawn.Name?.ToStringShort ?? pawn.LabelCap) + "," +
                    "\"lovers\":" + lovers + "," +
                    "\"rivals\":" + rivals + "," +
                    "\"friends\":" + friends + "," +
                    "\"family\":" + family + "," +
                    "\"enemies\":" + enemies + "," +
                    "\"positiveOpinions\":" + positiveOps + "," +
                    "\"negativeOpinions\":" + negativeOps +
                    "}");
            }

            return HttpServer.JsonSuccess("[" + string.Join(",", list) + "]");
        }

        private static Pawn FindPawnById(int id)
        {
            foreach (var pawn in Find.CurrentMap.mapPawns.FreeColonists)
                if (pawn.thingIDNumber == id) return pawn;
            return null;
        }
    }
}
