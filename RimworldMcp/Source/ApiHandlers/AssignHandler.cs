using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using RimWorld;
using Verse;

namespace RimworldMcp
{
    /// <summary>
    /// Assign tab — outfits, food restrictions, drug policies, medical care.
    /// Uses reflection for RimWorld 1.6 compatibility.
    /// </summary>
    public static class AssignHandler
    {
        public static string Pawns(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            var list = new List<string>();
            var colonists = GameBridge.GetAllColonists();

            foreach (var pawn in colonists)
            {
                string outfit = "Any";
                string foodRestriction = "Any";
                string drugPolicy = "Social drugs";
                string medCare = "Best";

                try
                {
                    var ot = pawn.GetType().GetField("outfits", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.GetValue(pawn);
                    if (ot != null)
                    {
                        var current = ot.GetType().GetField("currentOutfit", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.GetValue(ot);
                        if (current != null)
                        {
                            var label = current.GetType().GetField("label", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.GetValue(current) as string;
                            if (label != null) outfit = label;
                        }
                    }
                }
                catch { }

                try
                {
                    var fr = pawn.GetType().GetField("foodRestriction", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.GetValue(pawn);
                    if (fr != null)
                    {
                        var current = fr.GetType().GetField("currentFoodRestriction", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.GetValue(fr);
                        if (current != null)
                        {
                            var label = current.GetType().GetField("label", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.GetValue(current) as string;
                            if (label != null) foodRestriction = label;
                        }
                    }
                }
                catch { }

                try
                {
                    var dp = pawn.GetType().GetField("drugPolicy", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.GetValue(pawn);
                    if (dp != null)
                    {
                        var current = dp.GetType().GetField("currentPolicy", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.GetValue(dp);
                        if (current != null)
                        {
                            var label = current.GetType().GetField("label", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.GetValue(current) as string;
                            if (label != null) drugPolicy = label;
                        }
                    }
                }
                catch { }

                try
                {
                    var ps = pawn.GetType().GetField("playerSettings", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.GetValue(pawn);
                    if (ps != null)
                    {
                        var mc = ps.GetType().GetField("medCare", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.GetValue(ps);
                        if (mc != null) medCare = mc.ToString();
                    }
                }
                catch { }

                list.Add("{" +
                    "\"id\":" + pawn.thingIDNumber + "," +
                    "\"name\":" + HttpServer.ToJsonString(pawn.Name?.ToStringShort ?? pawn.LabelCap) + "," +
                    "\"outfit\":" + HttpServer.ToJsonString(outfit) + "," +
                    "\"foodRestriction\":" + HttpServer.ToJsonString(foodRestriction) + "," +
                    "\"drugPolicy\":" + HttpServer.ToJsonString(drugPolicy) + "," +
                    "\"medCare\":" + HttpServer.ToJsonString(medCare) +
                    "}");
            }

            return HttpServer.JsonSuccess("[" + string.Join(",", list) + "]");
        }

        public static string ListOutfits(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            var list = new List<string>();
            try
            {
                var db = Current.Game.GetType().GetField("outfitDatabase", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.GetValue(Current.Game);
                if (db != null)
                {
                    var all = db.GetType().GetMethod("get_AllOutfits")?.Invoke(db, null) as IEnumerable;
                    if (all != null)
                    {
                        foreach (var o in all)
                        {
                            var label = o.GetType().GetField("label", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.GetValue(o) as string ?? "Unnamed";
                            list.Add("{\"label\":" + HttpServer.ToJsonString(label) + "}");
                        }
                    }
                }
            }
            catch { }

            return HttpServer.JsonSuccess("[" + string.Join(",", list) + "]");
        }

        public static string ListFood(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            var list = new List<string>();
            try
            {
                var db = Current.Game.GetType().GetField("foodRestrictionDatabase", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.GetValue(Current.Game);
                if (db != null)
                {
                    var all = db.GetType().GetMethod("get_AllFoodRestrictions")?.Invoke(db, null) as IEnumerable;
                    if (all != null)
                    {
                        foreach (var f in all)
                        {
                            var label = f.GetType().GetField("label", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.GetValue(f) as string ?? "Unnamed";
                            list.Add("{\"label\":" + HttpServer.ToJsonString(label) + "}");
                        }
                    }
                }
            }
            catch { }

            return HttpServer.JsonSuccess("[" + string.Join(",", list) + "]");
        }

        public static string ListDrugs(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            var list = new List<string>();
            try
            {
                var db = Current.Game.GetType().GetField("drugPolicyDatabase", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.GetValue(Current.Game);
                if (db != null)
                {
                    var all = db.GetType().GetMethod("get_AllPolicies")?.Invoke(db, null) as IEnumerable;
                    if (all != null)
                    {
                        foreach (var d in all)
                        {
                            var label = d.GetType().GetField("label", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.GetValue(d) as string ?? "Unnamed";
                            list.Add("{\"label\":" + HttpServer.ToJsonString(label) + "}");
                        }
                    }
                }
            }
            catch { }

            return HttpServer.JsonSuccess("[" + string.Join(",", list) + "]");
        }

        public static string SetOutfit(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            string body = HttpServer.ReadBody(req);
            var data = ParseSimpleJson(body);
            string pawnId = GetValue(data, "pawn");
            string outfitName = GetValue(data, "outfit");
            if (pawnId == null || outfitName == null) return HttpServer.JsonError("Missing fields: pawn, outfit");

            var pawn = FindPawn(pawnId);
            if (pawn == null) return HttpServer.JsonError($"Colonist not found: {pawnId}");

            try
            {
                var db = Current.Game.GetType().GetField("outfitDatabase", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.GetValue(Current.Game);
                if (db == null) return HttpServer.JsonError("Cannot access outfit database");
                var all = db.GetType().GetMethod("get_AllOutfits")?.Invoke(db, null) as IEnumerable;
                if (all == null) return HttpServer.JsonError("No outfits found");
                object found = null;
                string lower = outfitName.ToLower();
                foreach (var o in all)
                {
                    var label = o.GetType().GetField("label", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.GetValue(o) as string ?? "";
                    if (label.ToLower().Contains(lower)) { found = o; break; }
                }
                if (found == null) return HttpServer.JsonError($"Outfit not found: {outfitName}");
                var ot = pawn.GetType().GetField("outfits", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.GetValue(pawn);
                if (ot == null) return HttpServer.JsonError("Pawn has no outfit tracker");
                var setter = ot.GetType().GetField("currentOutfit", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (setter == null) return HttpServer.JsonError("Cannot set outfit on this version");
                setter.SetValue(ot, found);
                var labelFinal = found.GetType().GetField("label", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.GetValue(found) as string ?? outfitName;
                return HttpServer.JsonSuccess($"{{\"message\":\"{pawn.Name?.ToStringShort} assigned outfit: {labelFinal}\"}}");
            }
            catch (Exception ex) { return HttpServer.JsonError($"Failed: {ex.Message}"); }
        }

        public static string SetFood(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            string body = HttpServer.ReadBody(req);
            var data = ParseSimpleJson(body);
            string pawnId = GetValue(data, "pawn");
            string foodName = GetValue(data, "food");
            if (pawnId == null || foodName == null) return HttpServer.JsonError("Missing fields: pawn, food");

            var pawn = FindPawn(pawnId);
            if (pawn == null) return HttpServer.JsonError($"Colonist not found: {pawnId}");

            try
            {
                var db = Current.Game.GetType().GetField("foodRestrictionDatabase", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.GetValue(Current.Game);
                if (db == null) return HttpServer.JsonError("Cannot access food restriction database");
                var all = db.GetType().GetMethod("get_AllFoodRestrictions")?.Invoke(db, null) as IEnumerable;
                if (all == null) return HttpServer.JsonError("No food restrictions found");
                object found = null;
                string lower = foodName.ToLower();
                foreach (var f in all)
                {
                    var label = f.GetType().GetField("label", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.GetValue(f) as string ?? "";
                    if (label.ToLower().Contains(lower)) { found = f; break; }
                }
                if (found == null) return HttpServer.JsonError($"Food restriction not found: {foodName}");
                var fr = pawn.GetType().GetField("foodRestriction", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.GetValue(pawn);
                if (fr == null) return HttpServer.JsonError("Pawn has no food restriction tracker");
                var setter = fr.GetType().GetField("currentFoodRestriction", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (setter == null) return HttpServer.JsonError("Cannot set food restriction on this version");
                setter.SetValue(fr, found);
                var labelFinal = found.GetType().GetField("label", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.GetValue(found) as string ?? foodName;
                return HttpServer.JsonSuccess($"{{\"message\":\"{pawn.Name?.ToStringShort} assigned food: {labelFinal}\"}}");
            }
            catch (Exception ex) { return HttpServer.JsonError($"Failed: {ex.Message}"); }
        }

        public static string SetDrug(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            string body = HttpServer.ReadBody(req);
            var data = ParseSimpleJson(body);
            string pawnId = GetValue(data, "pawn");
            string drugName = GetValue(data, "drug");
            if (pawnId == null || drugName == null) return HttpServer.JsonError("Missing fields: pawn, drug");

            var pawn = FindPawn(pawnId);
            if (pawn == null) return HttpServer.JsonError($"Colonist not found: {pawnId}");

            try
            {
                var db = Current.Game.GetType().GetField("drugPolicyDatabase", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.GetValue(Current.Game);
                if (db == null) return HttpServer.JsonError("Cannot access drug policy database");
                var all = db.GetType().GetMethod("get_AllPolicies")?.Invoke(db, null) as IEnumerable;
                if (all == null) return HttpServer.JsonError("No drug policies found");
                object found = null;
                string lower = drugName.ToLower();
                foreach (var d in all)
                {
                    var label = d.GetType().GetField("label", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.GetValue(d) as string ?? "";
                    if (label.ToLower().Contains(lower)) { found = d; break; }
                }
                if (found == null) return HttpServer.JsonError($"Drug policy not found: {drugName}");
                var dp = pawn.GetType().GetField("drugPolicy", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.GetValue(pawn);
                if (dp == null) return HttpServer.JsonError("Pawn has no drug policy tracker");
                var setter = dp.GetType().GetField("currentPolicy", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (setter == null) return HttpServer.JsonError("Cannot set drug policy on this version");
                setter.SetValue(dp, found);
                var labelFinal = found.GetType().GetField("label", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.GetValue(found) as string ?? drugName;
                return HttpServer.JsonSuccess($"{{\"message\":\"{pawn.Name?.ToStringShort} assigned drug policy: {labelFinal}\"}}");
            }
            catch (Exception ex) { return HttpServer.JsonError($"Failed: {ex.Message}"); }
        }

        public static string SetMedCare(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            string body = HttpServer.ReadBody(req);
            var data = ParseSimpleJson(body);
            string pawnId = GetValue(data, "pawn");
            string care = GetValue(data, "medCare");
            if (pawnId == null || care == null) return HttpServer.JsonError("Missing fields: pawn, medCare");

            var pawn = FindPawn(pawnId);
            if (pawn == null) return HttpServer.JsonError($"Colonist not found: {pawnId}");

            int cat;
            switch (care.ToLower())
            {
                case "best": case "4": cat = 3; break;
                case "better": case "3": cat = 2; break;
                case "normal": case "2": cat = 1; break;
                case "herbal": case "no": case "1": case "0": cat = 0; break;
                default: return HttpServer.JsonError($"Invalid medCare: {care}. Use: best, better, normal, herbal, no");
            }

            try
            {
                var ps = pawn.GetType().GetField("playerSettings", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.GetValue(pawn);
                if (ps == null) return HttpServer.JsonError("Pawn has no player settings");
                var setter = ps.GetType().GetField("medCare", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (setter == null) return HttpServer.JsonError("Cannot set medical care on this version");
                setter.SetValue(ps, (MedicalCareCategory)cat);
                return HttpServer.JsonSuccess($"{{\"message\":\"{pawn.Name?.ToStringShort} medical care set to {care}\"}}");
            }
            catch (Exception ex) { return HttpServer.JsonError($"Failed: {ex.Message}"); }
        }

        private static Pawn FindPawn(string identifier)
        {
            if (int.TryParse(identifier, out int id))
                foreach (var p in Find.CurrentMap.mapPawns.FreeColonists)
                    if (p.thingIDNumber == id) return p;
            foreach (var p in Find.CurrentMap.mapPawns.FreeColonists)
                if ((p.Name?.ToStringShort ?? "").ToLower().Contains(identifier.ToLower())) return p;
            return null;
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
