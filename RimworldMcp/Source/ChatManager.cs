using System;
using System.Collections.Generic;
using Verse;

namespace RimworldMcp
{
    /// <summary>
    /// Thread-safe message store for chat between the player and Hermes.
    /// Messages are queued for the MCP server to poll.
    /// </summary>
    public static class ChatManager
    {
        // Full message history (for in-game display)
        private static readonly List<ChatMessage> _allMessages = new List<ChatMessage>();
        // Pending messages from player that Hermes hasn't seen yet
        private static readonly Queue<ChatMessage> _pendingPlayerMessages = new Queue<ChatMessage>();
        // Pending responses from Hermes that the player hasn't seen yet
        private static readonly Queue<ChatMessage> _pendingHermesMessages = new Queue<ChatMessage>();
        private static readonly object _lock = new object();

        public static void AddMessage(string sender, string text)
        {
            var msg = new ChatMessage
            {
                Sender = sender,
                Text = text,
                Timestamp = DateTime.Now
            };

            lock (_lock)
            {
                _allMessages.Add(msg);

                if (sender == "Player" || sender == "player")
                    _pendingPlayerMessages.Enqueue(msg);
                else if (sender == "Hermes" || sender == "hermes")
                    _pendingHermesMessages.Enqueue(msg);
            }
        }

        /// <summary>
        /// Get all pending player messages (for MCP to read) and clear the queue.
        /// </summary>
        public static List<ChatMessage> GetPendingPlayerMessages()
        {
            var result = new List<ChatMessage>();
            lock (_lock)
            {
                while (_pendingPlayerMessages.Count > 0)
                    result.Add(_pendingPlayerMessages.Dequeue());
            }
            return result;
        }

        /// <summary>
        /// Get all pending Hermes responses (for in-game display) and clear the queue.
        /// </summary>
        public static List<ChatMessage> GetPendingHermesMessages()
        {
            var result = new List<ChatMessage>();
            lock (_lock)
            {
                while (_pendingHermesMessages.Count > 0)
                    result.Add(_pendingHermesMessages.Dequeue());
            }
            return result;
        }

        /// <summary>
        /// Get entire message history (for in-game display).
        /// </summary>
        public static List<ChatMessage> GetAllMessages()
        {
            lock (_lock)
            {
                return new List<ChatMessage>(_allMessages);
            }
        }

        /// <summary>
        /// Serialize a list of messages to JSON array string.
        /// </summary>
        public static string MessagesToJson(List<ChatMessage> messages)
        {
            var items = new List<string>();
            foreach (var msg in messages)
            {
                items.Add("{" +
                    $"\"sender\":\"{HttpServer.EscapeJson(msg.Sender)}\"," +
                    $"\"text\":\"{HttpServer.EscapeJson(msg.Text)}\"," +
                    $"\"timestamp\":\"{msg.Timestamp:HH:mm:ss}\"" +
                    "}");
            }
            return "[" + string.Join(",", items) + "]";
        }
    }

    public class ChatMessage
    {
        public string Sender { get; set; }
        public string Text { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
