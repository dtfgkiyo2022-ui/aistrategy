using UnityEngine;

namespace Rts.UnityHost
{
    /// <summary>Player entry for Issue 4-1: replays a file given on the command line, writes hashes, then quits with the exit code.</summary>
    public sealed class ReplayCheckBootstrap : MonoBehaviour
    {
        private void Start()
        {
            int exit = ReplayCrossCheck.RunFromCommandLine();
            UnityEngine.Application.Quit(exit);
        }
    }
}
