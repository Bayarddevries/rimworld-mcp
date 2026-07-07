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
    /// Extended pawn detail — stats, gear, and social log.
    ///
    /// GET  /api/pawns/stats/{id}    — full stat breakdown for one pawn
    /// GET  /api/pawns/gear/{id}     — worn apparel and equipment
    /// </summary>
    public static class PawnStatsHandler
    {
        public static string Stats(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            string path = req.Url.AbsolutePath.ToLowerInvariant();
            string idStr = HttpServer.ExtractIdFromPath(path, "/api/pawns/stats/");
            if (!int.TryParse(idStr, out int pawnId))
                return HttpServer.JsonError("Invalid pawn ID");

            var pawn = FindPawnById(pawnId);
            if (pawn == null)
                return HttpServer.JsonError($"Pawn not found: {pawnId}");

            var stats = new List<string>();

            // Key combat stats
            TryAddStat(stats, pawn, "Move Speed", StatDefOf.MoveSpeed);
            TryAddStat(stats, pawn, "Melee DPS", StatDefOf.MeleeDPS);
            TryAddStat(stats, pawn, "Melee Hit Chance", StatDefOf.MeleeHitChance);
            TryAddStat(stats, pawn, "Melee Dodge Chance", StatDefOf.MeleeDodgeChance);
            TryAddStat(stats, pawn, "Shooting Accuracy", StatDefOf.ShootingAccuracyPawn);
            TryAddStat(stats, pawn, "Aiming Time", StatDefOf.AimingDelayFactor);
            TryAddStat(stats, pawn, "Armor Blunt", StatDefOf.ArmorRating_Blunt);
            TryAddStat(stats, pawn, "Armor Sharp", StatDefOf.ArmorRating_Sharp);
            TryAddStat(stats, pawn, "Armor Heat", StatDefOf.ArmorRating_Heat);

            // Work stats — attempt known 1.6 stat names
            TryAddStat(stats, pawn, "Work Speed", StatDefOf.WorkSpeedGlobal);
            TryAddStat(stats, pawn, "Construction Speed", StatDefOf.ConstructionSpeedFactor);
            TryAddStat(stats, pawn, "Mining Speed", StatDefOf.MiningSpeed);
            TryAddStat(stats, pawn, "Research Speed", StatDefOf.ResearchSpeed);
            TryAddStat(stats, pawn, "General Labor", StatDefOf.GeneralLaborSpeed);

            // Capacity stats
            TryAddStat(stats, pawn, "Carrying Capacity", StatDefOf.CarryingCapacity);
            TryAddStat(stats, pawn, "Negotiation", StatDefOf.NegotiationAbility);
            TryAddStat(stats, pawn, "Social Impact", StatDefOf.SocialImpact);
            TryAddStat(stats, pawn, "Medical Tend Speed", StatDefOf.MedicalTendSpeed);
            TryAddStat(stats, pawn, "Medical Surgery", StatDefOf.MedicalSurgerySuccessChance);
            TryAddStat(stats, pawn, "Beauty", StatDefOf.PawnBeauty);

            return HttpServer.JsonSuccess("{" +
                "\"id\":" + pawnId + "," +
                "\"name\":" + HttpServer.ToJsonString(pawn.Name?.ToStringShort ?? pawn.LabelCap) + "," +
                "\"stats\":[" + string.Join(",", stats) + "]" +
                "}");
        }

        public static string Gear(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            string path = req.Url.AbsolutePath.ToLowerInvariant();
            string idStr = HttpServer.ExtractIdFromPath(path, "/api/pawns/gear/");
            if (!int.TryParse(idStr, out int pawnId))
                return HttpServer.JsonError("Invalid pawn ID");

            var pawn = FindPawnById(pawnId);
            if (pawn == null)
                return HttpServer.JsonError($"Pawn not found: {pawnId}");

            var equipment = new List<string>();
            var apparel = new List<string>();

            // Equipment (weapons)
            if (pawn.equipment != null)
            {
                foreach (var thing in pawn.equipment.AllEquipmentListForReading)
                {
                    float hp = (float)thing.HitPoints / thing.MaxHitPoints * 100;
                    equipment.Add("{" +
                        "\"defName\":" + HttpServer.ToJsonString(thing.def.defName) + "," +
                        "\"label\":" + HttpServer.ToJsonString(thing.LabelCap ?? thing.def.label) + "," +
                        "\"hp\":" + hp.ToString("F0") +
                        "}");
                }
            }

            // Apparel
            if (pawn.apparel != null)
            {
                foreach (var thing in pawn.apparel.WornApparel)
                {
                    float hp = (float)thing.HitPoints / thing.MaxHitPoints * 100;
                    apparel.Add("{" +
                        "\"defName\":" + HttpServer.ToJsonString(thing.def.defName) + "," +
                        "\"label\":" + HttpServer.ToJsonString(thing.LabelCap ?? thing.def.label) + "," +
                        "\"hp\":" + hp.ToString("F0") +
                        "}");
                }
            }

            return HttpServer.JsonSuccess("{" +
                "\"id\":" + pawnId + "," +
                "\"name\":" + HttpServer.ToJsonString(pawn.Name?.ToStringShort ?? pawn.LabelCap) + "," +
                "\"equipment\":" + (equipment.Count > 0 ? "[" + string.Join(",", equipment) + "]" : "[]") + "," +
                "\"apparel\":" + (apparel.Count > 0 ? "[" + string.Join(",", apparel) + "]" : "[]") +
                "}");
        }

        // ─── Helpers ───

        private static void TryAddStat(List<string> list, Pawn pawn, string label, StatDef def)
        {
            try
            {
                float val = pawn.GetStatValue(def);
                list.Add("{" +
                    "\"label\":" + HttpServer.ToJsonString(label) + "," +
                    "\"defName\":" + HttpServer.ToJsonString(def.defName) + "," +
                    "\"value\":" + val.ToString("F2") +
                    "}");
            }
            catch { }
        }

        private static Pawn FindPawnById(int id)
        {
            foreach (var pawn in Find.CurrentMap.mapPawns.FreeColonists)
                if (pawn.thingIDNumber == id) return pawn;
            return null;
        }
    }
}
