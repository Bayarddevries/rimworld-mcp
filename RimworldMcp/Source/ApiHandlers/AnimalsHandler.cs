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
    /// Animal management endpoints.
    ///
    /// GET  /api/animals          — list all tamed animals
    /// GET  /api/animals/wild     — list all wild animals on the map
    /// GET  /api/animals/{id}     — animal detail with training
    /// POST /api/animals/train    — apply a training session
    /// POST /api/animals/slaughter — slaughter an animal
    /// POST /api/animals/hunt     — hunt a wild animal
    /// </summary>
    public static class AnimalsHandler
    {
        public static string List(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            var animals = GameBridge.GetTamedAnimals();
            var list = new List<string>();

            foreach (var a in animals)
            {
                list.Add(SerializeAnimalBrief(a));
            }

            return HttpServer.JsonSuccess(HttpServer.BuildJsonArray(list.ToArray()));
        }

        public static string Wildlife(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            var wild = new List<string>();
            var playerFaction = Faction.OfPlayer;

            foreach (var pawn in Find.CurrentMap.mapPawns.AllPawns)
            {
                if (pawn.RaceProps?.Animal != true) continue;
                if (pawn.Faction == playerFaction) continue; // skip tamed
                if (pawn.RaceProps.IsMechanoid) continue;

                float health = pawn.health?.summaryHealth?.SummaryHealthPercent ?? 0;

                // Danger assessment
                float manhunterChance = 0;
                float manhunterOnDamage = 0;
                try { manhunterChance = pawn.RaceProps.manhunterOnTameFailChance; } catch { }
                try { manhunterOnDamage = pawn.RaceProps.manhunterOnDamageChance; } catch { }

                float wildness = 1;
                try { wildness = pawn.GetStatValue(StatDefOf.Wildness); } catch { }

                bool canTame = false;
                try { canTame = pawn.RaceProps.trainability != null; } catch { }

                // Determine threat level
                string threat = "safe";
                float combatPower = pawn.kindDef?.combatPower ?? 0;
                if (combatPower >= 1.5f) threat = "deadly";
                else if (combatPower >= 0.8f) threat = "dangerous";
                else if (manhunterChance > 0.05f || manhunterOnDamage > 0.05f) threat = "volatile";

                wild.Add("{" +
                    "\"id\":" + pawn.thingIDNumber + "," +
                    "\"name\":" + HttpServer.ToJsonString(pawn.LabelCap) + "," +
                    "\"kind\":" + HttpServer.ToJsonString(pawn.kindDef?.label ?? pawn.kindDef?.defName ?? "Animal") + "," +
                    "\"gender\":" + HttpServer.ToJsonString(pawn.gender.ToString()) + "," +
                    "\"health\":" + health.ToString("F2") + "," +
                    "\"wildness\":" + wildness.ToString("F2") + "," +
                    "\"trainable\":" + (canTame ? "true" : "false") + "," +
                    "\"threat\":" + HttpServer.ToJsonString(threat) + "," +
                    "\"manhunterChance\":" + manhunterChance.ToString("F2") + "," +
                    "\"combatPower\":" + combatPower.ToString("F2") + "," +
                    "\"bodySize\":" + pawn.BodySize.ToString("F1") + "," +
                    "\"herd\":" + HttpServer.ToJsonString(pawn.kindDef?.defName ?? "Unknown") +
                    "}");
            }

            return HttpServer.JsonSuccess(HttpServer.BuildJsonArray(wild.ToArray()));
        }

        public static string Hunt(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            string body = HttpServer.ReadBody(req);
            var data = ParseSimpleJson(body);
            string identifier = GetValue(data, "animal");

            if (identifier == null)
                return HttpServer.JsonError("Missing field: animal");

            var animal = Find.CurrentMap.mapPawns.AllPawns
                .FirstOrDefault(p => p.RaceProps?.Animal == true &&
                    (p.thingIDNumber.ToString() == identifier ||
                     p.LabelCap.ToLower().Contains(identifier.ToLower())));

            if (animal == null)
                return HttpServer.JsonError($"Animal not found: {identifier}");

            string name = animal.LabelCap;

            // Mark as hunted — draft a random colonist and assign hunt job
            var hunters = GameBridge.GetAllColonists()
                .Where(p => p.health?.summaryHealth?.SummaryHealthPercent > 0.5f)
                .ToList();

            if (hunters.Count == 0)
                return HttpServer.JsonError("No healthy colonists to hunt");

            var hunter = hunters[new Random().Next(hunters.Count)];

            var job = JobMaker.MakeJob(JobDefOf.Hunt, animal);
            hunter.jobs.StartJob(job, Verse.AI.JobCondition.InterruptForced);

            string msg = hunter.Name?.ToStringShort + " is hunting " + name;
            return HttpServer.JsonSuccess("{\"message\":" + HttpServer.ToJsonString(msg) + "}");
        }

        public static string Detail(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            string path = req.Url.AbsolutePath.ToLowerInvariant();
            string identifier = HttpServer.ExtractIdFromPath(path, "/api/animals/");

            var animal = GameBridge.GetTamedAnimals()
                .FirstOrDefault(p => p.thingIDNumber.ToString() == identifier ||
                                     p.LabelCap.ToLower().Contains(identifier.ToLower()));

            if (animal == null)
                return HttpServer.JsonError($"Animal not found: {identifier}");

            return HttpServer.JsonSuccess(SerializeAnimalFull(animal));
        }

        public static string Train(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            string body = HttpServer.ReadBody(req);
            var data = ParseSimpleJson(body);
            string identifier = GetValue(data, "animal");
            string trainDef = GetValue(data, "train");

            if (identifier == null || trainDef == null)
                return HttpServer.JsonError("Missing fields: animal, train");

            var animal = GameBridge.GetTamedAnimals()
                .FirstOrDefault(p => p.thingIDNumber.ToString() == identifier ||
                                     p.LabelCap.ToLower().Contains(identifier.ToLower()));

            if (animal == null)
                return HttpServer.JsonError($"Animal not found: {identifier}");

            if (animal.training == null)
                return HttpServer.JsonError("Animal cannot be trained");

            var td = DefDatabase<TrainableDef>.GetNamedSilentFail(trainDef);
            if (td == null)
                td = DefDatabase<TrainableDef>.AllDefsListForReading
                    .FirstOrDefault(d => d.label.ToLower().Contains(trainDef.ToLower()));

            if (td == null)
                return HttpServer.JsonError($"Trainable def not found: {trainDef}");

            // Check if already fully trained
            bool isFullyTrained = false;
            try { isFullyTrained = animal.training.HasLearned(td); } catch { }

            if (isFullyTrained)
                return HttpServer.JsonError($"{animal.LabelCap} already knows {td.label}");

            // Apply training (returns void in 1.6)
            animal.training.Train(td, null, true);

            string result = $"{animal.LabelCap} trained in {td.label}";
            return HttpServer.JsonSuccess($"{{\"message\":{HttpServer.ToJsonString(result)}}}");
        }

        public static string Slaughter(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            string body = HttpServer.ReadBody(req);
            var data = ParseSimpleJson(body);
            string identifier = GetValue(data, "animal");

            if (identifier == null)
                return HttpServer.JsonError("Missing field: animal");

            var animal = GameBridge.GetTamedAnimals()
                .FirstOrDefault(p => p.thingIDNumber.ToString() == identifier ||
                                     p.LabelCap.ToLower().Contains(identifier.ToLower()));

            if (animal == null)
                return HttpServer.JsonError($"Animal not found: {identifier}");

            string name = animal.LabelCap;

            // Gather products before killing
            var products = new List<string>();
            if (animal.RaceProps?.meatDef != null)
                products.Add(animal.RaceProps.meatDef.label);
            if (animal.RaceProps?.leatherDef != null)
                products.Add(animal.RaceProps.leatherDef.label);

            // Slaughter
            animal.Kill(null);
            if (animal.Corpse != null)
                animal.Corpse.Destroy();

            string productStr = products.Count > 0 ? " (" + string.Join(", ", products) + ")" : "";
            string msg = name + " slaughtered" + productStr;
            return HttpServer.JsonSuccess("{\"message\":" + HttpServer.ToJsonString(msg) + "}");
        }

        // ─── Serialization ───

        private static string SerializeAnimalBrief(Pawn a)
        {
            float health = a.health?.summaryHealth?.SummaryHealthPercent ?? 0;
            string bondedTo = GetBonded(a);

            return "{" +
                "\"id\":" + a.thingIDNumber + "," +
                "\"name\":" + HttpServer.ToJsonString(a.Name?.ToStringShort ?? a.LabelCap) + "," +
                "\"kind\":" + HttpServer.ToJsonString(a.kindDef?.label ?? a.kindDef?.defName ?? "Animal") + "," +
                "\"gender\":" + HttpServer.ToJsonString(a.gender.ToString()) + "," +
                "\"age\":" + (a.ageTracker?.AgeBiologicalYears ?? 0) + "," +
                "\"health\":" + health.ToString("F2") + "," +
                "\"bonded\":" + (bondedTo != null ? "true" : "false") + "," +
                "\"bonded_to\":" + (bondedTo != null ? HttpServer.ToJsonString(bondedTo) : "null") +
                "}";
        }

        private static string GetBonded(Pawn a)
        {
            if (a.relations?.DirectRelations == null) return null;
            foreach (var rel in a.relations.DirectRelations)
            {
                if (rel.def == PawnRelationDefOf.Bond)
                    return rel.otherPawn?.Name?.ToStringShort ?? rel.otherPawn?.LabelCap;
            }
            return null;
        }

        private static string SerializeAnimalFull(Pawn a)
        {
            float health = a.health?.summaryHealth?.SummaryHealthPercent ?? 0;
            float moodVal = a.needs?.mood?.CurLevelPercentage ?? 0;

            // Training — 1.6 API: GetTraining(td), HasLearned(td)
            var trainItems = new List<string>();
            if (a.training != null)
            {
                foreach (var td in DefDatabase<TrainableDef>.AllDefsListForReading)
                {
                    bool learned = false;
                    try { learned = a.training.HasLearned(td); } catch { }

                    trainItems.Add("{" +
                        "\"defName\":" + HttpServer.ToJsonString(td.defName) + "," +
                        "\"label\":" + HttpServer.ToJsonString(td.label ?? td.defName) + "," +
                        "\"learned\":" + (learned ? "true" : "false") +
                        "}");
                }
            }

            // Needs
            var needItems = new List<string>();
            if (a.needs != null)
            {
                foreach (var n in a.needs.AllNeeds)
                {
                    needItems.Add("{" +
                        "\"name\":" + HttpServer.ToJsonString(n.def.label) + "," +
                        "\"value\":" + n.CurLevelPercentage.ToString("F2") +
                        "}");
                }
            }

            string bondedTo = GetBonded(a);

            // Mass via stat
            float mass = 0;
            try { mass = a.GetStatValue(StatDefOf.Mass); } catch { }

            // Carry capacity via stat  
            float carry = 0;
            try { carry = a.GetStatValue(StatDefOf.CarryingCapacity); } catch { }

            return "{" +
                "\"id\":" + a.thingIDNumber + "," +
                "\"name\":" + HttpServer.ToJsonString(a.Name?.ToStringShort ?? a.LabelCap) + "," +
                "\"fullName\":" + HttpServer.ToJsonString(a.Name?.ToStringFull ?? a.LabelCap) + "," +
                "\"kind\":" + HttpServer.ToJsonString(a.kindDef?.label ?? a.kindDef?.defName ?? "Animal") + "," +
                "\"gender\":" + HttpServer.ToJsonString(a.gender.ToString()) + "," +
                "\"age\":" + (a.ageTracker?.AgeBiologicalYears ?? 0) + "," +
                "\"health\":" + health.ToString("F2") + "," +
                "\"mood\":" + moodVal.ToString("F2") + "," +
                "\"bodySize\":" + a.BodySize.ToString("F1") + "," +
                "\"mass\":" + mass.ToString("F0") + "," +
                "\"carryCapacity\":" + carry.ToString("F0") + "," +
                "\"trainability\":" + HttpServer.ToJsonString(a.RaceProps?.trainability?.label ?? "None") + "," +
                "\"wildness\":" + a.GetStatValue(StatDefOf.Wildness).ToString("F2") + "," +
                "\"bonded_to\":" + (bondedTo != null ? HttpServer.ToJsonString(bondedTo) : "null") + "," +
                "\"training\":" + (trainItems.Count > 0 ? "[" + string.Join(",", trainItems) + "]" : "[]") + "," +
                "\"needs\":" + (needItems.Count > 0 ? "[" + string.Join(",", needItems) + "]" : "[]") +
                "}";
        }

        // ─── Helpers ───

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
