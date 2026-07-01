using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimworldMcp
{
    /// <summary>
    /// Harmony patches that hook into RimWorld's game lifecycle.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class HarmonyPatches
    {
        static HarmonyPatches()
        {
            var harmony = new Harmony("hermes.rimworldmcp");
            harmony.PatchAll();
            Log.Message("[RimWorldMcp] Harmony patches applied.");
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

        internal static void StartMcpServer()
        {
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                RimworldMcpMod.Instance?.StartServer();
            });
        }
    }

    /// <summary>
    /// Register our GameComponent when a new game starts.
    /// </summary>
    [HarmonyPatch(typeof(Game), "InitNewGame")]
    public static class Game_InitNewGame_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            Log.Message("[RimWorldMcp] New game detected - starting MCP bridge.");
            HarmonyPatches.EnsureComponent();
            HarmonyPatches.StartMcpServer();
        }
    }

    /// <summary>
    /// Register our GameComponent when a game is loaded.
    /// </summary>
    [HarmonyPatch(typeof(Game), "LoadGame")]
    public static class Game_LoadGame_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            Log.Message("[RimWorldMcp] Game loaded - starting MCP bridge.");
            HarmonyPatches.EnsureComponent();
            HarmonyPatches.StartMcpServer();
        }
    }

    /// <summary>
    /// Clean up when game exits.
    /// </summary>
    [HarmonyPatch(typeof(Game), "ExposeGame")]
    public static class Game_ExposeGame_Patch
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                RimworldMcpMod.Instance?.StopServer();
            }
        }
    }

    /// <summary>
    /// Tick the GameBridge every game tick so queued API requests are processed.
    /// Also runs periodic event checks.
    /// Patches Map.MapUpdate (called each tick for the current map).
    /// </summary>
    [HarmonyPatch(typeof(Map), "MapUpdate")]
    public static class Map_MapUpdate_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            RimworldMcpMod.Instance?.Bridge?.Tick();
            EventFeedManager.PeriodicCheck();
        }
    }

    /// <summary>
    /// Catch letters (raids, events, notifications) as they fire.
    /// </summary>
    [HarmonyPatch(typeof(LetterStack), "ReceiveLetter")]
    public static class LetterStack_ReceiveLetter_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Letter let)
        {
            if (let == null) return;

            string type = "letter";
            string severity = "info";
            if (let.def?.defName != null)
            {
                string defName = let.def.defName;
                if (defName.Contains("Death") || defName.Contains("Attack") || defName.Contains("Raid"))
                    severity = "critical";
                else if (defName.Contains("Threat") || defName.Contains("Danger"))
                    severity = "warning";
                type = defName;
            }

            // Use the letter's def name as fallback — base Letter class doesn't have label/Text
            string labelText = let.ToString();
            EventFeedManager.RecordEvent(type, labelText ?? "Event", severity);
        }
    }

    /// <summary>
    /// GameComponent that tracks game lifecycle for the MCP bridge.
    /// </summary>
    public class RimworldMcpGameComp : GameComponent
    {
        public RimworldMcpGameComp()
        {
        }

        public RimworldMcpGameComp(Game game)
        {
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();
        }
    }
}
