using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using Verse;

namespace RimworldMcp
{
    /// <summary>
    /// HTTP handler for the event feed API.
    /// </summary>
    public static class EventFeedHandler
    {
        /// <summary>
        /// GET /api/events/feed — get pending events since last check (clears queue).
        /// </summary>
        public static string GetFeed(HttpListenerRequest req)
        {
            var events = EventFeedManager.GetPendingEvents();
            string json = EventFeedManager.EventsToJson(events);
            return HttpServer.JsonSuccess($"{{\"count\":{events.Count},\"events\":{json}}}");
        }

        /// <summary>
        /// GET /api/events/history — get all events recorded this session.
        /// </summary>
        public static string GetHistory(HttpListenerRequest req)
        {
            var events = EventFeedManager.GetAllEvents();
            string json = EventFeedManager.EventsToJson(events);
            return HttpServer.JsonSuccess($"{{\"count\":{events.Count},\"events\":{json}}}");
        }

        /// <summary>
        /// GET /api/events/stream — Server-Sent Events endpoint.
        /// Maintains an open connection and pushes events as they happen.
        /// The client reconnects on disconnect.
        /// </summary>
        public static void StreamSSE(Stream outputStream)
        {
            // Send initial "connected" event so the client knows the stream is alive
            byte[] connected = Encoding.UTF8.GetBytes(": connected\n\n");
            outputStream.Write(connected, 0, connected.Length);
            outputStream.Flush();

            Log.Message("[RimWorldMcp] SSE client connected to event stream");

            int emptyPolls = 0;

            while (true)
            {
                try
                {
                    var events = EventFeedManager.GetPendingEvents();
                    if (events.Count > 0)
                    {
                        emptyPolls = 0;
                        string json = EventFeedManager.EventsToJson(events);
                        // SSE format: event + data fields
                        string sse = $"event: rimworld_event\ndata: {{\"count\":{events.Count},\"events\":{json}}}\n\n";
                        byte[] data = Encoding.UTF8.GetBytes(sse);
                        outputStream.Write(data, 0, data.Length);
                        outputStream.Flush();
                    }
                    else
                    {
                        emptyPolls++;
                        // Send keepalive comment every ~10 seconds
                        if (emptyPolls % 20 == 0)
                        {
                            byte[] ping = Encoding.UTF8.GetBytes(": ping\n\n");
                            outputStream.Write(ping, 0, ping.Length);
                            outputStream.Flush();
                        }
                    }

                    Thread.Sleep(500);
                }
                catch (IOException)
                {
                    // Client disconnected
                    Log.Message("[RimWorldMcp] SSE client disconnected");
                    break;
                }
                catch (System.Exception ex)
                {
                    Log.Error($"[RimWorldMcp] SSE stream error: {ex.Message}");
                    break;
                }
            }
        }
    }
}
