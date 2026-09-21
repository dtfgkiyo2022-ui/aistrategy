using System;
using Rts.Application;
using Rts.Contracts;
using Rts.Presentation;
using Rts.Providers;
using Rts.Simulation;
using UnityEngine;
using Battle = Rts.Simulation.Simulation;

namespace Rts.UnityHost
{
    /// <summary>
    /// Drives a real match: Simulation + CommandGateway stepped at the scenario tick rate, with the
    /// player on faction 1 and a doctrine preset on faction 2. Display reads captured frames only.
    /// </summary>
    public sealed class LiveMatchHost : MonoBehaviour, IExternalAiControl, IMatchClock
    {
        [SerializeField] private BattlefieldView view;
        [SerializeField] private CommandPanel panel;
        [SerializeField] private uint viewFactionId = 1;
        [SerializeField] private string enemyPreset = "maintain";
        [SerializeField] private int aiDelayTicks;

        private Battle simulation;
        private CommandGateway gateway;
        private LiveCommandPort port;
        private PresetController enemy;
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
                view.ResetVisuals();
                if (simulation == null) return;
                view.Push(simulation.Capture(viewFactionId));
                panel.Bind(port, viewFactionId, viewFactionId, view);
            }
        }

        public void StepOneTick() { StepOnce(); }
        public FactionFrame Frame { get { return simulation == null ? null : simulation.Capture(viewFactionId); } }
        public bool HasEnded { get { return simulation != null && simulation.Capture(viewFactionId).Result.HasEnded; } }

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
        private JevPolicyProvider jev;
        private HttpJevTransport jevTransport;

        /// <summary>Set while a match is running with an external provider, for the display to read.</summary>
        public JevPolicyProvider ExternalProvider { get { return jev; } }

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
            gatewayOrders = 0;
        }

        private void OnDestroy() { StopExternal(); }

        public void Begin()
        {
            var scenario = ScenarioScale.Multiply(WeekTwoScenario.Create(), ScenarioMultiplier);
            tickSeconds = 1f / scenario.TickRateHz;
            simulation = new Battle(scenario);
            var provider = aiDelayTicks == 0 ? null : new DelayedPolicyProvider(aiDelayTicks, r => port.Interpret(r));
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
            gateway = new CommandGateway(simulation, provider, null,
                external == null ? null : AutonomousPollSchedule.OnChange(600), external);
            port = new LiveCommandPort(gateway, aiDelayTicks);
            enemy = PolicyPresets.CreateController(enemyPreset, 3 - viewFactionId, gateway);
            enemy.Initialize();
            if (external != null)
                gateway.EnableAutonomous(new UserPolicyIntent(0, new ScopeKey(viewFactionId, ScopeKind.All, 0),
                    PolicyKind.Focus, default(PolicyGoal), 50, new LossBudget(300),
                    new EndCondition(EndKind.UntilReplaced, 0), 0, new Expiration(long.MaxValue, 0, ExpireFlags.None)));

            view.SetTerrain(ScenarioTerrain.From(scenario.Map));
            view.Push(simulation.Capture(viewFactionId));
            panel.Bind(port, viewFactionId, viewFactionId, view);
            panel.ExternalAi = this;
        }

        /// <summary>Verification entry: sends a standard command through the same port the UI uses.</summary>
        public ulong Submit(UserPolicyIntent intent) { return port.Submit(intent); }

        public void StepOnce()
        {
            if (simulation == null || HasEnded) return;
            gateway.Step();
            var frame = simulation.Capture(viewFactionId);
            view.Push(frame);
            if (frame.Result.HasEnded) return;
            enemy.Step(simulation.Capture(3 - viewFactionId));
        }

        private void Start() { Begin(); }

        private void Update()
        {
            if (simulation == null) return;
            if (port.RestartRequested || externalRestartRequested)
            {
                aiDelayTicks = port.DelayTicks;
                externalRestartRequested = false;
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
    }
}
