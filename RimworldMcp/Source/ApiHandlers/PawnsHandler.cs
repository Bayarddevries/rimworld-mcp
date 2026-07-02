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
    /// <summary>
    /// Handles all pawn-related API requests.
    /// </summary>
    public static class PawnsHandler
    {
        public static string List(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            var pawns = GameBridge.GetAllColonists();
            var pawnEntries = new List<string>();

            foreach (var pawn in pawns)
            {
                pawnEntries.Add(SerializePawn(pawn));
            }

            return HttpServer.JsonSuccess(HttpServer.BuildJsonArray(pawnEntries.ToArray()));
        }

        public static string GetById(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            string path = req.Url.AbsolutePath.ToLowerInvariant();
            string identifier = HttpServer.ExtractIdFromPath(path, "/api/pawns/");

            var pawn = GameBridge.FindColonist(identifier);
            if (pawn == null)
                return HttpServer.JsonError($"Pawn not found: {identifier}");

            return HttpServer.JsonSuccess(SerializePawn(pawn));
        }

        public static string SetSkill(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            string body = HttpServer.ReadBody(req);
            var data = ParseSimpleJson(body);

            string pawnId = GetValue(data, "pawn");
            string skillName = GetValue(data, "skill");
            string levelStr = GetValue(data, "level");

            if (pawnId == null || skillName == null || levelStr == null)
                return HttpServer.JsonError("Missing fields: pawn, skill, level");

            if (!int.TryParse(levelStr, out int level))
                return HttpServer.JsonError("level must be an integer (0-20)");

            level = Math.Max(0, Math.Min(20, level));

            var pawn = GameBridge.FindColonist(pawnId);
            if (pawn == null)
                return HttpServer.JsonError($"Pawn not found: {pawnId}");

            // Find skill by name
            SkillDef skillDef = DefDatabase<SkillDef>.AllDefsListForReading
                .FirstOrDefault(s => s.label.ToLower() == skillName.ToLower() ||
                                     s.defName.ToLower() == skillName.ToLower());
            if (skillDef == null)
                return HttpServer.JsonError($"Skill not found: {skillName}. Available: " +
                    string.Join(", ", DefDatabase<SkillDef>.AllDefsListForReading.Select(s => s.label)));

            var skill = pawn.skills?.GetSkill(skillDef);
            if (skill == null)
                return HttpServer.JsonError($"Pawn does not have skill {skillName}");

            skill.Level = level;

            return HttpServer.JsonSuccess($"{{\"message\":\"Set {skillDef.label} to level {level} for {pawn.LabelCap}\",\"pawn\":{HttpServer.ToJsonString(pawn.LabelCap)},\"skill\":{HttpServer.ToJsonString(skillDef.label)},\"level\":{level}}}");
        }

        public static string AddTrait(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            string body = HttpServer.ReadBody(req);
            var data = ParseSimpleJson(body);

            string pawnId = GetValue(data, "pawn");
            string traitName = GetValue(data, "trait");
            string degreeStr = GetValue(data, "degree");

            if (pawnId == null || traitName == null)
                return HttpServer.JsonError("Missing fields: pawn, trait");

            int degree = 0;
            if (degreeStr != null) int.TryParse(degreeStr, out degree);

            var pawn = GameBridge.FindColonist(pawnId);
            if (pawn == null)
                return HttpServer.JsonError($"Pawn not found: {pawnId}");

            // Find trait def
            TraitDef traitDef = DefDatabase<TraitDef>.AllDefsListForReading
                .FirstOrDefault(t => t.label.ToLower() == traitName.ToLower() ||
                                     t.defName.ToLower() == traitName.ToLower());

            if (traitDef == null)
                return HttpServer.JsonError($"Trait not found: {traitName}");

            // Remove existing trait of same def
            var existing = pawn.story?.traits?.allTraits?.Find(t => t.def == traitDef);
            if (existing != null)
                pawn.story.traits.allTraits.Remove(existing);

            pawn.story?.traits?.GainTrait(new Trait(traitDef, degree));

            return HttpServer.JsonSuccess($"{{\"message\":\"Added trait {traitDef.label} to {pawn.LabelCap}\"}}");
        }

        public static string SetHealth(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            string body = HttpServer.ReadBody(req);
            var data = ParseSimpleJson(body);

            string pawnId = GetValue(data, "pawn");
            string action = GetValue(data, "action"); // "heal", "injure"
            string bodyPart = GetValue(data, "body_part"); // optional

            if (pawnId == null || action == null)
                return HttpServer.JsonError("Missing fields: pawn, action");

            var pawn = GameBridge.FindColonist(pawnId);
            if (pawn == null)
                return HttpServer.JsonError($"Pawn not found: {pawnId}");

            if (action == "heal")
            {
                if (bodyPart != null)
                {
                    // Heal specific body part
                    var part = FindBodyPart(pawn, bodyPart);
                    if (part == null)
                        return HttpServer.JsonError($"Body part not found: {bodyPart}");

                    pawn.health.RestorePart(part, null, true);
                }
                else
                {
                    // Heal everything
                    pawn.health.Reset();
                }
                return HttpServer.JsonSuccess($"{{\"message\":\"Healed {pawn.LabelCap}\"}}");
            }
            else if (action == "injure")
            {
                // Apply injury to random or specified part
                var part = bodyPart != null ? FindBodyPart(pawn, bodyPart) : pawn.health.hediffSet.GetNotMissingParts().RandomElement();
                if (part == null)
                    return HttpServer.JsonError($"Cannot find body part to injure");

                var damage = new DamageInfo(DamageDefOf.Scratch, 10, 999f);
                pawn.TakeDamage(damage);

                return HttpServer.JsonSuccess($"{{\"message\":\"Injured {pawn.LabelCap}\"}}");
            }

            return HttpServer.JsonError("Unknown action. Use 'heal' or 'injure'.");
        }

        public static string SetNeeds(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            string body = HttpServer.ReadBody(req);
            var data = ParseSimpleJson(body);

            string pawnId = GetValue(data, "pawn");
            string needName = GetValue(data, "need");
            string valueStr = GetValue(data, "value");

            if (pawnId == null || needName == null || valueStr == null)
                return HttpServer.JsonError("Missing fields: pawn, need, value");

            if (!float.TryParse(valueStr, out float value))
                return HttpServer.JsonError("value must be a number (0.0 to 1.0)");

            value = Math.Max(0, Math.Min(1, value));

            var pawn = GameBridge.FindColonist(pawnId);
            if (pawn == null)
                return HttpServer.JsonError($"Pawn not found: {pawnId}");

            // Find need by name
            var need = pawn.needs?.AllNeeds?
                .FirstOrDefault(n => n.def.label.ToLower() == needName.ToLower() ||
                                     n.def.defName.ToLower() == needName.ToLower());

            if (need == null)
                return HttpServer.JsonError($"Need '{needName}' not found on {pawn.LabelCap}");

            need.CurLevel = value;

            return HttpServer.JsonSuccess($"{{\"message\":\"Set {need.def.label} to {value:F2} for {pawn.LabelCap}\"}}");
        }

        public static string EquipGear(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            string body = HttpServer.ReadBody(req);
            var data = ParseSimpleJson(body);

            string pawnId = GetValue(data, "pawn");
            string itemName = GetValue(data, "item");
            string slot = GetValue(data, "slot"); // "weapon", "apparel", "inventory"

            if (pawnId == null || itemName == null)
                return HttpServer.JsonError("Missing fields: pawn, item");

            var pawn = GameBridge.FindColonist(pawnId);
            if (pawn == null)
                return HttpServer.JsonError($"Pawn not found: {pawnId}");

            // Try to find item on map by label
            Thing thing = null;
            foreach (var t in Find.CurrentMap.spawnedThings)
            {
                if (t.def.label.ToLower().Contains(itemName.ToLower()) && !t.IsForbidden(pawn.Faction))
                {
                    thing = t;
                    break;
                }
            }

            if (thing == null)
            {
                // Try to spawn it
                ThingDef thingDef = DefDatabase<ThingDef>.AllDefsListForReading
                    .FirstOrDefault(d => d.label.ToLower().Contains(itemName.ToLower()));

                if (thingDef == null)
                    return HttpServer.JsonError($"Item not found on map: {itemName}");

                thing = ThingMaker.MakeThing(thingDef);
                GenSpawn.Spawn(thing, pawn.Position, Find.CurrentMap);
            }

            // Equip based on slot
            if (slot == "weapon" || thing.def.IsWeapon)
            {
                var eq = pawn.equipment;
                if (eq != null)
                {
                    eq.AddEquipment((ThingWithComps)thing);
                    return HttpServer.JsonSuccess($"{{\"message\":\"Equipped {thing.LabelCap} to {pawn.LabelCap}\"}}");
                }
            }
            else if (slot == "apparel" || thing.def.IsApparel)
            {
                var app = pawn.apparel;
                if (app != null)
                {
                    app.Wear((Apparel)thing);
                    return HttpServer.JsonSuccess($"{{\"message\":\"Equipped {thing.LabelCap} to {pawn.LabelCap}\"}}");
                }
            }

            return HttpServer.JsonError("Could not equip item. Try specifying slot: weapon, apparel, or inventory.");
        }

        public static string Inspire(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            string body = HttpServer.ReadBody(req);
            var data = ParseSimpleJson(body);

            string pawnId = GetValue(data, "pawn");
            string inspirationType = GetValue(data, "inspiration");

            var pawn = GameBridge.FindColonist(pawnId);
            if (pawn == null)
                return HttpServer.JsonError($"Pawn not found: {pawnId}");

            if (inspirationType != null)
            {
                var inspDef = DefDatabase<InspirationDef>.AllDefsListForReading
                    .FirstOrDefault(i => i.label.ToLower().Contains(inspirationType.ToLower()) ||
                                         i.defName.ToLower().Contains(inspirationType.ToLower()));
                if (inspDef != null && pawn.mindState != null)
                {
                    pawn.mindState.inspirationHandler.TryStartInspiration(inspDef);
                    return HttpServer.JsonSuccess($"{{\"message\":\"Inspired {pawn.LabelCap} with {inspDef.label}\"}}");
                }
            }

            // Random inspiration
            if (pawn.mindState?.inspirationHandler != null)
            {
                var allInsp = DefDatabase<InspirationDef>.AllDefsListForReading;
                foreach (var insp in allInsp.InRandomOrder())
                {
                    if (pawn.mindState.inspirationHandler.TryStartInspiration(insp))
                        return HttpServer.JsonSuccess($"{{\"message\":\"Inspired {pawn.LabelCap} with {insp.label}\"}}");
                }
            }

            return HttpServer.JsonError("Could not inspire pawn");
        }

        // --- Serialization ---

        internal static string SerializePawn(Pawn pawn)
        {
            var fields = new List<string>();

            // Basic info
            fields.Add($"\"id\":{pawn.thingIDNumber}");
            fields.Add($"\"name\":{HttpServer.ToJsonString(pawn.LabelCap)}");
            fields.Add($"\"full_name\":{HttpServer.ToJsonString(pawn.Name?.ToStringFull ?? pawn.LabelCap)}");
            fields.Add($"\"faction\":{HttpServer.ToJsonString(pawn.Faction?.Name ?? "None")}");
            fields.Add($"\"gender\":{HttpServer.ToJsonString(pawn.gender.ToString())}");
            fields.Add($"\"age\":{pawn.ageTracker?.AgeBiologicalYears ?? 0}");
            fields.Add($"\"kind\":{HttpServer.ToJsonString(pawn.kindDef?.defName ?? "Unknown")}");

            // Drafted status
            fields.Add($"\"drafted\":{(pawn.drafter?.Drafted == true ? "true" : "false")}");

            // Health
            if (pawn.health != null)
            {
                float healthPct = (float)pawn.health.summaryHealth.SummaryHealthPercent;
                fields.Add($"\"health\":{healthPct:F2}");

                var injuries = new List<string>();
                foreach (var hediff in pawn.health.hediffSet.hediffs)
                {
                    if (hediff.Visible)
                        injuries.Add(HttpServer.BuildJsonObject(
                            ("label", HttpServer.ToJsonString(hediff.LabelCap)),
                            ("severity", hediff.Severity.ToString("F2")),
                            ("part", HttpServer.ToJsonString(hediff.Part?.Label ?? "whole body"))
                        ));
                }
                fields.Add($"\"injuries\":{HttpServer.BuildJsonArray(injuries.ToArray())}");
            }

            // Mood
            if (pawn.needs?.mood != null)
            {
                fields.Add($"\"mood\":{pawn.needs.mood.CurLevelPercentage:F2}");
                fields.Add($"\"mood_level\":{HttpServer.ToJsonString(pawn.needs.mood.MoodString)}");
            }

            // Skills
            if (pawn.skills != null)
            {
                var skillList = new List<string>();
                foreach (var skill in pawn.skills.skills)
                {
                    skillList.Add(HttpServer.BuildJsonObject(
                        ("name", HttpServer.ToJsonString(skill.def.label)),
                        ("level", skill.Level.ToString()),
                        ("passion", ((int)skill.passion).ToString()),
                        ("xp", skill.xpSinceLastLevel.ToString("F0"))
                    ));
                }
                fields.Add($"\"skills\":{HttpServer.BuildJsonArray(skillList.ToArray())}");
            }

            // Traits
            if (pawn.story?.traits != null)
            {
                var traitList = new List<string>();
                foreach (var trait in pawn.story.traits.allTraits)
                {
                    traitList.Add(HttpServer.ToJsonString(trait.LabelCap));
                }
                fields.Add($"\"traits\":{HttpServer.BuildJsonArray(traitList.ToArray())}");
            }

            // Needs
            if (pawn.needs != null)
            {
                var needList = new List<string>();
                foreach (var need in pawn.needs.AllNeeds)
                {
                    needList.Add(HttpServer.BuildJsonObject(
                        ("name", HttpServer.ToJsonString(need.def.label)),
                        ("value", need.CurLevelPercentage.ToString("F2")),
                        ("description", HttpServer.ToJsonString(need.LabelCap))
                    ));
                }
                fields.Add($"\"needs\":{HttpServer.BuildJsonArray(needList.ToArray())}");
            }

            // Equipment
            if (pawn.equipment != null && pawn.equipment.AllEquipmentListForReading.Count > 0)
            {
                var gearList = new List<string>();
                foreach (var eq in pawn.equipment.AllEquipmentListForReading)
                {
                    gearList.Add(HttpServer.ToJsonString(eq.LabelCap));
                }
                fields.Add($"\"equipment\":{HttpServer.BuildJsonArray(gearList.ToArray())}");
            }

            // Apparel
            if (pawn.apparel != null)
            {
                var apparelList = new List<string>();
                foreach (var app in pawn.apparel.WornApparel)
                {
                    apparelList.Add(HttpServer.ToJsonString(app.LabelCap));
                }
                fields.Add($"\"apparel\":{HttpServer.BuildJsonArray(apparelList.ToArray())}");
            }

            // Position
            if (pawn.Position != null)
            {
                fields.Add($"\"position\":{HttpServer.BuildJsonObject(
                    ("x", pawn.Position.x.ToString()),
                    ("z", pawn.Position.z.ToString())
                )}");
            }

            // Work status
            if (pawn.jobs?.curJob != null)
            {
                fields.Add($"\"current_job\":{HttpServer.ToJsonString(pawn.jobs.curJob.ToString())}");
            }
            else
            {
                fields.Add($"\"current_job\":\"Idle\"");
            }

            return "{" + string.Join(",", fields) + "}";
        }

        // --- Helpers ---

        private static BodyPartRecord FindBodyPart(Pawn pawn, string name)
        {
            string lower = name.ToLower();
            foreach (var part in pawn.health.hediffSet.GetNotMissingParts())
            {
                if (part.Label.ToLower().Contains(lower) ||
                    part.def.defName.ToLower().Contains(lower))
                    return part;
            }
            return null;
        }

        private static Dictionary<string, string> ParseSimpleJson(string json)
        {
            var dict = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(json)) return dict;

            // Simple key-value parser (no nested objects)
            json = json.Trim();
            if (json.StartsWith("{")) json = json.Substring(1);
            if (json.EndsWith("}")) json = json.Substring(0, json.Length - 1);

            bool inString = false;
            bool escape = false;
            string currentKey = null;
            string currentValue = "";
            StringBuilder sb = new StringBuilder();

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
                        sb.Clear();
                        continue;
                    }
                    if (c == ',' || c == '}')
                    {
                        if (currentKey != null)
                        {
                            currentValue = sb.ToString().Trim().Trim('"');
                            dict[currentKey] = currentValue;
                            currentKey = null;
                            sb.Clear();
                        }
                        continue;
                    }
                    if (c == '{' || c == '}' || c == '[' || c == ']' || char.IsWhiteSpace(c))
                        continue;
                }
                sb.Append(c);
            }

            if (currentKey != null)
            {
                currentValue = sb.ToString().Trim().Trim('"');
                dict[currentKey] = currentValue;
            }

            return dict;
        }

        private static string GetValue(Dictionary<string, string> dict, string key)
        {
            dict.TryGetValue(key, out var val);
            return val;
        }
    }
}
