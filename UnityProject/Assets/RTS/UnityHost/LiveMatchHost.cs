using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Rts.Application;
using Rts.Contracts;
using Rts.Presentation;
using Rts.Providers;
using Rts.Simulation;
using Rts.Tactics;
using Rts.TacticsJs;
using UnityEngine;
using Battle = Rts.Simulation.Simulation;

namespace Rts.UnityHost
{
    /// <summary>
    /// Drives a real match: Simulation + CommandGateway stepped at the scenario tick rate, with the
    /// player on faction 1 and a doctrine preset on faction 2. Display reads captured frames only.
    /// </summary>
    public sealed class LiveMatchHost : MonoBehaviour, IExternalAiControl, IMatchClock, IMatchRestart, IOpponentControl, IMapChoice, IMatchRuleChoice, IFrameSource, IPlayerFilesControl
    {
        [SerializeField] private BattlefieldView view;
        [SerializeField] private CommandPanel panel;
        [SerializeField] private uint viewFactionId = 1;
        [SerializeField] private string enemyPreset = "maintain";
        [SerializeField] private int aiDelayTicks;

        private Battle simulation;
        private ScenarioDefinition currentScenario;
        private CommandGateway gateway;
        private LiveCommandPort port;
        private PresetController enemy;
        // The opponent's side is fixed when the match starts; switching the viewed side must not hand it the other frame.
        private uint enemyFactionId = 2;
        // The player's own side may run a doctrine too. "maintain" by default, so a hands-off match runs both sides
        // alike; with "none" the player's armies wait for the player's orders, as before.
        [SerializeField] private string ownPreset = "maintain";
        private PresetController own;
        private uint ownFactionId = 1;
        private OwnDoctrineChoice ownDoctrine;

        [SerializeField] private string enemyTactic = "";
        [SerializeField] private string ownTactic = "";
        private TacticCatalogEntry[] tacticEntries = Array.Empty<TacticCatalogEntry>();
        private string[] tacticChoices = new[] { TacticMatchSetup.None };
        private TacticMatchSide enemyTacticSide;
        private TacticMatchSide ownTacticSide;
        private TacticChoice enemyTacticChoice;
        private TacticChoice ownTacticChoice;
        private MatchPackWriter matchPack;
        private string matchPackPath = "";
        [SerializeField] private bool enemyTacticAutoReload;
        [SerializeField] private bool ownTacticAutoReload;
        private readonly TacticReloadPoller tacticReloadPoller = new TacticReloadPoller(TimeSpan.FromSeconds(3));
        private TacticFileStamp enemyTacticStamp;
        private TacticFileStamp ownTacticStamp;
        private string rulebookStatus = "";

        /// <summary>The own-side doctrine picker for the setup panel; same choices as the opponent's.</summary>
        private sealed class OwnDoctrineChoice : IOpponentControl
        {
            private readonly LiveMatchHost host;
            public OwnDoctrineChoice(LiveMatchHost host) { this.host = host; }
            public string[] Choices { get { return Rts.Application.PolicyPresets.Names; } }
            public string Current
            {
                get { return host.ownTacticSide != null && host.ownTacticSide.HasTactic ? "none" : host.ownPreset; }
                set
                {
                    if (host.ownTacticSide != null && host.ownTacticSide.HasTactic) return;
                    if (value == host.ownPreset || System.Array.IndexOf(Choices, value) < 0) return;
                    host.ownPreset = value;
                    host.matchRestartRequested = true;
                }
            }
        }

        private sealed class TacticChoice : ITacticControl
        {
            private readonly LiveMatchHost host;
            private readonly bool ownSide;
            private string[] choices = new[] { TacticMatchSetup.None };
            private TacticHost tacticHost;
            private string reloadMessage = "";

            internal TacticChoice(LiveMatchHost host, bool ownSide) { this.host = host; this.ownSide = ownSide; }
            internal void Bind(string[] choices, TacticHost tacticHost)
            {
                this.choices = choices ?? new[] { TacticMatchSetup.None };
                this.tacticHost = tacticHost;
            }
            public string[] Choices { get { return choices; } }
            public string Current
            {
                get { return ownSide ? host.ownTactic : host.enemyTactic; }
                set
                {
                    if (value == Current || Array.IndexOf(choices, value) < 0) return;
                    if (ownSide) host.ownTactic = value; else host.enemyTactic = value;
                    host.matchRestartRequested = true;
                }
            }
            public bool Active { get { return tacticHost != null; } }
            public string Name { get { return tacticHost == null ? "" : tacticHost.Name; } }
            public long LastTick { get { return tacticHost == null ? -1 : tacticHost.LastCallTick; } }
            public int SentCommands { get { return tacticHost == null ? 0 : tacticHost.SentCommandCount; } }
            public int RejectedCommands { get { return tacticHost == null ? 0 : tacticHost.RejectedCommandCount; } }
            public int FailureCount { get { return tacticHost == null ? 0 : tacticHost.Failures.Count; } }
            public string LastFailureReason { get { return tacticHost == null || tacticHost.LastFailure == null ? "" : tacticHost.LastFailure.Reason; } }
            public bool Disabled { get { return tacticHost != null && tacticHost.Disabled; } }
            public IReadOnlyList<string> ConsoleLines { get { return tacticHost == null ? Array.Empty<string>() : tacticHost.RecentConsoleLines; } }
            public IReadOnlyList<TacticParamView> Parameters { get { return tacticHost == null ? Array.Empty<TacticParamView>() : ToViews(tacticHost.Parameters); } }
            private static TacticParamView[] ToViews(IReadOnlyList<TacticParamDefinition> definitions)
            {
                var views = new TacticParamView[definitions.Count];
                for (int i = 0; i < views.Length; i++) { var d = definitions[i]; views[i] = new TacticParamView(d.Name, d.Label, d.Type, d.DefaultValue, d.Min, d.Max, d.Step, d.Choices); }
                return views;
            }
            public IReadOnlyDictionary<string, object> ParamValues { get { return tacticHost == null ? new Dictionary<string, object>() : tacticHost.ParamValues; } }
            public bool SetParam(string name, object value) { return tacticHost != null && ownSide && tacticHost.SetParam(name, value); }
            public bool AutoReload
            {
                get { return ownSide ? host.ownTacticAutoReload : host.enemyTacticAutoReload; }
                set { if (ownSide) host.ownTacticAutoReload = value; else host.enemyTacticAutoReload = value; }
            }
            public string ReloadMessage { get { return reloadMessage; } }
            public bool Reload()
            {
                var result = host.TryReloadTactic(ownSide, false);
                reloadMessage = result.Success ? result.Report : result.Reason;
                return result.Success;
            }
            internal void SetReloadMessage(string message) { reloadMessage = message ?? ""; }
        }
        private float accumulated;
        private int speedMultiplier = 1;
        private bool paused;
        private float tickSeconds = BattlefieldView.TickSeconds;

        public long Tick { get { return simulation == null ? 0 : simulation.Capture(viewFactionId).Tick; } }

        public bool Paused { get { return paused; } set { paused = value; } }

        public int SpeedMultiplier
        {
            get { return speedMultiplier; }
            set { speedMultiplier = value == 2 || value == 4 ? value : 1; }
        }

        /// <summary>Verification only: shows that faction's own frame. It never shows both at once.</summary>
        public uint ViewFactionId
        {
            get { return viewFactionId; }
            set
            {
                if (value != 1 && value != 2 || value == viewFactionId) return;
                viewFactionId = value;
                if (economyPanel != null) economyPanel.Bind(gateway, viewFactionId, view, economyLayer);
                view.ResetVisuals();
                if (simulation == null) return;
                view.Push(simulation.Capture(viewFactionId));
                panel.Bind(port, viewFactionId, viewFactionId, view);
            }
        }

        /// <summary>The result overlay's "Play again": same delay and outside-AI setting, both sides back at tick 0.</summary>
        public void RestartMatch() { matchRestartRequested = true; }

        // IOpponentControl: which doctrine the opponent runs. Reads and writes the field Begin() already uses, so
        // picking a different one and restarting is the same path a fresh match always took.
        public string[] Choices { get { return Rts.Application.PolicyPresets.Names; } }
        public string Current
        {
            get { return enemyTacticSide != null && enemyTacticSide.HasTactic ? "none" : enemyPreset; }
            set
            {
                if (enemyTacticSide != null && enemyTacticSide.HasTactic) return;
                if (value == enemyPreset || System.Array.IndexOf(Choices, value) < 0) return;
                enemyPreset = value;
                matchRestartRequested = true; // both sides start again from tick 0, as with the reply delay
            }
        }

        public void StepOneTick() { StepOnce(); }
        public FactionFrame Frame { get { return simulation == null ? null : simulation.Capture(viewFactionId); } }
        public bool HasEnded { get { return simulation != null && simulation.Capture(viewFactionId).Result.HasEnded; } }
        public string MatchPackPath { get { return matchPackPath; } }

        /// <summary>Measurement only (stage 5): repeats every soldier this many times. 1 is the normal match.</summary>
        public static int ScenarioMultiplier = 1;

        /// <summary>
        /// Ver.2, off by default: when set, this faction's autonomous upper policy is decided by the external judgement
        /// model instead of being absent. It is a separate provider from the one that interprets the player's own
        /// orders, so the model never decides what the player just asked for. Turning it on sends the faction's
        /// observation to an outside service, so nothing here turns it on by itself.
        /// </summary>
        public static Func<IPolicyProvider> ExternalPolicyProvider;

        /// <summary>The environment variable the key is read from. The key is never stored, shown or logged.</summary>
        public const string KeyVariable = "PROBE_KEY";

        // What the switch on the panel controls. Off by default: a match that has not been told otherwise sends nothing.
        private bool externalEnabled;
        private bool externalRestartRequested;
        private bool matchRestartRequested;
        private JevPolicyProvider jev;
        private HttpJevTransport jevTransport;
        private IDisposable judgementTransport;
        private IPolicyProvider activeExternal;
        private LiveAiCommandPort liveAi;
        private OperationTable operationTable;

        [SerializeField] private bool fineJudgementEnabled;
        [SerializeField] private bool fineJudgementLocal;
        [SerializeField] private int fineJudgementIntervalTicks = 200;
        [SerializeField] private bool developmentAiEntry = true;
        [SerializeField] private string developmentAiInstruction = "北の拠点を守れ";
        [SerializeField] private string developmentAiModel = "local-llm";
        private string developmentAiMessage = "";
        private bool developmentAiModelListOpen;
        private const string AiInstructionControl = "AiInstruction";
        private bool aiFieldFocused;
        private Rect aiFieldRect;

        /// <summary>細かい判断を試し遊びの陣営1で使うか。変更は次の試合から有効です。</summary>
        public bool FineJudgementEnabled
        {
            get { return fineJudgementEnabled; }
            set { if (value == fineJudgementEnabled) return; fineJudgementEnabled = value; matchRestartRequested = true; }
        }

        /// <summary>Jev／ローカル LLM の判断間隔。20tick 未満は決定機会を壊さないため20に丸めます。</summary>
        public int FineJudgementIntervalTicks
        {
            get { return fineJudgementIntervalTicks; }
            set { int normalized = Math.Max(20, value); if (normalized == fineJudgementIntervalTicks) return; fineJudgementIntervalTicks = normalized; matchRestartRequested = true; }
        }

        public string FineJudgementModel { get { return fineJudgementLocal ? "local-llm" : "jev"; } }

        /// <summary>Set while a match is running with an external provider, for the display to read.</summary>
        public JevPolicyProvider ExternalProvider { get { return jev; } }

        /// <summary>画面担当が読む試し遊びのAI受け口。状態・費用・モデル一覧はこのオブジェクトから取得します。</summary>
        public LiveAiCommandPort AiCommands { get { return liveAi; } }
        public System.Collections.Generic.IReadOnlyList<LiveAiModelOption> AiModels => liveAi == null
            ? Array.Empty<LiveAiModelOption>() : liveAi.Models;
        public System.Collections.Generic.IReadOnlyList<LiveAiInstruction> AiInstructions => liveAi == null
            ? Array.Empty<LiveAiInstruction>() : liveAi.Instructions;
        public System.Collections.Generic.IReadOnlyList<LiveOperationView> AiOperations => liveAi == null
            ? Array.Empty<LiveOperationView>() : liveAi.Operations;
        public decimal AiMatchCostYen => liveAi == null ? 0m : liveAi.MatchCostYen;
        public decimal AiRemainingBudgetYen => liveAi == null ? 3m : liveAi.RemainingBudgetYen;
        public decimal AiRemainingFreeYen => 0m;

        /// <summary>Public placement supplied to the temporary strategist UI and to hit-area registration.</summary>
        public Rect AiPanelRect { get { return UiLayout.Calculate(Screen.width, Screen.height).Strategist; } }

        public bool KeyAvailable { get { return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(KeyVariable)); } }

        public bool Enabled
        {
            get { return externalEnabled; }
            set
            {
                if (value == externalEnabled) return;
                if (value && !KeyAvailable) return; // nothing to ask with; the panel says so
                externalEnabled = value;
                externalRestartRequested = true;    // both sides start again from tick 0, as with the reply delay
            }
        }

        public string Status
        {
            get
            {
                if (jev == null) return externalEnabled ? "Starting..." : "Off.";
                string cost = "~$" + (jevTransport.InputTokens * 42 / 1000000m).ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture);
                string line = jev.Availability == JevAvailability.Paused
                    ? "Paused after repeated failures; resumes at tick " + jev.ResumeTick + ". The automatic AI is playing alone."
                    : "Asking. Orders given: " + gatewayOrders + ", declined: " + jev.DeclinedCount + ", repeats skipped: " + jev.SuppressedCount;
                return line + "\nFailed calls: " + jev.FailureCount + "   Used: " + cost;
            }
        }

        private int gatewayOrders;

        private void StopExternal()
        {
            if (jev != null) jev.Dispose();
            jev = null;
            jevTransport = null;
            if (judgementTransport != null) judgementTransport.Dispose();
            judgementTransport = null;
            activeExternal = null;
            gatewayOrders = 0;
        }

        private void StopLiveAi()
        {
            if (liveAi != null) liveAi.Dispose();
            liveAi = null;
        }

        private void OnDestroy() { StopTactics(); StopLiveAi(); StopExternal(); }

        private void OnDisable() { StopTactics(); }

        private void StopTactics()
        {
            ownTacticSide?.Host?.Dispose();
            enemyTacticSide?.Host?.Dispose();
        }

        // IMapChoice (Ver.3): a random map with the economy, or the Ver.1 two-road map. The seed is picked here, outside
        // the simulation, and the generated map goes into the replay whole, so the wall clock never reaches a decision.
        [SerializeField] private bool economyMap = true;
        // S-2 trial switch. It is deliberately separate from EconomyMap and defaults off; presentation bounds are not
        // changed here because the presentation owner handles the large-map camera and overlay work.
        [SerializeField] private bool largeMap = false;
        private ulong mapSeed;
        private EconomyLayer economyLayer;
        private EconomyPanel economyPanel;

        public bool EconomyMap
        {
            get { return economyMap; }
            set { if (value == economyMap) return; economyMap = value; matchRestartRequested = true; }
        }

        public bool LargeMap
        {
            get { return largeMap; }
            set { if (value == largeMap) return; largeMap = value; matchRestartRequested = true; }
        }

        public ulong Seed { get { return mapSeed; } }

        public void NewMap() { mapSeed = FreshSeed(); matchRestartRequested = true; }

        [SerializeField] private bool monks = false, ageVictory = false;

        public bool Monks
        {
            get { return monks; }
            set { if (value == monks) return; monks = value; matchRestartRequested = true; }
        }

        public bool AgeVictory
        {
            get { return ageVictory; }
            set { if (value == ageVictory) return; ageVictory = value; matchRestartRequested = true; }
        }

        // Off by default so the usual match keeps the first two civilisations; on opens all fourteen for trying them.
        [SerializeField] private bool allCivilisations = false;

        public bool AllCivilisations
        {
            get { return allCivilisations; }
            set { if (value == allCivilisations) return; allCivilisations = value; matchRestartRequested = true; }
        }

        private static ulong FreshSeed() { return (ulong)(DateTime.UtcNow.Ticks % 1000000L) + 1UL; }

        FactionFrame IFrameSource.Latest(uint factionId)
        {
            return simulation == null ? null : simulation.Capture(factionId);
        }

        private void RefreshTacticCatalog()
        {
            var parents = new List<string>();
            if (UnityEngine.Application.isEditor)
                parents.Add(Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "..", "..", "TacticSamples")));
            else
                parents.Add(Path.Combine(UnityEngine.Application.streamingAssetsPath, "TacticSamples"));
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (!string.IsNullOrEmpty(documents)) parents.Add(Path.Combine(documents, "AiCommandRts", "Tactics"));

            var entries = TacticCatalog.Scan(parents, Path.Combine(UnityEngine.Application.streamingAssetsPath, "TacticRuntimes"));
            tacticEntries = new List<TacticCatalogEntry>(entries).ToArray();
            var choices = new List<string> { TacticMatchSetup.None };
            foreach (var entry in tacticEntries) if (entry.IsSelectable) choices.Add(entry.Path);
            tacticChoices = choices.ToArray();
            if (Array.IndexOf(tacticChoices, ownTactic) < 0) ownTactic = TacticMatchSetup.None;
            if (Array.IndexOf(tacticChoices, enemyTactic) < 0) enemyTactic = TacticMatchSetup.None;
        }

        public void OpenTacticsFolder()
        {
            OpenPlayerFolder("Tactics");
        }

        public void OpenPacksFolder()
        {
            OpenPlayerFolder("Packs");
        }

        public string RulebookStatus { get { return rulebookStatus; } }

        public void ExportRulebook()
        {
            if (currentScenario == null)
            {
                rulebookStatus = "ルールブックを書き出せません：試合が開始されていません。";
                return;
            }
            try
            {
                string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (string.IsNullOrEmpty(documents)) throw new IOException("Documentsフォルダが見つかりません。");
                string folder = Path.Combine(documents, "AiCommandRts", "Rulebook");
                Directory.CreateDirectory(folder);
                var book = TacticRulebook.Write(currentScenario);
                File.WriteAllText(Path.Combine(folder, "rulebook.md"), book.markdown, new System.Text.UTF8Encoding(false));
                File.WriteAllText(Path.Combine(folder, "rulebook.json"), book.json, new System.Text.UTF8Encoding(false));
                rulebookStatus = "ルールブックを書き出しました：" + folder;
                OpenFolder(folder);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is InvalidOperationException)
            {
                rulebookStatus = "ルールブックを書き出せません：" + e.Message;
                Debug.LogWarning(rulebookStatus);
            }
        }

        public void RefreshTacticList()
        {
            RefreshTacticCatalog();
            if (enemyTacticChoice != null) enemyTacticChoice.Bind(tacticChoices, enemyTacticSide == null ? null : enemyTacticSide.Host);
            if (ownTacticChoice != null) ownTacticChoice.Bind(tacticChoices, ownTacticSide == null ? null : ownTacticSide.Host);
        }

        private static void OpenPlayerFolder(string leaf)
        {
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrEmpty(documents)) return;
            string folder = Path.Combine(documents, "AiCommandRts", leaf);
            Directory.CreateDirectory(folder);
            UnityEngine.Application.OpenURL("file:///" + folder.Replace('\\', '/'));
        }

        private static void OpenFolder(string folder)
        {
            UnityEngine.Application.OpenURL("file:///" + folder.Replace('\\', '/'));
        }

        private static ITacticRuntime LoadTacticRuntime(string path)
        {
            var loaded = TacticFolder.Load(path, Path.Combine(UnityEngine.Application.streamingAssetsPath, "TacticRuntimes"));
            if (!loaded.IsSuccess) throw new InvalidDataException("戦術フォルダを読み込めません: " + loaded.Error);
            return loaded.Runtime;
        }

        /// <summary>
        /// A tactic folder that stopped loading (edited or removed after the scan) must not stop the match from
        /// starting: that side falls back to no tactic and the reason goes to the console.
        /// </summary>
        private TacticMatchSide CreateTacticSide(uint factionId, ref string selection)
        {
            try { return TacticMatchSetup.Create(factionId, selection, LoadTacticRuntime, this, gateway, gateway, null, scope => gateway.FactionVersions(factionId).Versions(scope)); }
            catch (Exception e) when (e is InvalidDataException || e is InvalidOperationException || e is ArgumentException)
            {
                Debug.LogWarning("Tactic for faction " + factionId + " could not be loaded; playing without it: " + e.Message);
                selection = TacticMatchSetup.None;
                return TacticMatchSetup.Create(factionId, TacticMatchSetup.None, LoadTacticRuntime, this, gateway, gateway);
            }
        }

        private static string TacticSetupJson(ScenarioDefinition scenario, uint factionId)
        {
            return "{\"matchSeed\":" + scenario.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ",\"factionId\":" + factionId.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ",\"map\":{\"widthMeters\":" + scenario.Map.WidthMeters
                + ",\"heightMeters\":" + scenario.Map.HeightMeters
                + ",\"widthCells\":" + scenario.Map.WidthCells
                + ",\"heightCells\":" + scenario.Map.HeightCells + "}}";
        }

        public void Begin()
        {
            StopTactics();
            if (mapSeed == 0) mapSeed = FreshSeed();
            RefreshTacticCatalog();
            // Stage-5 measurement tools scale the Ver.1 map; they always get it.
            // V3-4: the random map is the terrain map (mapgen-3): forests, a river, mountains, and the industry of mapgen-2.
            // The academy needs gold on the map, so the all-civilisations match asks the generator for the gold placement too.
            var scenario = economyMap && ScenarioMultiplier == 1 ? (largeMap ? MapGenerator.GenerateLarge(mapSeed, gold: allCivilisations)
                : MapGenerator.GenerateTerrain(mapSeed, gold: allCivilisations))
                : ScenarioScale.Multiply(WeekTwoScenario.Create(), ScenarioMultiplier);
            currentScenario = scenario;
            if (economyMap && ScenarioMultiplier == 1)
            {
                scenario.Economy.MonksEnabled = monks;
                scenario.Economy.AgeVictoryEnabled = ageVictory;
                if (allCivilisations)
                {
                    scenario.Economy.Forestry = true;
                    scenario.Economy.Masonry = true;
                    scenario.Economy.Caravan = true;
                    scenario.Economy.Cavalry = true;
                    scenario.Economy.Bridge = true;
                    scenario.Economy.Academy = true;
                    scenario.Economy.Cult = true;
                    scenario.Economy.MonksEnabled = true; // the cult trains monks, so the monk rules come with it
                    scenario.Economy.Mountain = true;
                    scenario.Economy.FishingCiv = true;
                    scenario.Economy.FishingEnabled = true; // the fishing civilisation needs the river fish
                    scenario.Economy.Tollgate = true;
                    scenario.Economy.Metropolis = true;
                    scenario.Economy.Sanctuary = true;
                    scenario.Economy.CoreDefence = true;
                    scenario.Economy.ArmyGrowth = true;
                    scenario.Economy.EconomyScale = true;
                    scenario.Economy.LatePush = true;
                }
            }
            tickSeconds = 1f / scenario.TickRateHz;
            simulation = new Battle(scenario);
            matchPack = null;
            matchPackPath = "";
            operationTable = new OperationTable(viewFactionId);
            var provider = aiDelayTicks == 0 ? null : new DelayedPolicyProvider(aiDelayTicks, r => port.Interpret(r));
            StopLiveAi();
            StopExternal();
            IPolicyProvider external = null;
            if (ExternalPolicyProvider != null) external = ExternalPolicyProvider();
            else if (externalEnabled && KeyAvailable)
            {
                jevTransport = new HttpJevTransport(() => Environment.GetEnvironmentVariable(KeyVariable));
                jev = new JevPolicyProvider(jevTransport);
                jev.Observe = record => { if (record.OrderCount > 0) gatewayOrders++; };
                external = jev;
            }
            else if (fineJudgementEnabled)
            {
                if (fineJudgementLocal)
                {
                    var local = new LocalLlmTransport(
                        Environment.GetEnvironmentVariable("LOCAL_LLM_URL") ?? LocalLlmTransport.DefaultUrl,
                        Environment.GetEnvironmentVariable("LOCAL_LLM_MODEL") ?? LocalLlmTransport.DefaultModel);
                    judgementTransport = local;
                    jev = new JevPolicyProvider(local);
                    external = jev;
                }
                else if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TYPESAFE_API_KEY")))
                {
                    jevTransport = new HttpJevTransport(() => Environment.GetEnvironmentVariable("TYPESAFE_API_KEY"));
                    judgementTransport = null; // HttpJevTransport is not IDisposable: there is nothing to release
                    jev = new JevPolicyProvider(jevTransport);
                    external = jev;
                }
            }
            activeExternal = external;
            AutonomousPollSchedule externalSchedule = null;
            if (external != null)
                externalSchedule = AutonomousPollSchedule.OnChange(fineJudgementEnabled ? Math.Max(20, fineJudgementIntervalTicks) : 600);
            gateway = new CommandGateway(simulation, provider, null,
                externalSchedule, external);
            port = new LiveCommandPort(gateway, aiDelayTicks);
            liveAi = new LiveAiCommandPort(gateway, () => Frame, operationTable: operationTable,
                changeDoctrine: preset => SwitchOwnDoctrine(preset),
                changeTacticParam: (name, value) => TrySetOwnTacticParam(name, value),
                switchTactic: name => TrySwitchOwnTactic(name),
                enrichSummary: AddOwnTacticInfo);
            enemyFactionId = 3 - viewFactionId;
            enemyTacticSide = CreateTacticSide(enemyFactionId, ref enemyTactic);
            if (enemyTacticSide.Host != null) enemyTacticSide.Host.Start(TacticSetupJson(scenario, enemyFactionId));
            enemy = PolicyPresets.CreateController(enemyTacticSide.HasTactic ? "none" : enemyPreset, enemyFactionId, gateway);
            enemy.Initialize();
            ownFactionId = viewFactionId;
            ownTacticSide = CreateTacticSide(ownFactionId, ref ownTactic);
            if (ownTacticSide.Host != null) ownTacticSide.Host.Start(TacticSetupJson(scenario, ownFactionId));
            // An outside AI already steers the own side; a doctrine on top of it would fight it.
            own = PolicyPresets.CreateController(external != null || ownTacticSide.HasTactic ? "none" : ownPreset, ownFactionId, gateway);
            own.Initialize();
            if (external != null)
                gateway.EnableAutonomous(new UserPolicyIntent(0, new ScopeKey(viewFactionId, ScopeKind.All, 0),
                    PolicyKind.Focus, default(PolicyGoal), 50, new LossBudget(300),
                    new EndCondition(EndKind.UntilReplaced, 0), 0, new Expiration(long.MaxValue, 0, ExpireFlags.None)));

            view.SetTerrain(ScenarioTerrain.From(scenario.Map));
            view.Push(simulation.Capture(viewFactionId));
            TryStartMatchPack(scenario);
            panel.Bind(port, viewFactionId, viewFactionId, view);
            panel.ExternalAi = this;
            panel.MatchRestart = this;
            panel.Opponent = this;
            if (ownDoctrine == null) ownDoctrine = new OwnDoctrineChoice(this);
            panel.OwnDoctrine = ownDoctrine;
            if (enemyTacticChoice == null) enemyTacticChoice = new TacticChoice(this, false);
            if (ownTacticChoice == null) ownTacticChoice = new TacticChoice(this, true);
            enemyTacticChoice.Bind(tacticChoices, enemyTacticSide.Host);
            ownTacticChoice.Bind(tacticChoices, ownTacticSide.Host);
            enemyTacticChoice.SetReloadMessage("");
            ownTacticChoice.SetReloadMessage("");
            enemyTacticStamp = TacticFileStamp.Capture(enemyTactic);
            ownTacticStamp = TacticFileStamp.Capture(ownTactic);
            panel.OpponentTactic = enemyTacticChoice;
            panel.OwnTactic = ownTacticChoice;
            // Added at run time so the scene file stays as it is. Unity's fake null defeats ??, hence the explicit checks.
            if (economyLayer == null) economyLayer = GetComponent<EconomyLayer>();
            if (economyLayer == null) economyLayer = gameObject.AddComponent<EconomyLayer>();
            if (economyPanel == null) economyPanel = GetComponent<EconomyPanel>();
            if (economyPanel == null) economyPanel = gameObject.AddComponent<EconomyPanel>();
            economyLayer.Clear();
            economyLayer.Bind(view);
            economyPanel.Bind(gateway, viewFactionId, view, economyLayer);
            economyPanel.ExtraCivilisations = economyMap && ScenarioMultiplier == 1 && allCivilisations;
            panel.MapChoice = this;
            panel.MatchRuleChoice = this;
            panel.MatchPackPathProvider = () => MatchPackPath;
            panel.PlayerFiles = this;
            panel.LanguageChanged = japanese => { PlayerPrefs.SetInt(LanguageKey, japanese ? 1 : 0); PlayerPrefs.Save(); };
            panel.ExtraBlocksClick = economyPanel.BlocksClick;
            panel.ExtraGroundClick = economyPanel.TryConsumeGroundClick;
        }

        /// <summary>Verification entry: sends a standard command through the same port the UI uses.</summary>
        public ulong Submit(UserPolicyIntent intent) { return port.Submit(intent); }

        /// <summary>画面から呼ぶ参謀入口。予約はこの呼び出しの中で話し始めた時点に開きます。</summary>
        public ulong Speak(string instruction, ScopeKey? fixedTarget, string model)
        {
            if (liveAi == null) throw new InvalidOperationException("試合が開始されていません。");
            return liveAi.BeginInterpretation(instruction, fixedTarget, model);
        }

        /// <summary>
        /// Changes the player's doctrine without restarting the match.  The old doctrine is cleared by a logged
        /// Doctrine ReturnToAuto input, then the replacement controller observes the current tick before proposing.
        /// The setup choice is updated as well, while the setup panel still restarts a match when changed directly.
        /// </summary>
        public bool SwitchOwnDoctrine(string preset)
        {
            if (simulation == null || gateway == null || port == null) return false;
            if (activeExternal != null) return false;
            if (ownTacticSide != null && ownTacticSide.HasTactic) return false;
            if (Array.IndexOf(PolicyPresets.Names, preset) < 0)
                throw new ArgumentException("Preset must be none, maintain, maintain-legacy or concentrate.", nameof(preset));
            if (preset == ownPreset) return true;
            long tick = simulation.Capture(ownFactionId).Tick;
            // ResetDoctrine is logged before the new controller's proposal.  Both apply at tick+1, so every old
            // doctrine field is removed first; this is also why switching to none really returns to human control.
            gateway.ResetDoctrine(ownFactionId, checked(tick + 1));
            var replacement = PolicyPresets.CreateController(preset, ownFactionId, gateway);
            replacement.Initialize(tick);
            own = replacement;
            ownPreset = preset;
            return true;
        }

        public bool SetOwnTacticParam(string name, string value)
        {
            return TrySetOwnTacticParam(name, value).Success;
        }

        private AiTacticChangeResult TrySetOwnTacticParam(string name, string value)
        {
            var host = ownTacticSide == null ? null : ownTacticSide.Host;
            if (host == null) return AiTacticChangeResult.Fail("自軍に戦術がないため、つまみを変えられません。");
            TacticParamDefinition definition = null;
            foreach (var candidate in host.Parameters) if (candidate.Name == name) { definition = candidate; break; }
            if (definition == null) return AiTacticChangeResult.Fail("つまみが見つかりません: " + (name ?? ""));
            object parsed;
            string parseReason;
            if (!TryParseTacticValue(definition, value, out parsed, out parseReason)) return AiTacticChangeResult.Fail(parseReason);
            object before = host.ParamValues[name];
            if (!host.TrySetParam(name, parsed, out var reason)) return AiTacticChangeResult.Fail(reason);
            object after = host.ParamValues[name];
            return AiTacticChangeResult.Ok(definition.Label + "を " + TacticValue(before) + " → " + TacticValue(after) + " にしました");
        }

        public bool SwitchOwnTactic(string name)
        {
            return TrySwitchOwnTactic(name).Success;
        }

        private AiTacticChangeResult TrySwitchOwnTactic(string name)
        {
            if (simulation == null || gateway == null || currentScenario == null) return AiTacticChangeResult.Fail("試合が開始されていません。");
            string selection = ResolveTacticSelection(name);
            if (selection == null) return AiTacticChangeResult.Fail("戦術名が見つかりません: " + (name ?? ""));
            if (selection == ownTactic) return AiTacticChangeResult.Ok("戦術はすでに " + (ownTacticSide != null && ownTacticSide.Host != null ? ownTacticSide.Host.Name : "なし") + " です");

            TacticMatchSide replacement;
            try
            {
                replacement = TacticMatchSetup.Create(ownFactionId, selection, LoadTacticRuntime, this, gateway, gateway, null,
                    scope => gateway.FactionVersions(ownFactionId).Versions(scope));
                replacement.Host?.Start(TacticSetupJson(currentScenario, ownFactionId));
            }
            catch (Exception e) when (e is InvalidDataException || e is InvalidOperationException || e is ArgumentException)
            {
                return AiTacticChangeResult.Fail("戦術を読み込めません: " + e.Message);
            }

            long tick = simulation.Capture(ownFactionId).Tick;
            gateway.ResetDoctrine(ownFactionId, checked(tick + 1));
            ownTacticSide?.Host?.Dispose();
            ownTacticSide = replacement;
            ownTactic = selection;
            own = PolicyPresets.CreateController(replacement.HasTactic ? "none" : ownPreset, ownFactionId, gateway);
            own.Initialize(tick);
            ownTacticChoice?.Bind(tacticChoices, ownTacticSide.Host);
            ownTacticStamp = TacticFileStamp.Capture(ownTactic);
            ownTacticChoice?.SetReloadMessage("");
            string report = replacement.HasTactic ? "戦術を " + replacement.Host.Name + " に切り替えました" : "戦術をやめ、お任せに戻しました";
            return AiTacticChangeResult.Ok(report);
        }

        private AiTacticChangeResult TryReloadTactic(bool ownSide, bool automatic)
        {
            if (simulation == null || gateway == null || currentScenario == null)
                return AiTacticChangeResult.Fail("試合が開始されていません。");
            var oldSide = ownSide ? ownTacticSide : enemyTacticSide;
            var oldHost = oldSide == null ? null : oldSide.Host;
            string selection = ownSide ? ownTactic : enemyTactic;
            uint factionId = ownSide ? ownFactionId : enemyFactionId;
            if (oldHost == null || string.IsNullOrEmpty(selection))
                return AiTacticChangeResult.Fail("読み直す戦術が選ばれていません。");

            TacticMatchSide replacement = null;
            try
            {
                replacement = TacticReloadBuilder.CreateReplacement(factionId, selection, LoadTacticRuntime, this, gateway, gateway,
                    oldHost, TacticSetupJson(currentScenario, factionId), null,
                    scope => gateway.FactionVersions(factionId).Versions(scope));
            }
            catch (Exception e)
            {
                replacement?.Host?.Dispose();
                string reason = "戦術の読み直しに失敗しました: " + e.Message;
                RecordTacticReload(factionId, selection, false, automatic, reason);
                if (ownSide) ownTacticChoice?.SetReloadMessage(reason); else enemyTacticChoice?.SetReloadMessage(reason);
                return AiTacticChangeResult.Fail(reason);
            }

            long tick = simulation.Capture(factionId).Tick;
            gateway.ResetDoctrine(factionId, checked(tick + 1));
            oldHost.Dispose();
            if (ownSide)
            {
                ownTacticSide = replacement;
                own = PolicyPresets.CreateController("none", ownFactionId, gateway);
                own.Initialize(tick);
                ownTacticChoice?.Bind(tacticChoices, replacement.Host);
                ownTacticChoice?.SetReloadMessage("戦術を読み直しました：" + replacement.Host.Name);
                ownTacticStamp = TacticFileStamp.Capture(selection);
            }
            else
            {
                enemyTacticSide = replacement;
                enemy = PolicyPresets.CreateController("none", enemyFactionId, gateway);
                enemy.Initialize(tick);
                enemyTacticChoice?.Bind(tacticChoices, replacement.Host);
                enemyTacticChoice?.SetReloadMessage("戦術を読み直しました：" + replacement.Host.Name);
                enemyTacticStamp = TacticFileStamp.Capture(selection);
            }
            string report = (automatic ? "自動で" : "") + "戦術を読み直しました：" + replacement.Host.Name;
            RecordTacticReload(factionId, selection, true, automatic, report);
            return AiTacticChangeResult.Ok(report);
        }

        private void RecordTacticReload(uint factionId, string selection, bool success, bool automatic, string reason)
        {
            if (matchPack == null) return;
            long tick = simulation == null ? 0 : simulation.Capture(factionId).Tick;
            matchPack.RecordTacticReload(DateTime.UtcNow, tick, factionId, selection, success, automatic, reason);
        }

        private void CheckAutomaticTacticReloads()
        {
            if (!tacticReloadPoller.ShouldCheck(DateTime.UtcNow)) return;
            CheckAutomaticTacticReload(false);
            CheckAutomaticTacticReload(true);
        }

        private void CheckAutomaticTacticReload(bool ownSide)
        {
            var choice = ownSide ? ownTacticChoice : enemyTacticChoice;
            if (choice == null || !choice.AutoReload || !choice.Active) return;
            string selection = ownSide ? ownTactic : enemyTactic;
            var current = TacticFileStamp.Capture(selection);
            var previous = ownSide ? ownTacticStamp : enemyTacticStamp;
            if (current == previous) return;
            var result = TryReloadTactic(ownSide, true);
            choice.SetReloadMessage(result.Success ? result.Report : result.Reason);
            if (ownSide) ownTacticStamp = current; else enemyTacticStamp = current;
        }

        private string ResolveTacticSelection(string name)
        {
            if (string.IsNullOrEmpty(name) || name == "なし") return TacticMatchSetup.None;
            foreach (var entry in tacticEntries ?? Array.Empty<TacticCatalogEntry>())
                if (entry.IsSelectable && (entry.Path == name || entry.DisplayName == name || entry.FolderName == name)) return entry.Path;
            return null;
        }

        private void AddOwnTacticInfo(AiSituationSummary summary)
        {
            var host = ownTacticSide == null ? null : ownTacticSide.Host;
            var available = (tacticEntries ?? Array.Empty<TacticCatalogEntry>()).Where(x => x.IsSelectable).Select(x => x.DisplayName);
            var parameters = host == null ? Array.Empty<AiTacticParameterInfo>() : host.Parameters.Select(d => new AiTacticParameterInfo(d.Name, d.Label, d.Type,
                host.ParamValues.TryGetValue(d.Name, out var value) ? TacticValue(value) : TacticValue(d.DefaultValue),
                d.Min, d.Max, d.Step, d.Choices)).ToArray();
            summary.SetTacticInfo(host == null ? "" : host.Name, available, parameters);
        }

        private static bool TryParseTacticValue(TacticParamDefinition definition, string text, out object value, out string reason)
        {
            value = null; reason = null; text = text ?? "";
            if (definition.Type == "bool")
            {
                if (text == "true") { value = true; return true; }
                if (text == "false") { value = false; return true; }
                reason = "boolはtrueまたはfalseです。"; return false;
            }
            if (definition.Type == "choice") { value = text; return true; }
            if (definition.Type == "int" && int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var integer)) { value = integer; return true; }
            if (definition.Type == "number" && decimal.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number)) { value = number; return true; }
            reason = definition.Type + "は数値でなければなりません。"; return false;
        }

        private static string TacticValue(object value)
        {
            if (value is bool boolean) return boolean ? "true" : "false";
            if (value is IFormattable formattable) return formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture);
            return value == null ? "" : value.ToString();
        }

        public decimal EstimateAiCost(string instruction, string model, ScopeKey? fixedTarget = null)
        {
            if (liveAi == null) throw new InvalidOperationException("試合が開始されていません。");
            return liveAi.Estimate(instruction, model, fixedTarget);
        }

        /// <summary>LLMを呼ばず、直前の解釈中／実行中の指示を取り消します。</summary>
        public bool CancelLastAiInstruction() { return liveAi != null && liveAi.CancelLast(); }

        /// <summary>試し遊びの作戦表から指定 ID の作戦を取り消します。</summary>
        public bool CancelAiOperation(ulong operationId) { return liveAi != null && liveAi.CancelOperation(operationId); }

        public void StepOnce()
        {
            if (simulation == null || HasEnded) return;
            // Tactic calls are deliberately before the gateway step, matching tactic-match in the CLI.
            TacticHostTickResult ownTacticResult = null;
            TacticHostTickResult enemyTacticResult = null;
            if (ownFactionId == 1)
            {
                if (ownTacticSide != null && ownTacticSide.Host != null) ownTacticResult = ownTacticSide.Host.Tick();
                if (enemyTacticSide != null && enemyTacticSide.Host != null) enemyTacticResult = enemyTacticSide.Host.Tick();
            }
            else
            {
                if (enemyTacticSide != null && enemyTacticSide.Host != null) enemyTacticResult = enemyTacticSide.Host.Tick();
                if (ownTacticSide != null && ownTacticSide.Host != null) ownTacticResult = ownTacticSide.Host.Tick();
            }
            if (matchPack != null)
            {
                matchPack.RecordTactic(ownTacticResult, ownFactionId, ownTactic);
                matchPack.RecordTactic(enemyTacticResult, enemyFactionId, enemyTactic);
            }
            gateway.Step();
            var frame = simulation.Capture(viewFactionId);
            view.Push(frame);
            if (liveAi != null) liveAi.Poll(frame.Tick);
            matchPack?.RecordAfterStep(simulation);
            if (frame.Result.HasEnded)
            {
                FinishMatchPack();
                return;
            }
            enemy.Step(simulation.Capture(enemyFactionId));
            own.Step(simulation.Capture(ownFactionId));
            if (HasEnded) FinishMatchPack();
        }

        private void TryStartMatchPack(ScenarioDefinition scenario)
        {
            try
            {
                string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                string root = Path.Combine(documents, "AiCommandRts", "Packs");
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", System.Globalization.CultureInfo.InvariantCulture);
                matchPackPath = Path.Combine(root, stamp + "_" + scenario.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture));
                matchPack = new MatchPackWriter(matchPackPath, scenario, string.IsNullOrEmpty(ownTactic) ? ownPreset : ownTactic, string.IsNullOrEmpty(enemyTactic) ? enemyPreset : enemyTactic);
                matchPack.RecordInitial(simulation);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is InvalidOperationException)
            {
                matchPack = null;
                matchPackPath = "";
                Debug.LogWarning("Could not start match pack; the match continues: " + e.Message);
            }
        }

        private void FinishMatchPack()
        {
            StopTactics();
            if (matchPack == null) return;
            try
            {
                matchPack.Complete(simulation, gateway.Inputs, simulation.Capture(1).Tick);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is InvalidOperationException)
            {
                Debug.LogWarning("Could not write match pack; the match has ended: " + e.Message);
                matchPackPath = "";
            }
            finally { matchPack = null; }
        }

        private const string LanguageKey = "rts.language.japanese";

        private void Start()
        {
            // Japanese by default for play-testing; the choice is remembered on this PC (display only, never simulated).
            UiText.Japanese = PlayerPrefs.GetInt(LanguageKey, 1) == 1;
            Begin();
        }

        private void Update()
        {
            if (simulation == null) return;
            // Deliberately polled on Unity's main thread. This is a presentation convenience and never enters the
            // simulation's decision path; the wall clock keeps it independent of pause and match speed.
            CheckAutomaticTacticReloads();
            if (port.RestartRequested || externalRestartRequested || matchRestartRequested)
            {
                aiDelayTicks = port.DelayTicks;
                externalRestartRequested = false;
                matchRestartRequested = false;
                accumulated = 0f;
                Begin();
                return;
            }
            if (paused) return;
            accumulated += Time.deltaTime * speedMultiplier;
            while (accumulated >= tickSeconds)
            {
                accumulated -= tickSeconds;
                StepOnce();
            }
        }

        /// <summary>
        /// Where the IME candidate list is asked to open. Windows puts the list's top about one field height above the
        /// point it is given (seen in a player build, 10-05), so the point is a field height plus a margin below the
        /// field's bottom; the list then starts just under the field instead of covering it.
        /// </summary>
        private static Vector2 ImeCandidatePosition(Rect field)
        {
            return new Vector2(field.x, field.yMax + field.height + 8f);
        }

        private void LateUpdate()
        {
            if (aiFieldFocused) Input.compositionCursorPos = ImeCandidatePosition(aiFieldRect);
        }

        /// <summary>
        /// Temporary play-test entry owned by the host. The production chat UI can call Speak directly; this small
        /// inspector-friendly panel keeps the G-5 path usable before that UI exists.
        /// </summary>
        private void OnGUI()
        {
            if (!developmentAiEntry || liveAi == null || simulation == null) return;
            // In front of the other panels, so nothing drawn later can sit over the staff panel and take its clicks.
            GUI.depth = -10;
            UiStyles.Begin();
            UiHitAreas.Shared.BeginFrame(Time.frameCount);
            // IMGUI text fields only receive Japanese (IME) composition when the mode is forced on.
            Input.imeCompositionMode = IMECompositionMode.On;
            var rect = AiPanelRect;
            UiStyles.Box(rect, "試し遊び：参謀");
            UiHitAreas.Shared.Register(rect);
            var field = new Rect(rect.x + 8f, rect.y + 26f, rect.width - 16f, 24f);
            bool focused = GUI.GetNameOfFocusedControl() == AiInstructionControl;
            // Enter sends, but not while the IME is still converting: then Enter only confirms the conversion.
            var current = Event.current;
            bool enter = focused && current.type == EventType.KeyDown
                && (current.keyCode == KeyCode.Return || current.keyCode == KeyCode.KeypadEnter)
                && string.IsNullOrEmpty(Input.compositionString);
            if (enter) current.Use();
            GUI.SetNextControlName(AiInstructionControl);
            developmentAiInstruction = GUI.TextField(field, developmentAiInstruction ?? "");
            // The IME candidate list opens where this says. The field itself keeps resetting it (to a corner of the Game
            // view, 10-05), so it is set again here and once more in LateUpdate, after all GUI events of the frame.
            aiFieldFocused = focused;
            aiFieldRect = field;
            if (focused) Input.compositionCursorPos = ImeCandidatePosition(field);
            if (GUI.Button(new Rect(rect.x + 8f, rect.y + 54f, rect.width - 156f, 24f), ModelLabel(developmentAiModel) + "  ▼"))
                developmentAiModelListOpen = !developmentAiModelListOpen;
            if (GUI.Button(new Rect(rect.x + rect.width - 140f, rect.y + 54f, 132f, 24f), "送る") || enter)
            {
                developmentAiModelListOpen = false;
                try { Speak(developmentAiInstruction, null, developmentAiModel); developmentAiMessage = ""; }
                catch (Exception e) { developmentAiMessage = e.Message; }
            }
            if (!developmentAiModelListOpen)
            {
                if (GUI.Button(new Rect(rect.x + 8f, rect.y + 84f, 132f, 24f), "直前を取り消す"))
                    developmentAiMessage = CancelLastAiInstruction() ? "取り消しました" : "取り消せる指示はありません";
                GUI.Label(new Rect(rect.x + 148f, rect.y + 84f, rect.width - 156f, 24f),
                    "費用 " + AiMatchCostYen.ToString("0.000") + "円 / 残り " + AiRemainingBudgetYen.ToString("0.000") + "円");
                float top = rect.y + 112f;
                if (!string.IsNullOrEmpty(developmentAiMessage))
                {
                    GUI.Label(new Rect(rect.x + 8f, top, rect.width - 16f, 22f), developmentAiMessage);
                    top += 24f;
                }
                DrawChatLog(new Rect(rect.x + 4f, top, rect.width - 8f, rect.yMax - top - 4f));
            }
            else
            {
                // The open list takes the place of the rows below, inside the panel, three to a row: a list hanging
                // below the panel sat under the command-log button, which took the clicks (10-05).
                var models = AiModels;
                const int columns = 3;
                int rows = (models.Count + columns - 1) / columns;
                var list = new Rect(rect.x + 8f, rect.y + 82f, rect.width - 16f, rows * 24f + 4f);
                float cell = (list.width - 4f) / columns;
                GUI.Box(list, GUIContent.none, UiStyles.Panel);
                UiHitAreas.Shared.Register(list);
                for (int i = 0; i < models.Count; i++)
                {
                    var option = models[i];
                    var previous = GUI.enabled;
                    GUI.enabled = option.Available;
                    var cellRect = new Rect(list.x + 2f + (i % columns) * cell, list.y + 2f + (i / columns) * 24f, cell - 2f, 22f);
                    if (GUI.Button(cellRect, ModelLabel(option.Model) + (option.Available ? "" : "（キー未設定）")))
                    {
                        developmentAiModel = option.Model;
                        developmentAiModelListOpen = false;
                    }
                    GUI.enabled = previous;
                }
            }
        }

        private string ModelLabel(string model)
        {
            switch (model)
            {
                case "local-llm": return "ローカルLLM（無料）";
                case "jev": return "Jev（命令1つ）";
                default: return model ?? "";
            }
        }

        private Vector2 chatScroll;
        private int chatShownCount = -1;
        private readonly System.Collections.Generic.List<ChatLine> chatLines = new System.Collections.Generic.List<ChatLine>();

        private struct ChatLine
        {
            public string Text;
            public Color Color;
        }

        /// <summary>
        /// The conversation with the staff officer, oldest at the top: what was said, the reply, what it ordered, what
        /// was refused, the state and the cost. Scrolls to the newest exchange whenever one is added.
        /// </summary>
        private void DrawChatLog(Rect area)
        {
            if (area.height < 24f) return;
            var history = AiInstructions;
            chatLines.Clear();
            foreach (var item in history) AddChatLines(item);
            if (chatLines.Count == 0)
            {
                GUI.Label(new Rect(area.x + 4f, area.y, area.width - 8f, 22f), "参謀に話しかけると、ここにやり取りが出ます。");
                return;
            }
            float width = area.width - 20f;
            float total = 0f;
            foreach (var line in chatLines) total += UiStyles.Body.CalcHeight(new GUIContent(line.Text), width) + 2f;
            if (history.Count != chatShownCount)
            {
                chatShownCount = history.Count;
                chatScroll.y = float.MaxValue;
            }
            chatScroll = GUI.BeginScrollView(area, chatScroll, new Rect(0f, 0f, width, total));
            float y = 0f;
            var old = GUI.contentColor;
            foreach (var line in chatLines)
            {
                float h = UiStyles.Body.CalcHeight(new GUIContent(line.Text), width);
                GUI.contentColor = line.Color;
                GUI.Label(new Rect(0f, y, width, h), line.Text, UiStyles.Body);
                y += h + 2f;
            }
            GUI.contentColor = old;
            GUI.EndScrollView();
        }

        private void AddChatLines(LiveAiInstruction item)
        {
            var you = new Color(0.75f, 0.88f, 1f);
            var staff = Color.white;
            var detail = new Color(0.8f, 0.8f, 0.8f);
            var bad = new Color(1f, 0.6f, 0.5f);
            chatLines.Add(new ChatLine { Text = MatchOutcome.Clock(item.StartedTick) + " あなた：" + item.Instruction, Color = you });
            string state;
            switch (item.State)
            {
                case AiInstructionState.Interpreting: state = "考え中…"; break;
                case AiInstructionState.Executing: state = "実行中"; break;
                case AiInstructionState.Completed: state = "完了"; break;
                case AiInstructionState.Cancelled: state = "取り消し"; break;
                case AiInstructionState.Expired: state = "時間切れ"; break;
                default: state = "わからない"; break;
            }
            string reply = !string.IsNullOrEmpty(item.Say) ? item.Say : item.Reason;
            chatLines.Add(new ChatLine
            {
                Text = "参謀（" + ModelLabel(item.Model) + "）：" + (string.IsNullOrEmpty(reply) ? state : reply),
                Color = item.State == AiInstructionState.Unknown || item.State == AiInstructionState.Expired ? bad : staff
            });
            if (!string.IsNullOrEmpty(item.Say) && !string.IsNullOrEmpty(item.Reason) && item.Reason != item.Say)
                chatLines.Add(new ChatLine { Text = "　理由：" + item.Reason, Color = detail });
            // A late or refused answer may still carry commands; they were not carried out, and the log must say so.
            bool carriedOut = item.State == AiInstructionState.Executing || item.State == AiInstructionState.Completed
                || item.State == AiInstructionState.Cancelled;
            foreach (var issued in item.Issued ?? Array.Empty<string>())
                chatLines.Add(new ChatLine { Text = "　→ " + issued + (carriedOut ? "" : "（実行せず）"), Color = detail });
            foreach (var rejected in item.RejectedReasons ?? Array.Empty<string>())
                chatLines.Add(new ChatLine { Text = "　× 却下：" + rejected, Color = bad });
            chatLines.Add(new ChatLine
            {
                Text = "　［" + state + "　" + (item.State == AiInstructionState.Interpreting
                    ? "見積もり " + item.EstimatedCostYen.ToString("0.000") : item.ActualCostYen.ToString("0.000")) + "円］",
                Color = detail
            });
        }
    }
}
