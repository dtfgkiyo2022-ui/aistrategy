using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rts.Presentation
{
    /// <summary>既存操作の入口。既定値は従来の KeyCode と同じで、変更値だけ PlayerPrefs に保存する。</summary>
    public static class KeyBindings
    {
        private const string Prefix = "rts.settings.key.";
        private static readonly Dictionary<KeyBindingAction, KeyCode> values = new Dictionary<KeyBindingAction, KeyCode>();
        private static bool loaded;
        private static int escapeHandledFrame = -1;

        /// <summary>A panel used this frame's Esc to cancel something, so the settings menu must not also open.</summary>
        public static void MarkEscapeHandled() { escapeHandledFrame = Time.frameCount; }
        public static bool EscapeHandledThisFrame { get { return escapeHandledFrame == Time.frameCount; } }

        public static void Load()
        {
            values.Clear();
            foreach (var pair in KeyBindingRules.Defaults)
            {
                var action = pair.Action;
                values[action] = (KeyCode)PlayerPrefs.GetInt(Prefix + action, pair.Key);
            }
            loaded = true;
        }

        public static void Save()
        {
            EnsureLoaded();
            foreach (var pair in values) PlayerPrefs.SetInt(Prefix + pair.Key, (int)pair.Value);
            PlayerPrefs.Save();
        }

        public static void Reset()
        {
            values.Clear();
            foreach (var pair in KeyBindingRules.Defaults) values[pair.Action] = (KeyCode)pair.Key;
            loaded = true;
            Save();
        }

        public static KeyCode Get(KeyBindingAction action)
        {
            EnsureLoaded();
            KeyCode value;
            return values.TryGetValue(action, out value) ? value : KeyCode.None;
        }

        public static void Set(KeyBindingAction action, KeyCode key)
        {
            EnsureLoaded();
            var pairs = ToPairs();
            KeyBindingRules.Swap(pairs, action, (int)key);
            values.Clear();
            foreach (var pair in pairs) values[pair.Action] = (KeyCode)pair.Key;
            Save();
        }

        public static bool GetKey(KeyBindingAction action)
        {
            if (action == KeyBindingAction.AddSelection)
                return Input.GetKey(Get(action)) || Input.GetKey(KeyCode.RightShift);
            return Input.GetKey(Get(action));
        }

        public static bool GetKeyDown(KeyBindingAction action)
        {
            if (action == KeyBindingAction.AddSelection)
                return Input.GetKeyDown(Get(action)) || Input.GetKeyDown(KeyCode.RightShift);
            return Input.GetKeyDown(Get(action));
        }

        public static Vector3 CameraVector()
        {
            // The editable bindings keep the existing WASD defaults; the arrow keys remain the
            // old secondary camera controls and therefore do not consume another binding slot.
            bool right = GetKey(KeyBindingAction.CameraRight) || Input.GetKey(KeyCode.RightArrow);
            bool left = GetKey(KeyBindingAction.CameraLeft) || Input.GetKey(KeyCode.LeftArrow);
            bool up = GetKey(KeyBindingAction.CameraUp) || Input.GetKey(KeyCode.UpArrow);
            bool down = GetKey(KeyBindingAction.CameraDown) || Input.GetKey(KeyCode.DownArrow);
            float x = (right ? 1f : 0f) - (left ? 1f : 0f);
            float z = (up ? 1f : 0f) - (down ? 1f : 0f);
            return new Vector3(x, 0f, z);
        }

        public static string Display(KeyBindingAction action) { return Get(action).ToString(); }

        private static void EnsureLoaded() { if (!loaded) Load(); }

        private static List<KeyBindingPair> ToPairs()
        {
            var result = new List<KeyBindingPair>();
            foreach (var pair in KeyBindingRules.Defaults) result.Add(new KeyBindingPair(pair.Action, (int)Get(pair.Action)));
            return result;
        }
    }
}
