using System;
using RimWorld;
using Verse;

namespace RimworldMcp
{
    /// <summary>
    /// Registers our GameComponent which runs Bridge.Tick() each game tick.
    /// Also starts the HTTP server via LongEventHandler on game load.
    /// </summary>
    public static class MCPLifecycle
    {
        public static void OnGameLoaded()
        {
            // Register GameComponent FIRST so Bridge.Tick() can run
            EnsureComponent();
            // Start HTTP server directly (creates its own background thread)
            RimworldMcpMod.Instance?.StartServer();
            // Letter tracking (non-critical)
            try { EventFeedManager.Init(); }
            catch (Exception ex) { Log.Warning($"[RimWorldMcp] Event feed init failed: {ex.Message}"); }
            Log.Message("[RimWorldMcp] Bridge ready.");
        }

        internal static void EnsureComponent()
        {
            if (Current.Game == null) return;
            var existing = Current.Game.GetComponent<RimworldMcpGameComp>();
            if (existing == null)
            {
                var comp = new RimworldMcpGameComp(Current.Game);
                Current.Game.components.Add(comp);
                Log.Message("[RimWorldMcp] GameComponent registered.");
            }
        }
    }

    /// <summary>
    /// GameComponent that calls Bridge.Tick() every game tick.
    /// This is the critical mechanism that processes queued API requests.
    /// </summary>
    public class RimworldMcpGameComp : GameComponent
    {
        private int _tickCounter = 0;

        public RimworldMcpGameComp() { }

        public RimworldMcpGameComp(Game game) { }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();
            _tickCounter++;
            // Process bridge queue every tick (lightweight — only processes if queue has items)
            RimworldMcpMod.Instance?.Bridge?.Tick();
            // Run periodic checks every 500 ticks (~8 seconds at 1x speed)
            if (_tickCounter % 500 == 0)
            {
                EventFeedManager.PeriodicCheck();
            }
        }
    }
}
