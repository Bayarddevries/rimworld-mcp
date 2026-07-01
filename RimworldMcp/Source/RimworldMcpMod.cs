using System;
using System.Threading;
using RimWorld;
using Verse;

namespace RimworldMcp
{
    public class RimworldMcpMod : Mod
    {
        public static RimworldMcpMod Instance { get; private set; }
        public GameBridge Bridge { get; private set; }
        private HttpServer _httpServer;
        private Thread _serverThread;

        public RimworldMcpMod(ModContentPack content) : base(content)
        {
            Instance = this;
            Bridge = new GameBridge();
        }

        public override void DoSettingsWindowContents(UnityEngine.Rect inRect)
        {
            base.DoSettingsWindowContents(inRect);
            // Future: add settings for port, auth token, etc.
        }

        // Called when a new game is loaded or created
        // We start the HTTP server once a colony exists
        public void StartServer()
        {
            if (_httpServer != null) return;

            _httpServer = new HttpServer(Bridge, 8765);
            _serverThread = new Thread(() =>
            {
                try
                {
                    Log.Message("[RimWorldMcp] Starting HTTP server on port 8765...");
                    _httpServer.Start();
                }
                catch (Exception ex)
                {
                    Log.Error($"[RimWorldMcp] Failed to start HTTP server: {ex.Message}");
                }
            });
            _serverThread.IsBackground = true;
            _serverThread.Start();

            Log.Message("[RimWorldMcp] HTTP server thread started.");
        }

        public void StopServer()
        {
            _httpServer?.Stop();
            _httpServer = null;
            _serverThread = null;
        }
    }
}
