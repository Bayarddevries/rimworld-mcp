using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimworldMcp
{
    /// <summary>
    /// Opens the MCP chat window when F12 is pressed.
    /// Patches Root.OnGUI to intercept the key press.
    /// </summary>
    [HarmonyPatch(typeof(Root), "OnGUI")]
    public static class Root_OnGUI_Patch
    {
        private static bool _windowWasOpen = false;

        [HarmonyPostfix]
        public static void Postfix()
        {
            // Check for F12 key down
            if (Event.current != null && 
                Event.current.type == EventType.KeyDown && 
                Event.current.keyCode == KeyCode.F12 &&
                !_windowWasOpen &&
                Current.Game != null)
            {
                OpenChatWindow();
                Event.current.Use();
            }

            // Track window state
            _windowWasOpen = Find.WindowStack?.IsOpen(typeof(ChatWindow)) ?? false;
        }

        private static void OpenChatWindow()
        {
            if (Find.WindowStack == null) return;

            // Don't open if already open
            if (Find.WindowStack.IsOpen(typeof(ChatWindow)))
                return;

            Find.WindowStack.Add(new ChatWindow());
            Log.Message("[RimWorldMcp] Chat window opened (F12).");
        }
    }
}
