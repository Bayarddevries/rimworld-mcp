using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using Verse;

namespace RimworldMcp
{
    /// <summary>
    /// HTTP server that exposes RimWorld colony state via REST API.
    /// Uses HttpListener (built-in .NET) — no external dependencies.
    /// All game-state access routes through GameBridge for thread safety.
    /// </summary>
    public class HttpServer
    {
        private readonly GameBridge _bridge;
        private readonly int _port;
        private HttpListener _listener;
        private bool _running;

        // Route handler: matches URL path prefix to handler function
        private readonly Dictionary<string, Func<HttpListenerRequest, string>> _getRoutes;
        private readonly Dictionary<string, Func<HttpListenerRequest, string>> _postRoutes;

        public HttpServer(GameBridge bridge, int port)
        {
            _bridge = bridge;
            _port = port;
            _getRoutes = new Dictionary<string, Func<HttpListenerRequest, string>>();
            _postRoutes = new Dictionary<string, Func<HttpListenerRequest, string>>();

            RegisterRoutes();
        }

        private void RegisterRoutes()
        {
            // GET routes
            _getRoutes["/api/pawns"] = PawnsHandler.List;
            _getRoutes["/api/pawns/"] = PawnsHandler.GetById;
            _getRoutes["/api/colony/resources"] = ColonyHandler.Resources;
            _getRoutes["/api/colony/overview"] = ColonyHandler.Overview;
            _getRoutes["/api/research"] = ResearchHandler.List;
            _getRoutes["/api/map"] = MapHandler.Get;
            _getRoutes["/api/map/bills"] = BillsHandler.List;
            _getRoutes["/api/events/storyteller"] = EventsHandler.StorytellerInfo;
            // SSE event stream is handled separately in HandleRequest
            _getRoutes["/api/health"] = HealthHandler.Check;
            _getRoutes["/api/version"] = (req) => "{\"name\":\"RimWorld MCP Bridge\",\"version\":\"1.0.0\",\"game_version\":\"1.6\"}";
            _getRoutes["/api/chat/pending"] = ChatHandler.GetPendingPlayerMessages;
            _getRoutes["/api/chat/messages"] = ChatHandler.GetAllMessages;
            _getRoutes["/api/chat/pending_responses"] = ChatHandler.GetPendingHermesResponses;
            _getRoutes["/api/events/feed"] = EventFeedHandler.GetFeed;
            _getRoutes["/api/events/history"] = EventFeedHandler.GetHistory;
            _getRoutes["/api/goals"] = GoalHandler.ListGoals;
            _getRoutes["/api/colony/paused"] = TimeHandler.GetPauseState;
            _getRoutes["/api/colony/autopause"] = AutoPauseManager.GetConfig;
            _getRoutes["/api/prisoners"] = PrisonersHandler.List;
            _getRoutes["/api/prisoners/"] = PrisonersHandler.Detail;
            _getRoutes["/api/zones"] = ZonesHandler.List;
            _getRoutes["/api/alerts"] = AlertsHandler.List;

            // POST routes
            _postRoutes["/api/pawns/skill"] = PawnsHandler.SetSkill;
            _postRoutes["/api/pawns/trait"] = PawnsHandler.AddTrait;
            _postRoutes["/api/pawns/health"] = PawnsHandler.SetHealth;
            _postRoutes["/api/pawns/needs"] = PawnsHandler.SetNeeds;
            _postRoutes["/api/pawns/gear"] = PawnsHandler.EquipGear;
            _postRoutes["/api/pawns/inspire"] = PawnsHandler.Inspire;
            _postRoutes["/api/pawns/priorities"] = PawnsHandler.HandlePriorities;
            _postRoutes["/api/pawns/inventory"] = PawnsHandler.Inventory;
            _postRoutes["/api/pawns/unequip"] = PawnsHandler.UnequipGear;
            _postRoutes["/api/pawns/rename"] = PawnsHandler.Rename;
            _postRoutes["/api/pawns/surgery"] = PawnsHandler.Surgery;
            _postRoutes["/api/spawn/thing"] = SpawnHandler.SpawnThing;
            _postRoutes["/api/spawn/pawn"] = SpawnHandler.SpawnPawn;
            _postRoutes["/api/research/unlock"] = ResearchHandler.Unlock;
            _postRoutes["/api/events/trigger"] = EventsHandler.Trigger;
            _postRoutes["/api/colony/stockpile"] = ColonyHandler.AddResources;
            _postRoutes["/api/colony/forbid"] = ColonyHandler.ForbidItem;
            _postRoutes["/api/colony/command"] = ColonyHandler.IssueCommand;
            _postRoutes["/api/save"] = SaveHandler.Save;
            _postRoutes["/api/map/bills/add"] = BillsHandler.AddBill;
            _postRoutes["/api/map/bills/remove"] = BillsHandler.RemoveBill;
            _postRoutes["/api/map/bills/suspend"] = BillsHandler.SuspendBill;
            _postRoutes["/api/chat/send"] = ChatHandler.SendPlayerMessage;
            _postRoutes["/api/chat/respond"] = ChatHandler.SendHermesResponse;
            _postRoutes["/api/goals/set"] = GoalHandler.SetGoal;
            _postRoutes["/api/goals/remove"] = GoalHandler.RemoveGoal;
            _postRoutes["/api/batch"] = BatchHandler.Execute;
            _postRoutes["/api/colony/time"] = TimeHandler.SetTime;
            _postRoutes["/api/colony/autopause"] = AutoPauseManager.SetConfig;
            _postRoutes["/api/prisoners/action"] = PrisonersHandler.Action;
            _postRoutes["/api/prisoners/mode"] = PrisonersHandler.SetMode;
            _postRoutes["/api/pawns/command"] = CommandHandler.Execute;
        }

        public void Start()
        {
            _listener = new HttpListener();
            // Bind to all interfaces so Tailscale and LAN IPs can reach the dashboard
            _listener.Prefixes.Add("http://*:8765/");
            // Also keep localhost for clients on the same machine
            _listener.Prefixes.Add("http://localhost:8765/");
            _listener.Start();
            _running = true;

            Log.Message($"[RimWorldMcp] HTTP server listening on http://localhost:{_port}/");

            while (_running)
            {
                try
                {
                    var context = _listener.GetContext();
                    ThreadPool.QueueUserWorkItem(_ => HandleRequest(context));
                }
                catch (HttpListenerException)
                {
                    // Server stopped
                    break;
                }
                catch (Exception ex)
                {
                    Log.Error($"[RimWorldMcp] HTTP listener error: {ex.Message}");
                }
            }
        }

        public void Stop()
        {
            _running = false;
            _listener?.Stop();
            _listener = null;
            Log.Message("[RimWorldMcp] HTTP server stopped.");
        }

        private void HandleRequest(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            try
            {
                string path = request.Url.AbsolutePath.ToLowerInvariant();

                // SSE event stream — long-lived connection, handled separately
                if (request.HttpMethod == "GET" && path == "/api/events/stream")
                {
                    response.ContentType = "text/event-stream";
                    response.Headers["Cache-Control"] = "no-cache";
                    response.Headers["Connection"] = "keep-alive";
                    response.Headers["Access-Control-Allow-Origin"] = "*";
                    response.StatusCode = 200;

                    EventFeedHandler.StreamSSE(response.OutputStream);
                    return; // StreamSSE loops until disconnect; don't close here
                }

                // Dashboard — serve HTML
                if (request.HttpMethod == "GET" && path == "/dashboard")
                {
                    string html = DashboardHandler.ServeDashboard(request);
                    byte[] htmlBuffer = Encoding.UTF8.GetBytes(html);
                    response.ContentType = "text/html; charset=utf-8";
                    response.ContentLength64 = htmlBuffer.Length;
                    response.Headers["Access-Control-Allow-Origin"] = "*";
                    response.OutputStream.Write(htmlBuffer, 0, htmlBuffer.Length);
                    return;
                }

                string result;

                if (request.HttpMethod == "GET")
                {
                    // Health check — doesn't need game thread, handle directly
                    if (path == "/api/health")
                    {
                        result = HealthHandler.Check(request);
                    }
                    else
                    {
                        result = HandleRoute(_getRoutes, path, request);
                    }
                }
                else if (request.HttpMethod == "POST")
                {
                    result = HandleRoute(_postRoutes, path, request);
                }
                else
                {
                    result = JsonError("Method not allowed. Use GET or POST.");
                }

                byte[] buffer = Encoding.UTF8.GetBytes(result);
                response.ContentType = "application/json";
                response.ContentLength64 = buffer.Length;
                response.Headers["Access-Control-Allow-Origin"] = "*";
                response.OutputStream.Write(buffer, 0, buffer.Length);
            }
            catch (Exception ex)
            {
                string error = JsonError($"Internal error: {ex.Message}");
                byte[] buffer = Encoding.UTF8.GetBytes(error);
                response.ContentType = "application/json";
                response.ContentLength64 = buffer.Length;
                response.Headers["Access-Control-Allow-Origin"] = "*";
                response.OutputStream.Write(buffer, 0, buffer.Length);
            }
            finally
            {
                response.OutputStream.Close();
            }
        }

        private string HandleRoute(Dictionary<string, Func<HttpListenerRequest, string>> routes, string path, HttpListenerRequest request)
        {
            // Exact match first
            if (routes.TryGetValue(path, out var handler))
            {
                return _bridge.Execute(() => handler(request));
            }

            // Prefix match (for /api/pawns/{id} style routes)
            // Pick the longest matching key to avoid ambiguous matching
            // e.g. /api/pawns (List) vs /api/pawns/ (GetById) for path /api/pawns/255
            string bestKey = null;
            foreach (var kvp in routes)
            {
                if (path.StartsWith(kvp.Key) && path.Length > kvp.Key.Length)
                {
                    if (bestKey == null || kvp.Key.Length > bestKey.Length)
                        bestKey = kvp.Key;
                }
            }
            if (bestKey != null)
                return _bridge.Execute(() => routes[bestKey](request));

            return JsonError($"Route not found: {request.HttpMethod} {path}");
        }

        internal static string ReadBody(HttpListenerRequest request)
        {
            using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
            {
                return reader.ReadToEnd();
            }
        }

        internal static string ExtractIdFromPath(string path, string prefix)
        {
            string id = path.Substring(prefix.Length);
            // Remove trailing slash
            if (id.EndsWith("/")) id = id.Substring(0, id.Length - 1);
            return Uri.UnescapeDataString(id);
        }

        internal static string JsonError(string message)
        {
            return $"{{\"success\":false,\"error\":\"{EscapeJson(message)}\"}}";
        }

        internal static string JsonSuccess(string data)
        {
            return $"{{\"success\":true,\"data\":{data}}}";
        }

        internal static string EscapeJson(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }

        internal static string BuildJsonObject(params (string key, string value)[] fields)
        {
            var sb = new StringBuilder("{");
            for (int i = 0; i < fields.Length; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append($"\"{EscapeJson(fields[i].key)}\":{fields[i].value}");
            }
            sb.Append("}");
            return sb.ToString();
        }

        internal static string BuildJsonArray(params string[] items)
        {
            return "[" + string.Join(",", items) + "]";
        }

        internal static string ToJsonString(string s)
        {
            return $"\"{EscapeJson(s)}\"";
        }
    }
}
