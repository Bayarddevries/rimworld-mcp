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
    /// Returns a full work-priorities grid across all pawns and work types,
    /// with relevant skill levels overlaid so the player can see at a glance
    /// who is best suited for each job.
    ///
    /// GET /api/colony/workgrid
    /// </summary>
    public static class WorkGridHandler
    {
        public static string Handle(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            var pawns = GameBridge.GetAllColonists();
            var workTypes = DefDatabase<WorkTypeDef>.AllDefsListForReading
                .OrderBy(wt => wt.naturalPriority)       // game's default ordering
                .ToList();

            // Build workType metadata array
            var wtItems = new List<string>();
            foreach (var wt in workTypes)
            {
                var skillNames = new List<string>();
                if (wt.relevantSkills != null)
                {
                    foreach (var sk in wt.relevantSkills)
                    {
                        skillNames.Add(HttpServer.ToJsonString(sk.defName));
                    }
                }

                wtItems.Add("{" +
                    "\"defName\":" + HttpServer.ToJsonString(wt.defName) + "," +
                    "\"label\":" + HttpServer.ToJsonString(wt.label ?? wt.defName) + "," +
                    "\"pawnLabel\":" + HttpServer.ToJsonString(wt.pawnLabel ?? wt.label ?? wt.defName) + "," +
                    "\"verb\":" + HttpServer.ToJsonString(wt.gerundLabel ?? wt.label ?? wt.defName) + "," +
                    "\"description\":" + HttpServer.ToJsonString(wt.description ?? "") + "," +
                    "\"relevantSkills\":" + (
                        skillNames.Count > 0
                            ? "[" + string.Join(",", skillNames) + "]"
                            : "[]"
                    ) + "," +
                    "\"naturalPriority\":" + wt.naturalPriority +
                    "}");
            }

            // Build pawn data array
            var pawnItems = new List<string>();
            foreach (var pawn in pawns)
            {
                if (pawn.workSettings == null) continue;

                var priorityObj = new List<string>();
                var skillObj = new List<string>();

                foreach (var wt in workTypes)
                {
                    int prio = pawn.workSettings.GetPriority(wt);
                    if (prio > 0)
                    {
                        priorityObj.Add(
                            "\"" + HttpServer.EscapeJson(wt.defName) + "\":" + prio
                        );
                    }
                }

                // Collect pawn skills mapped by defName
                if (pawn.skills != null)
                {
                    foreach (var sk in pawn.skills.skills)
                    {
                        skillObj.Add("\"" + HttpServer.EscapeJson(sk.def.defName) + "\":{" +
                            "\"level\":" + sk.Level + "," +
                            "\"passion\":" + ((int)sk.passion) +
                            "}");
                    }
                }

                pawnItems.Add("{" +
                    "\"id\":" + pawn.thingIDNumber + "," +
                    "\"name\":" + HttpServer.ToJsonString(pawn.Name?.ToStringShort ?? pawn.LabelCap) + "," +
                    "\"fullName\":" + HttpServer.ToJsonString(pawn.Name?.ToStringFull ?? pawn.LabelCap) + "," +
                    "\"priorities\":{" + string.Join(",", priorityObj) + "}," +
                    "\"skills\":{" + string.Join(",", skillObj) + "}" +
                    "}");
            }

            string json = "{" +
                "\"workTypes\":" + HttpServer.BuildJsonArray(wtItems.ToArray()) + "," +
                "\"pawns\":" + HttpServer.BuildJsonArray(pawnItems.ToArray()) +
                "}";

            return HttpServer.JsonSuccess(json);
        }
    }
}
