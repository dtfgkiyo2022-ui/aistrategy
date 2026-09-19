using Rts.Presentation;
using UnityEngine;

namespace Rts.UnityHost
{
    /// <summary>Steps the mock source at 20 Hz and hands each frame to the view.</summary>
    public sealed class MockBattlefieldHost : MonoBehaviour
    {
        [SerializeField] private BattlefieldView view;
        [SerializeField] private CommandPanel panel;

        private readonly MockFrameSource source = new MockFrameSource();
        private readonly MockCommandPort port = new MockCommandPort();
        private float accumulated;

        private void Start()
        {
            source.CommandProvider = port.Views;
            view.SetTerrain(MockTerrain.Create());
            view.Push(source.Latest(1));
            panel.Bind(port, 1, 1, view);
        }

        private void Update()
        {
            accumulated += Time.deltaTime;
            while (accumulated >= BattlefieldView.TickSeconds)
            {
                accumulated -= BattlefieldView.TickSeconds;
                source.Advance();
                port.SetTick(source.Tick);
                view.Push(source.Latest(1));
            }
        }
    }
}
