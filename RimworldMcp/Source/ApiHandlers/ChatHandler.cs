using System;
using System.Net;
using Verse;

namespace RimworldMcp
{
    /// <summary>
    /// HTTP handler for chat endpoints.
    /// Bridges player ↔ Hermes communication through ChatManager.
    /// </summary>
    public static class ChatHandler
    {
        /// <summary>
        /// GET /api/chat/pending — Hermes polls for new player messages.
        /// Returns pending messages and clears the queue.
        /// </summary>
        public static string GetPendingPlayerMessages(HttpListenerRequest req)
        {
            var messages = ChatManager.GetPendingPlayerMessages();
            string json = ChatManager.MessagesToJson(messages);
            return HttpServer.JsonSuccess($"{{\"count\":{messages.Count},\"messages\":{json}}}");
        }

        /// <summary>
        /// GET /api/chat/messages — get full chat history (for in-game display).
        /// </summary>
        public static string GetAllMessages(HttpListenerRequest req)
        {
            var messages = ChatManager.GetAllMessages();
            string json = ChatManager.MessagesToJson(messages);
            return HttpServer.JsonSuccess($"{{\"count\":{messages.Count},\"messages\":{json}}}");
        }

        /// <summary>
        /// GET /api/chat/pending_responses — get unread Hermes responses (for in-game display).
        /// </summary>
        public static string GetPendingHermesResponses(HttpListenerRequest req)
        {
            var messages = ChatManager.GetPendingHermesMessages();
            string json = ChatManager.MessagesToJson(messages);
            return HttpServer.JsonSuccess($"{{\"count\":{messages.Count},\"messages\":{json}}}");
        }

        /// <summary>
        /// POST /api/chat/send — player sends a message (from in-game window or external API).
        /// Body: {"text": "your message"}
        /// </summary>
        public static string SendPlayerMessage(HttpListenerRequest req)
        {
            string body = HttpServer.ReadBody(req);
            var data = ParseSimpleJson(body);

            string text = GetValue(data, "text");
            if (string.IsNullOrEmpty(text))
                return HttpServer.JsonError("Missing field: text");

            ChatManager.AddMessage("Player", text);
            Log.Message($"[RimWorldMcp] Player chat: {text}");

            return HttpServer.JsonSuccess($"{{\"message\":\"Message sent\"}}");
        }

        /// <summary>
        /// POST /api/chat/respond — Hermes sends a response back to the player.
        /// Body: {"text": "response text"}
        /// </summary>
        public static string SendHermesResponse(HttpListenerRequest req)
        {
            string body = HttpServer.ReadBody(req);
            var data = ParseSimpleJson(body);

            string text = GetValue(data, "text");
            if (string.IsNullOrEmpty(text))
                return HttpServer.JsonError("Missing field: text");

            ChatManager.AddMessage("Hermes", text);
            Log.Message($"[RimWorldMcp] Hermes responds: {text}");

            return HttpServer.JsonSuccess($"{{\"message\":\"Response delivered\"}}");
        }

        // -- Helpers (same pattern as other handlers) --
        private static System.Collections.Generic.Dictionary<string, string> ParseSimpleJson(string json)
        {
            var dict = new System.Collections.Generic.Dictionary<string, string>();
            if (string.IsNullOrEmpty(json)) return dict;
            json = json.Trim();
            if (json.StartsWith("{")) json = json.Substring(1);
            if (json.EndsWith("}")) json = json.Substring(0, json.Length - 1);
            bool inString = false, escape = false;
            string currentKey = null;
            var sb = new System.Text.StringBuilder();
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

        private static string GetValue(System.Collections.Generic.Dictionary<string, string> dict, string key)
        {
            dict.TryGetValue(key, out var val);
            return val;
        }
    }
}
