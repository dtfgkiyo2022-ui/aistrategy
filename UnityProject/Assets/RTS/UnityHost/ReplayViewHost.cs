using System;
using System.IO;
using Rts.Application;
using Rts.Contracts;
using Rts.Presentation;
using Rts.Replay;
using UnityEngine;

namespace Rts.UnityHost
{
    /// <summary>
    /// Plays a recorded match into the display. Viewing only: the CLI stays the verification path,
    /// but a tick whose recorded hash does not match is shown so a bad recording is never silently watched.
    /// </summary>
    public sealed class ReplayViewHost : MonoBehaviour, IMatchClock
    {
        [SerializeField] private BattlefieldView view;
        [SerializeField] private string folder = "";
        [SerializeField] private uint viewFactionId = 1;
        [SerializeField] private bool allowBuildMismatch = true;

        private ReplayPlayer player;
        private string[] files = Array.Empty<string>();
        private string status = "Select a replay file.";
        private float accumulated;
        private float tickSeconds = BattlefieldView.TickSeconds;
        private int speedMultiplier = 1;
        private bool paused;

        public string Status { get { return status; } }
        public long Tick { get { return player == null ? 0 : player.Tick; } }
        public long? DisplayMismatchTick { get { return player == null ? null : player.DisplayMismatchTick; } }
        public bool Paused { get { return paused; } set { paused = value; } }
        public int SpeedMultiplier { get { return speedMultiplier; } set { speedMultiplier = value == 2 || value == 4 ? value : 1; } }

        public uint ViewFactionId
        {
            get { return viewFactionId; }
            set
            {
                if (value != 1 && value != 2 || value == viewFactionId) return;
                viewFactionId = value;
                view.ResetVisuals();
                if (player != null && player.Tick >= 0) view.Push(player.Capture(viewFactionId));
            }
        }

        public string Folder
        {
            get { return string.IsNullOrEmpty(folder) ? Directory.GetCurrentDirectory() : folder; }
            set { folder = value; }
        }

        public string[] Files { get { return files; } }

        public void Refresh()
        {
            try
            {
                files = Directory.Exists(Folder) ? Directory.GetFiles(Folder, "*.rtsreplay") : Array.Empty<string>();
                status = files.Length + " replay file(s) in " + Folder;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
            {
                files = Array.Empty<string>();
                status = "Cannot read " + Folder + ": " + e.Message;
            }
        }

        public bool Open(string path)
        {
            Close();
            try
            {
                var stream = File.OpenRead(path);
                player = new ReplayPlayer(stream, BuildIdentityOf(), allowBuildMismatch);
                view.SetTerrain(ScenarioTerrain.From(player.Scenario.Map));
                tickSeconds = 1f / player.Scenario.TickRateHz;
                view.ResetVisuals();
                player.StepOnce();
                view.Push(player.Capture(viewFactionId));
                status = "Playing " + Path.GetFileName(path) + " (" + player.Scenario.ScenarioId + ")" + DisplayOnlyNotice;
                return true;
            }
            catch (Exception e) when (e is IOException || e is InvalidDataException || e is UnauthorizedAccessException)
            {
                Close();
                status = "Cannot open " + Path.GetFileName(path) + ": " + e.Message;
                return false;
            }
        }

        public void Close()
        {
            if (player != null) { player.Dispose(); player = null; }
            accumulated = 0f;
        }

        public void StepOneTick()
        {
            if (player == null) return;
            if (player.StepOnce()) view.Push(player.Capture(viewFactionId));
            else status = "Finished at tick " + player.Tick + MismatchText() + DisplayOnlyNotice;
        }

        // Always shown: this viewer does not verify the recording (it cannot even tell which build made it), so a
        // clean playback must not be read as a verified one. Verification is the CLI replay/compare commands.
        private const string DisplayOnlyNotice = "  [display only - not a verification; build not checked]";

        private string MismatchText()
        {
            return player != null && player.DisplayMismatchTick.HasValue ? "  hash differs at tick " + player.DisplayMismatchTick.Value : "";
        }

        // The viewer is display-side, so the build identity only decides whether a foreign recording is refused.
        private static BuildIdentity BuildIdentityOf() { return new BuildIdentity { Commit = "unity-view", SourceHash = "", Backend = "unity" }; }

        private void Update()
        {
            if (player == null || paused) return;
            accumulated += Time.deltaTime * speedMultiplier;
            while (accumulated >= tickSeconds)
            {
                accumulated -= tickSeconds;
                StepOneTick();
                if (player == null) return;
            }
        }

        private void OnDestroy() { Close(); }

        private void OnGUI()
        {
            var rect = new Rect(10f, Screen.height - 250f, 430f, 240f);
            GUI.Box(rect, "Replay");
            GUI.Label(new Rect(rect.x + 6f, rect.y + 22f, rect.width - 12f, 20f), status + MismatchText());
            folder = GUI.TextField(new Rect(rect.x + 6f, rect.y + 44f, rect.width - 90f, 22f), Folder);
            if (GUI.Button(new Rect(rect.xMax - 80f, rect.y + 44f, 74f, 22f), "Refresh")) Refresh();
            for (int i = 0; i < files.Length && i < 7; i++)
                if (GUI.Button(new Rect(rect.x + 6f, rect.y + 70f + i * 24f, rect.width - 12f, 22f), Path.GetFileName(files[i])))
                    Open(files[i]);
        }
    }
}
