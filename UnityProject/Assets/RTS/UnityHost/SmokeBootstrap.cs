using System;
using UnityEngine;

namespace Rts.UnityHost
{
    /// <summary>Creates the product smoke host only when the player was launched with -rts-smoke.</summary>
    public static class SmokeBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RunIfRequested()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (!SmokeOptions.HasSwitch(args)) return;
            if (!SmokeOptions.TryParse(args, out var options, out var error))
            {
                Debug.LogError("RTS smoke arguments are invalid: " + error);
                UnityEngine.Application.Quit(2);
                return;
            }

            LiveMatchHost.SmokeMode = true;
            var gameObject = new GameObject("RTS Smoke Host");
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
            var host = gameObject.AddComponent<LiveMatchHost>();
            host.ConfigureSmoke(options);
        }
    }
}
