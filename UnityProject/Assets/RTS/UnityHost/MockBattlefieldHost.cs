using Rts.Presentation;
using UnityEngine;

namespace Rts.UnityHost
{
    /// <summary>Steps the mock source at 20 Hz and hands each frame to the view.</summary>
    public sealed class MockBattlefieldHost : MonoBehaviour
    {
        [SerializeField] private BattlefieldView view;

        private readonly MockFrameSource source = new MockFrameSource();
        private float accumulated;

        private void Start()
        {
            view.Push(source.Latest(1));
        }

        private void Update()
        {
            accumulated += Time.deltaTime;
            while (accumulated >= BattlefieldView.TickSeconds)
            {
                accumulated -= BattlefieldView.TickSeconds;
                source.Advance();
                view.Push(source.Latest(1));
            }
        }
    }
}
