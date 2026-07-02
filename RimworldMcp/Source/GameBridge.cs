using System;
using System.Collections.Generic;
using System.Threading;
using RimWorld;
using Verse;

namespace RimworldMcp
{
    /// <summary>
    /// Marshals API requests from HTTP background threads onto RimWorld's main (game) thread.
    /// All game-state access must go through here to avoid race conditions.
    /// </summary>
    public class GameBridge
    {
        private readonly Queue<Action> _pendingActions = new Queue<Action>();
        private readonly object _lock = new object();

        /// <summary>
        /// Execute an action on the game thread and wait for the result.
        /// </summary>
        public T Execute<T>(Func<T> action)
        {
            T result = default(T);
            Exception caught = null;
            var done = new ManualResetEvent(false);

            lock (_lock)
            {
                _pendingActions.Enqueue(() =>
                {
                    try
                    {
                        result = action();
                    }
                    catch (Exception ex)
                    {
                        caught = ex;
                    }
                    finally
                    {
                        done.Set();
                    }
                });
            }

            // Signal the game thread to process the queue
            done.WaitOne(TimeSpan.FromSeconds(10));

            if (caught != null)
                throw new Exception($"GameBridge execution error: {caught.Message}", caught);

            return result;
        }

        /// <summary>
        /// Execute a void action on the game thread.
        /// </summary>
        public void Execute(Action action)
        {
            Execute<bool>(() => { action(); return true; });
        }

        /// <summary>
        /// Called every game tick by Harmony patch or mod ticker.
        /// Processes all queued actions on the game thread.
        /// </summary>
        public void Tick()
        {
            lock (_lock)
            {
                while (_pendingActions.Count > 0)
                {
                    var action = _pendingActions.Dequeue();
                    action();
                }
            }
        }

        /// <summary>
        /// Check if the game is in a valid state for API calls.
        /// </summary>
        public static bool IsGameReady()
        {
            return Current.Game != null &&
                   Find.CurrentMap != null &&
                   Find.CurrentMap.mapPawns != null;
        }

        /// <summary>
        /// Get a list of all humanlike colonists on the current map.
        /// </summary>
        public static List<Pawn> GetAllColonists()
        {
            var pawns = new List<Pawn>();
            if (!IsGameReady()) return pawns;

            foreach (var pawn in Find.CurrentMap.mapPawns.FreeColonists)
            {
                pawns.Add(pawn);
            }
            return pawns;
        }

        /// <summary>
        /// Find a colonist by name or ID.
        /// </summary>
        public static Pawn FindColonist(string identifier)
        {
            foreach (var pawn in Find.CurrentMap.mapPawns.FreeColonists)
            {
                if (pawn.LabelCap == identifier ||
                    pawn.Name?.ToStringFull == identifier ||
                    pawn.thingIDNumber.ToString() == identifier)
                    return pawn;
            }
            return null;
        }

        /// <summary>
        /// Get all tamed animals (not colony mechs — API removed in 1.6).
        /// </summary>
        public static List<Pawn> GetTamedAnimals()
        {
            var animals = new List<Pawn>();
            if (!IsGameReady()) return animals;

            foreach (var pawn in Find.CurrentMap.mapPawns.AllPawns)
            {
                if (pawn.RaceProps?.Animal == true && pawn.Faction == Faction.OfPlayer)
                    animals.Add(pawn);
            }
            return animals;
        }
    }
}
