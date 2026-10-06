using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using NUnit.Framework;
using Rts.TacticsJs;

namespace Rts.Core.Tests
{
    [NonParallelizable]
    public sealed class Win32ChildProcessTests
    {
        [SetUp]
        public void RequireWindows()
        {
            Assume.That(Win32ChildProcess.IsSupported, Is.True, "Win32 の Headless テストは Windows で実行します。");
        }

        [Test]
        public void EchoCanBeReadFromStandardOutput()
        {
            using var child = StartCmd("/c echo win32-child");
            Assert.That(child.StandardOutput.ReadLine(), Is.EqualTo("win32-child"));
            Assert.That(child.WaitForExit(2000), Is.True);
        }

        [Test]
        public void StandardInputCanBeSentAndReadBack()
        {
            using var child = StartExecutable(Path.Combine(Environment.SystemDirectory, "findstr.exe"), "/r .");
            child.StandardInput.WriteLine("round-trip");
            child.StandardInput.Flush();
            Thread.Sleep(100);
            child.StandardInput.Dispose();
            Assert.That(child.StandardOutput.ReadLine(), Is.EqualTo("round-trip"));
            Assert.That(child.WaitForExit(2000), Is.True);
        }

        [Test]
        public void KillStopsTheChild()
        {
            using var child = StartCmd("/c ping 127.0.0.1 -n 30 > nul");
            Thread.Sleep(100);
            child.Kill();
            Assert.That(child.WaitForExit(2000), Is.True);
            Assert.That(child.HasExited, Is.True);
        }

        [Test]
        public void DisposeClosesTheJobAndDoesNotLeaveTheChild()
        {
            var child = StartCmd("/c ping 127.0.0.1 -n 30 > nul");
            int pid = child.Id;
            child.Dispose();
            child.Dispose();
            AssertExited(pid);
        }

        private static Win32ChildProcess StartCmd(string arguments)
        {
            string command = Environment.GetEnvironmentVariable("ComSpec") ?? Path.Combine(Environment.SystemDirectory, "cmd.exe");
            return StartExecutable(command, arguments);
        }

        private static Win32ChildProcess StartExecutable(string command, string arguments)
        {
            var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["SystemRoot"] = Environment.GetEnvironmentVariable("SystemRoot") ?? string.Empty,
                ["TEMP"] = Path.GetTempPath(),
                ["TMP"] = Path.GetTempPath()
            };
            return Win32ChildProcess.Start(command, arguments, Environment.CurrentDirectory, environment);
        }

        private static void AssertExited(int pid)
        {
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById(pid);
                Assert.That(process.HasExited, Is.True);
            }
            catch (ArgumentException) { }
        }
    }
}
