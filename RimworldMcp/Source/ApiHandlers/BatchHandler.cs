using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using Verse;

namespace RimworldMcp
{
    /// <summary>
    /// Batch execution endpoint. Accepts an array of commands and executes them
    /// by making HTTP calls to the local RimWorld API server.
    ///
    /// POST /api/batch
    /// Body: {"calls": [
    ///   {"method": "POST", "path": "/api/spawn/thing", "body": {"thing": "steel", "count": "200"}},
    ///   {"method": "POST", "path": "/api/pawns/skill", "body": {"pawn": "Lumi", "skill": "Cooking", "level": "12"}},
    ///   {"method": "GET", "path": "/api/colony/overview"}
    /// ]}
    ///
    /// Returns: {"success": true, "data": {"count": 3, "results": [...]}}
    /// </summary>
    public static class BatchHandler
    {
        private const int PORT = 8765;

        public static string Execute(HttpListenerRequest req)
        {
            if (!GameBridge.IsGameReady())
                return HttpServer.JsonError("No game loaded");

            string body = HttpServer.ReadBody(req);
            var calls = ParseBatchCalls(body);

            if (calls == null || calls.Count == 0)
                return HttpServer.JsonError("Missing or empty 'calls' array");

            var results = new List<string>();

            foreach (var call in calls)
            {
                string method = call.Method.ToUpperInvariant();
                string path = call.Path;
                string callBody = call.Body;

                string result;
                try
                {
                    string url = $"http://localhost:{PORT}{path}";
                    var webRequest = (HttpWebRequest)WebRequest.Create(url);
                    webRequest.Method = method;
                    webRequest.ContentType = "application/json";
                    webRequest.Timeout = 10000;

                    if (method == "POST" && !string.IsNullOrEmpty(callBody))
                    {
                        byte[] data = Encoding.UTF8.GetBytes(callBody);
                        webRequest.ContentLength = data.Length;
                        using (var stream = webRequest.GetRequestStream())
                            stream.Write(data, 0, data.Length);
                    }
                    else
                    {
                        webRequest.ContentLength = 0;
                    }

                    using (var response = webRequest.GetResponse())
                    using (var stream = response.GetResponseStream())
                    using (var reader = new StreamReader(stream))
                    {
                        result = reader.ReadToEnd();
                    }
                }
                catch (Exception ex)
                {
                    result = HttpServer.JsonError($"Batch item failed: {ex.Message}");
                }

                results.Add("{" +
                    $"\"method\":\"{HttpServer.EscapeJson(method)}\"," +
                    $"\"path\":\"{HttpServer.EscapeJson(path)}\"," +
                    $"\"result\":{result}" +
                    "}");
            }

            string json = "[" + string.Join(",", results) + "]";
            return HttpServer.JsonSuccess($"{{\"count\":{results.Count},\"results\":{json}}}");
        }

        private static List<BatchCall> ParseBatchCalls(string json)
        {
            var calls = new List<BatchCall>();
            if (string.IsNullOrEmpty(json)) return null;

            int callsIdx = json.IndexOf("\"calls\"", StringComparison.OrdinalIgnoreCase);
            if (callsIdx < 0) return null;

            int arrayStart = json.IndexOf('[', callsIdx);
            if (arrayStart < 0) return null;

            int bracketDepth = 0;
            int objStart = -1;
            bool inString = false;
            bool escape = false;

            for (int i = arrayStart; i < json.Length; i++)
            {
                char c = json[i];
                if (escape) { escape = false; continue; }
                if (c == '\\') { escape = true; continue; }
                if (c == '"') { inString = !inString; continue; }
                if (inString) continue;

                if (c == '{') { bracketDepth++; if (objStart < 0) objStart = i; }
                else if (c == '}')
                {
                    bracketDepth--;
                    if (bracketDepth == 0 && objStart >= 0)
                    {
                        string objJson = json.Substring(objStart, i - objStart + 1);
                        var call = ParseCallObject(objJson);
                        if (call != null) calls.Add(call);
                        objStart = -1;
                    }
                }
                else if (c == '[') bracketDepth++;
                else if (c == ']') bracketDepth--;
            }

            return calls;
        }

        private static BatchCall ParseCallObject(string json)
        {
            var dict = ParseSimpleJson(json);
            string method = GetValue(dict, "method");
            string path = GetValue(dict, "path");
            if (method == null || path == null) return null;

            // Reconstruct body as JSON by finding the "body" field
            string bodyJson = ExtractBodyJson(json);

            return new BatchCall
            {
                Method = method,
                Path = path,
                Body = bodyJson
            };
        }

        private static string ExtractBodyJson(string json)
        {
            // Find "body" : { ... } and extract the object
            int bodyIdx = json.IndexOf("\"body\"", StringComparison.OrdinalIgnoreCase);
            if (bodyIdx < 0) return null;

            int colonIdx = json.IndexOf(':', bodyIdx + 5);
            if (colonIdx < 0) return null;

            int objStart = json.IndexOf('{', colonIdx);
            if (objStart < 0)
            {
                // Might be a string value
                int quoteStart = json.IndexOf('"', colonIdx);
                if (quoteStart < 0) return null;
                int quoteEnd = json.IndexOf('"', quoteStart + 1);
                if (quoteEnd < 0) return null;
                return json.Substring(quoteStart, quoteEnd - quoteStart + 1);
            }

            int depth = 0;
            for (int i = objStart; i < json.Length; i++)
            {
                if (json[i] == '{') depth++;
                else if (json[i] == '}') { depth--; if (depth == 0) return json.Substring(objStart, i - objStart + 1); }
            }

            return null;
        }

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

    internal class BatchCall
    {
        public string Method { get; set; }
        public string Path { get; set; }
        public string Body { get; set; }
    }
}
