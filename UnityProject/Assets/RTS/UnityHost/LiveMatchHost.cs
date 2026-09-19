using Rts.Application;
using Rts.Contracts;
using Rts.Presentation;
using Rts.Simulation;
using UnityEngine;
using Battle = Rts.Simulation.Simulation;

namespace Rts.UnityHost
{
    /// <summary>
    /// Drives a real match: Simulation + CommandGateway stepped at the scenario tick rate, with the
    /// player on faction 1 and a doctrine preset on faction 2. Display reads captured frames only.
    /// </summary>
    public sealed class LiveMatchHost : MonoBehaviour
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
        private float tickSeconds = BattlefieldView.TickSeconds;

        public long Tick { get { return simulation == null ? 0 : simulation.Capture(viewFactionId).Tick; } }
        public FactionFrame Frame { get { return simulation == null ? null : simulation.Capture(viewFactionId); } }
        public bool HasEnded { get { return simulation != null && simulation.Capture(viewFactionId).Result.HasEnded; } }

        public void Begin()
        {
            var scenario = WeekTwoScenario.Create();
            tickSeconds = 1f / scenario.TickRateHz;
            simulation = new Battle(scenario);
            var provider = aiDelayTicks == 0 ? null : new DelayedPolicyProvider(aiDelayTicks, r => port.Interpret(r));
            gateway = new CommandGateway(simulation, provider);
            port = new LiveCommandPort(gateway, aiDelayTicks);
            enemy = PolicyPresets.CreateController(enemyPreset, 3 - viewFactionId, gateway);
            enemy.Initialize();

            view.SetTerrain(ScenarioTerrain.From(scenario.Map));
            view.Push(simulation.Capture(viewFactionId));
            panel.Bind(port, viewFactionId, viewFactionId, view);
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
            if (port.RestartRequested)
            {
                aiDelayTicks = port.DelayTicks;
                accumulated = 0f;
                Begin();
                return;
            }
            accumulated += Time.deltaTime;
            while (accumulated >= tickSeconds)
            {
                accumulated -= tickSeconds;
                StepOnce();
            }
        }
    }
}
