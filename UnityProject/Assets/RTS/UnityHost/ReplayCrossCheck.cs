using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Rts.Application;
using Rts.Replay;
using UnityEngine;

namespace Rts.UnityHost
{
    /// <summary>
    /// Issue 4-1: replays a recording made elsewhere (CLI on .NET) inside this Unity runtime and writes every tick's
    /// state/event hash, so Mono (Editor) and IL2CPP (Player) results can be compared with the CLI's `replay` output.
    /// </summary>
    public static class ReplayCrossCheck
    {
        public static string Backend
        {
            get
            {
#if ENABLE_IL2CPP
                return "IL2CPP";
#elif ENABLE_MONO
                return "Mono";
#else
                return "Unknown";
#endif
            }
        }

        /// <summary>Returns 0 when every recorded hash matched, 2 on a mismatch, 3 on a bad file, 4 on a fault.</summary>
        public static int Run(string replayPath, string hashesPath, string summaryPath)
        {
            var hashes = new StringBuilder();
            long ticks = 0;
            int exit;
            string detail;
            try
            {
                var build = new BuildIdentity { Commit = "unity-" + Backend, SourceHash = "", Backend = Backend };
                using (var stream = File.OpenRead(replayPath))
                {
                    var outcome = ReplayRunner.Replay(stream, build, (state, hash, events) =>
                    {
                        hashes.Append(state.Tick).Append(' ').Append(ReplayBinary.Hex(hash)).Append(' ').Append(ReplayBinary.Hex(events)).Append('\n');
                        ticks++;
                    }, true);
                    exit = outcome.IsFault ? 4 : outcome.FirstMismatchTick.HasValue ? 2 : 0;
                    detail = "lastTick=" + outcome.LastTick + " firstMismatchTick=" + (outcome.FirstMismatchTick.HasValue ? outcome.FirstMismatchTick.Value.ToString() : "none") + " fault=" + outcome.IsFault;
                }
            }
            catch (Exception e) when (e is InvalidDataException || e is EndOfStreamException || e is IOException)
            {
                exit = 3;
                detail = "format/version: " + e.Message;
            }

            File.WriteAllText(hashesPath, hashes.ToString());
            File.WriteAllText(summaryPath,
                "backend=" + Backend + "\nunity=" + UnityEngine.Application.unityVersion + "\nreplay=" + Path.GetFileName(replayPath)
                + "\nticksHashed=" + ticks + "\n" + detail + "\nexit=" + exit + "\n");
            return exit;
        }

        /// <summary>Reads `-replay <file> -hashes <file> -summary <file>` from the command line.</summary>
        public static int RunFromCommandLine()
        {
            var args = Environment.GetCommandLineArgs();
            string replay = Arg(args, "-replay"), hashes = Arg(args, "-hashes"), summary = Arg(args, "-summary");
            if (replay == null || hashes == null || summary == null) return 3;
            return Run(replay, hashes, summary);
        }

        private static string Arg(string[] args, string name)
        {
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
