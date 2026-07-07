using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimworldMcp
{
    public static class CommandHandler
    {
        public static string Execute(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            string body = HttpServer.ReadBody(req);
            var data = ParseSimpleJson(body);
            string pawnId = GetValue(data, "pawn");
            string command = GetValue(data, "command");
            string targetId = GetValue(data, "target");

            if (pawnId == null || command == null)
                return HttpServer.JsonError("Missing fields: pawn, command");

            var pawn = FindPawn(pawnId);
            if (pawn == null)
                return HttpServer.JsonError($"Pawn not found: {pawnId}");

            // If pawn is drafted, undraft first
            if (pawn.Drafted)
                pawn.drafter.Drafted = false;

            switch (command.ToLower())
            {
                case "prioritize_haul":
                {
                    Thing thing = FindNearestHaulable(pawn);
                    if (thing == null)
                        return HttpServer.JsonError("No haulable items nearby");
                    var job = JobMaker.MakeJob(JobDefOf.HaulToCell, thing);
                    pawn.jobs.StartJob(job, JobCondition.InterruptForced);
                    return HttpServer.JsonSuccess($"{{\"message\":\"Prioritizing haul of {thing.LabelCap}\"}}");
                }

                case "prioritize_build":
                {
                    var blueprint = FindNearestBlueprint(pawn);
                    if (blueprint == null)
                        return HttpServer.JsonError("No construction blueprints nearby");
                    var job = JobMaker.MakeJob(JobDefOf.FinishFrame, blueprint);
                    pawn.jobs.StartJob(job, JobCondition.InterruptForced);
                    return HttpServer.JsonSuccess($"{{\"message\":\"Prioritizing construction\"}}");
                }

                case "tend":
                {
                    Pawn patient = null;
                    if (targetId != null)
                        patient = FindPawn(targetId);
                    if (patient == null)
                        patient = FindNearestInjuredColonist(pawn);
                    if (patient == null)
                        return HttpServer.JsonError("No one needs tending");
                    var job = JobMaker.MakeJob(JobDefOf.TendPatient, patient);
                    pawn.jobs.StartJob(job, JobCondition.InterruptForced);
                    return HttpServer.JsonSuccess($"{{\"message\":\"Tending {patient.Name.ToStringShort}\"}}");
                }

                case "rescue":
                {
                    Pawn downed = null;
                    if (targetId != null)
                        downed = FindPawn(targetId);
                    if (downed == null)
                        downed = FindNearestDownedColonist(pawn);
                    if (downed == null)
                        return HttpServer.JsonError("No one to rescue");
                    var job = JobMaker.MakeJob(JobDefOf.Rescue, downed);
                    pawn.jobs.StartJob(job, JobCondition.InterruptForced);
                    return HttpServer.JsonSuccess($"{{\"message\":\"Rescuing {downed.Name.ToStringShort}\"}}");
                }

                case "arrest":
                {
                    Pawn target = null;
                    if (targetId != null)
                        target = FindPawn(targetId);
                    if (target == null)
                        target = FindNearestArrestable(pawn);
                    if (target == null)
                        return HttpServer.JsonError("No one to arrest nearby");
                    var job = JobMaker.MakeJob(JobDefOf.Arrest, target);
                    pawn.jobs.StartJob(job, JobCondition.InterruptForced);
                    return HttpServer.JsonSuccess($"{{\"message\":\"Arresting {target.Name.ToStringShort}\"}}");
                }

                case "go_to":
                case "goto":
                {
                    string xStr = GetValue(data, "x");
                    string zStr = GetValue(data, "z");
                    if (xStr == null || zStr == null || !int.TryParse(xStr, out int x) || !int.TryParse(zStr, out int z))
                        return HttpServer.JsonError("Missing or invalid fields: x, z");
                    var cell = new IntVec3(x, 0, z);
                    if (!cell.InBounds(Find.CurrentMap))
                        return HttpServer.JsonError($"Position ({x},{z}) out of bounds");
                    var job = JobMaker.MakeJob(JobDefOf.Goto, cell);
                    pawn.jobs.StartJob(job, JobCondition.InterruptForced);
                    return HttpServer.JsonSuccess($"{{\"message\":\"{pawn.Name.ToStringShort} moving to ({x},{z})\"}}");
                }

                case "hunt_animal":
                case "hunt":
                {
                    if (targetId == null)
                        return HttpServer.JsonError("Missing field: target (animal id or name)");
                    var animal = Find.CurrentMap.mapPawns.AllPawns
                        .FirstOrDefault(p => p.RaceProps?.Animal == true && p.Faction != Faction.OfPlayer &&
                            (p.thingIDNumber.ToString() == targetId || p.LabelCap.ToLower().Contains(targetId.ToLower())));
                    if (animal == null)
                        return HttpServer.JsonError($"No wild animal found: {targetId}");
                    var huntJob = JobMaker.MakeJob(JobDefOf.Hunt, animal);
                    pawn.jobs.StartJob(huntJob, JobCondition.InterruptForced);
                    return HttpServer.JsonSuccess($"{{\"message\":\"{pawn.Name.ToStringShort} hunting {animal.LabelCap}\"}}");
                }

                case "fire_at":
                case "attack":
                {
                    if (targetId == null)
                        return HttpServer.JsonError("Missing field: target (pawn id or name)");
                    var target = Find.CurrentMap.mapPawns.AllPawns
                        .FirstOrDefault(p => (p.thingIDNumber.ToString() == targetId || p.LabelCap.ToLower().Contains(targetId.ToLower()))
                                          && p.Faction != Faction.OfPlayer && !p.Dead && !p.Downed);
                    if (target == null)
                        return HttpServer.JsonError($"No valid target found: {targetId}");
                    // Draft first for ranged attacks
                    if (!pawn.Drafted)
                        pawn.drafter.Drafted = true;
                    var attackJob = JobMaker.MakeJob(JobDefOf.AttackMelee, target);
                    pawn.jobs.StartJob(attackJob, JobCondition.InterruptForced);
                    return HttpServer.JsonSuccess($"{{\"message\":\"{pawn.Name.ToStringShort} attacking {target.LabelCap}\"}}");
                }

                case "cut_plant":
                case "chop":
                {
                    if (targetId == null)
                        return HttpServer.JsonError("Missing field: target (tree id)");
                    var plant = Find.CurrentMap.spawnedThings
                        .FirstOrDefault(t => t.thingIDNumber.ToString() == targetId || t.LabelCap.ToLower().Contains(targetId.ToLower()));
                    if (plant == null || !(plant is Plant))
                        return HttpServer.JsonError($"No plant found: {targetId}");
                    var chopJob = JobMaker.MakeJob(JobDefOf.CutPlant, plant);
                    pawn.jobs.StartJob(chopJob, JobCondition.InterruptForced);
                    return HttpServer.JsonSuccess($"{{\"message\":\"{pawn.Name.ToStringShort} cutting {plant.LabelCap}\"}}");
                }

                case "mine":
                {
                    string xStr = GetValue(data, "x");
                    string zStr = GetValue(data, "z");
                    if (xStr == null || zStr == null || !int.TryParse(xStr, out int x) || !int.TryParse(zStr, out int z))
                        return HttpServer.JsonError("Missing or invalid fields: x, z");
                    var cell = new IntVec3(x, 0, z);
                    if (!cell.InBounds(Find.CurrentMap))
                        return HttpServer.JsonError($"Position ({x},{z}) out of bounds");
                    var rock = Find.CurrentMap.thingGrid.ThingsListAt(cell)?.FirstOrDefault();
                    if (rock == null || !rock.def.building?.isResourceRock == true)
                        return HttpServer.JsonError($"No mineable rock at ({x},{z})");
                    var mineJob = JobMaker.MakeJob(JobDefOf.Mine, rock);
                    pawn.jobs.StartJob(mineJob, JobCondition.InterruptForced);
                    return HttpServer.JsonSuccess($"{{\"message\":\"{pawn.Name.ToStringShort} mining at ({x},{z})\"}}");
                }

                case "draft":
                {
                    pawn.drafter.Drafted = true;
                    return HttpServer.JsonSuccess($"{{\"message\":\"{pawn.Name.ToStringShort} drafted\"}}");
                }

                case "undraft":
                {
                    pawn.drafter.Drafted = false;
                    return HttpServer.JsonSuccess($"{{\"message\":\"{pawn.Name.ToStringShort} undrafted\"}}");
                }

                default:
                    return HttpServer.JsonError($"Unknown command: {command}. Try: prioritize_haul, prioritize_build, tend, rescue, arrest, go_to, hunt, attack, chop, mine, draft, undraft");
            }
        }

        private static Pawn FindPawn(string identifier)
        {
            int id;
            bool isInt = int.TryParse(identifier, out id);
            foreach (var pawn in Find.CurrentMap.mapPawns.AllPawns)
            {
                if (isInt && pawn.thingIDNumber == id) return pawn;
                if (pawn.GetUniqueLoadID().ToLower() == identifier.ToLower()) return pawn;
                if (pawn.Name?.ToStringShort.ToLower().Contains(identifier.ToLower()) == true) return pawn;
            }
            return null;
        }

        private static Thing FindNearestHaulable(Pawn pawn)
        {
            Thing closest = null;
            float closestDist = 99999f;
            foreach (var thing in Find.CurrentMap.spawnedThings)
            {
                if (thing.def?.EverHaulable != true) continue;
                if (thing.IsForbidden(pawn.Faction)) continue;
                // Check if it's already in a stockpile
                if (thing.IsInValidStorage()) continue;
                float dist = pawn.Position.DistanceTo(thing.Position);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closest = thing;
                }
            }
            return closest;
        }

        private static Thing FindNearestBlueprint(Pawn pawn)
        {
            Thing closest = null;
            float closestDist = 99999f;
            foreach (var thing in Find.CurrentMap.spawnedThings)
            {
                if (!(thing is Frame)) continue;
                var frame = thing as Frame;
                if (frame.Faction != pawn.Faction) continue;
                float dist = pawn.Position.DistanceTo(thing.Position);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closest = thing;
                }
            }
            return closest;
        }

        private static Pawn FindNearestInjuredColonist(Pawn pawn)
        {
            Pawn closest = null;
            float closestDist = 99999f;
            foreach (var col in Find.CurrentMap.mapPawns.FreeColonists)
            {
                if (col == pawn) continue;
                if (!col.health.HasHediffsNeedingTend()) continue;
                float dist = pawn.Position.DistanceTo(col.Position);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closest = col;
                }
            }
            return closest;
        }

        private static Pawn FindNearestDownedColonist(Pawn pawn)
        {
            Pawn closest = null;
            float closestDist = 99999f;
            foreach (var col in Find.CurrentMap.mapPawns.FreeColonists)
            {
                if (col == pawn) continue;
                if (!col.Downed) continue;
                float dist = pawn.Position.DistanceTo(col.Position);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closest = col;
                }
            }
            return closest;
        }

        private static Pawn FindNearestArrestable(Pawn pawn)
        {
            Pawn closest = null;
            float closestDist = 99999f;
            foreach (var col in Find.CurrentMap.mapPawns.AllPawns)
            {
                if (col.Faction == pawn.Faction) continue;
                if (col.Downed || col.Dead) continue;
                if (col.guest != null && col.guest.IsPrisoner && col.guest.HostFaction == Faction.OfPlayer) continue;
                float dist = pawn.Position.DistanceTo(col.Position);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closest = col;
                }
            }
            return closest;
        }

        // Simple JSON parser (duplicated across handlers — needs consolidation)
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

        private static string GetValue(Dictionary<string, string> dict, string key)
        {
            dict.TryGetValue(key, out var val);
            return val;
        }
    }
}
