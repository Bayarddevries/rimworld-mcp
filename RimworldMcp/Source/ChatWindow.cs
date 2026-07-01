using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimworldMcp
{
    /// <summary>
    /// In-game chat window for talking to Hermes.
    /// Open with F12 hotkey (configurable).
    /// </summary>
    public class ChatWindow : Window
    {
        private string _inputText = "";
        private Vector2 _scrollPosition;
        private string _lastMessageText = "";

        public override Vector2 InitialSize => new Vector2(500f, 400f);

        public ChatWindow()
        {
            closeOnClickedOutside = true;
            closeOnAccept = false;
            closeOnCancel = true;
            absorbInputAroundWindow = true;
            doCloseX = true;
            draggable = true;
            preventCameraMotion = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Small;

            // Title
            Widgets.Label(new Rect(0f, 0f, inRect.width, 30f), "RimWorld MCP — Chat with Hermes");

            // Message history area
            float historyHeight = inRect.height - 80f;
            Rect historyRect = new Rect(0f, 35f, inRect.width, historyHeight);
            float contentHeight = 0f;
            float lineHeight = 22f;

            var messages = ChatManager.GetAllMessages();
            if (messages.Count > 0)
                contentHeight = messages.Count * lineHeight;

            Rect viewRect = new Rect(0f, 0f, historyRect.width - 20f, Mathf.Max(contentHeight, historyRect.height));
            Widgets.BeginScrollView(historyRect, ref _scrollPosition, viewRect);

            float y = 0f;
            foreach (var msg in messages)
            {
                // Color by sender
                GUI.color = msg.Sender == "Player"
                    ? new Color(0.6f, 0.8f, 1.0f)   // blue for player
                    : new Color(0.4f, 1.0f, 0.5f);   // green for Hermes

                string label = $"[{msg.Timestamp:HH:mm}] {msg.Sender}: {msg.Text}";
                Widgets.Label(new Rect(4f, y, viewRect.width - 8f, lineHeight), label);
                GUI.color = Color.white;

                y += lineHeight;
            }

            Widgets.EndScrollView();

            // Auto-scroll to bottom if new message
            if (messages.Count > 0)
            {
                string latestText = messages[messages.Count - 1].Text;
                if (latestText != _lastMessageText)
                {
                    _lastMessageText = latestText;
                    _scrollPosition.y = contentHeight;
                }
            }

            // Input area
            float inputY = inRect.height - 40f;
            Rect inputRect = new Rect(0f, inputY, inRect.width - 60f, 30f);
            Rect sendRect = new Rect(inRect.width - 55f, inputY, 55f, 30f);

            _inputText = Widgets.TextField(inputRect, _inputText);
            if (Widgets.ButtonText(sendRect, "Send"))
                SendMessage();
        }

        private void SendMessage()
        {
            if (string.IsNullOrWhiteSpace(_inputText))
                return;

            ChatManager.AddMessage("Player", _inputText.Trim());

            // Also add an immediate acknowledgment
            string playerMessage = _inputText.Trim();
            _inputText = "";

            Log.Message($"[RimWorldMcp] Player sent message to Hermes: {playerMessage}");
        }

        public override void OnAcceptKeyPressed()
        {
            SendMessage();
            Event.current.Use();
        }
    }
}
