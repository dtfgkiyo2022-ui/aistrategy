using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Rts.Tactics;

namespace Rts.TacticsJs
{
    /// <summary>One permission-restricted Deno process per match. Never runs system Python.</summary>
    public sealed class PyodideTacticRuntime : ITacticRuntime, ITacticLogSource, ITacticFailurePolicy, IDisposable, ITacticParameterRuntime, ITacticSignalRuntime
    {
        public const int StartupTimeoutMilliseconds = 10000;
        public const int CallTimeoutMilliseconds = 50;
        public const string NumpyWheel = "numpy-2.4.6-cp314-cp314-pyemscripten_2026_0_wasm32.whl";
        private readonly string folder;
        private readonly string runtimes;
        private readonly List<string> logs = new List<string>();
        private Process process;
        private Win32ChildProcess win32Process;
        private Task<string> pending;
        private bool win32ResponsePending;
        private string cacheDirectory;
        private bool started;
        private bool stopped;
        private int timeouts;
        private readonly IReadOnlyList<TacticParamDefinition> parameters;
        private readonly IReadOnlyList<TacticSignalDefinition> signals;
        private readonly Dictionary<string, object> parameterValues = new Dictionary<string, object>(StringComparer.Ordinal);
        public string Name { get; }
        public int ConsecutiveFailureLimit => 3;
        public bool IsStopped => stopped;
        public int? ProcessId { get; private set; }
        public double StartupMilliseconds { get; private set; }

        public PyodideTacticRuntime(string folder, string runtimes, string name = "python", IReadOnlyList<TacticParamDefinition> parameters = null,
            IReadOnlyList<TacticSignalDefinition> signals = null)
        {
            this.folder = Path.GetFullPath(folder);
            this.runtimes = Path.GetFullPath(runtimes ?? FindDefaultRuntimes());
            Name = name;
            this.parameters = parameters ?? Array.Empty<TacticParamDefinition>();
            this.signals = TacticSignalDefinition.Validate(signals);
            foreach (var parameter in this.parameters) parameterValues[parameter.Name] = parameter.DefaultValue;
        }

        public IReadOnlyList<TacticParamDefinition> Parameters => parameters;
        public IReadOnlyList<TacticSignalDefinition> Signals => signals;

        public void SetParameters(IReadOnlyDictionary<string, object> values)
        {
            parameterValues.Clear();
            foreach (var parameter in parameters)
                parameterValues[parameter.Name] = values != null && values.TryGetValue(parameter.Name, out var value) ? value : parameter.DefaultValue;
        }

        public static string FindDefaultRuntimes()
        {
            for (var current = new DirectoryInfo(AppContext.BaseDirectory); current != null; current = current.Parent)
            {
                string candidate = Path.Combine(current.FullName, "UnityProject", "Assets", "StreamingAssets", "TacticRuntimes");
                if (Directory.Exists(candidate)) return candidate;
            }
            return Path.Combine(AppContext.BaseDirectory, "StreamingAssets", "TacticRuntimes");
        }

        public static string AvailabilityError(string root)
        {
            root = root ?? FindDefaultRuntimes();
            foreach (string file in new[] { "deno/deno.exe", "pyodide-host.mjs", "pyodide/pyodide.mjs", "pyodide/pyodide.asm.mjs", "pyodide/pyodide.asm.wasm", "pyodide/python_stdlib.zip", "pyodide/pyodide-lock.json", "pyodide/" + NumpyWheel })
                if (!File.Exists(Path.Combine(root, file))) return "Python実行環境がありません: " + file;
            return null;
        }

        public void Start(string setupJson)
        {
            if (started || stopped) throw new InvalidOperationException("Python戦術は既に開始または停止しています。");
            started = true;
            var watch = Stopwatch.StartNew();
            try
            {
                string error = AvailabilityError(runtimes);
                if (error != null) throw new FileNotFoundException(error);
                string pyodide = Path.Combine(runtimes, "pyodide");
                // Commas delimit Deno permission entries. Refuse ambiguous grants.
                if (pyodide.Contains(",") || folder.Contains(",")) throw new ArgumentException("Python戦術のパスにカンマは使えません。");
                cacheDirectory = Path.Combine(Path.GetTempPath(), "rts-pyodide-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(cacheDirectory);
                string deno = Path.Combine(runtimes, "deno", "deno.exe");
                string arguments = "run --no-prompt --no-remote --cached-only " + Quote("--allow-read=" + pyodide + "," + folder)
                    + " " + Quote(Path.Combine(runtimes, "pyodide-host.mjs")) + " " + Quote(folder);
                var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["DENO_DIR"] = cacheDirectory
                };
                if (Win32ChildProcess.IsSupported)
                {
                    // Same environment as the Process path: DENO_DIR only. Passing SystemRoot made the
                    // first "start" call about 23ms slower (27ms -> 50ms, over the call budget; measured 10-06).
                    win32Process = Win32ChildProcess.Start(deno, arguments, runtimes, environment);
                    ProcessId = win32Process.Id;
                }
                else
                {
                    var info = new ProcessStartInfo
                    {
                        FileName = deno,
                        Arguments = arguments,
                        WorkingDirectory = runtimes,
                        UseShellExecute = false, CreateNoWindow = true,
                        RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                        StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8
                    };
                    info.Environment.Clear();
                    foreach (var item in environment) info.Environment[item.Key] = item.Value;
                    process = Process.Start(info) ?? throw new IOException("Denoを起動できません。");
                    ProcessId = process.Id;
                }
                // Win32ChildProcess drains stderr on its own thread. Its synchronous pipe
                // reader also avoids sending ReadAsync work to the CLR thread pool.
                string ready;
                if (win32Process != null)
                {
                    int remaining = Math.Max(1, StartupTimeoutMilliseconds - (int)watch.ElapsedMilliseconds);
                    if (!win32Process.TryReadStandardOutputLine(remaining, out ready))
                        throw new TimeoutException("Pyodideの起動が10秒を超えました。");
                }
                else
                {
                    // Drain stderr without accumulating attacker-controlled output in memory.
                    var stderr = StandardError;
                    _ = Task.Run(async () => { var buffer = new char[1024]; try { while (await stderr.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false) != 0) { } } catch (IOException) { } catch (ObjectDisposedException) { } });
                    pending = ReadResponse(StandardOutput);
                    int remaining = Math.Max(1, StartupTimeoutMilliseconds - (int)watch.ElapsedMilliseconds);
                    if (!pending.Wait(remaining)) throw new TimeoutException("Pyodideの起動が10秒を超えました。");
                    ready = pending.GetAwaiter().GetResult();
                    pending = null;
                }
                if (ready != "{\"ready\":true}") throw new IOException("Pyodideの起動に失敗しました: " + ready);
                StartupMilliseconds = watch.Elapsed.TotalMilliseconds;
            }
            catch { Dispose(); throw; }
            // Loading user code and on_start have the same budget as on_tick.
            string inputSetup = setupJson ?? "{}";
            Call("start", inputSetup.IndexOf("\"params\"", StringComparison.Ordinal) >= 0 ? inputSetup : TacticParameterJson.AddParams(inputSetup, parameterValues));
        }

        public string Tick(string viewJson)
        {
            if (!started) Start("{}");
            string view = viewJson ?? throw new ArgumentNullException(nameof(viewJson));
            if (view.IndexOf("\"params\"", StringComparison.Ordinal) < 0) view = TacticParameterJson.AddParams(view, parameterValues);
            return Call("tick", view);
        }

        private string Call(string operation, string payload)
        {
            if (stopped) throw new InvalidOperationException("Python戦術は停止しています。");
            logs.Clear();
            var watch = Stopwatch.StartNew();
            if (win32Process != null) return CallWin32(operation, payload, watch);
            // A timed-out call may still be executing. Never enqueue another request behind it,
            // and never use its late output as commands for a newer observation.
            if (pending != null)
            {
                if (!pending.Wait(CallTimeoutMilliseconds)) return Timeout();
                pending.GetAwaiter().GetResult();
                pending = null;
            }
            int remaining = CallTimeoutMilliseconds - (int)watch.ElapsedMilliseconds;
            if (remaining <= 0) return Timeout();
            var input = StandardInput;
            var output = StandardOutput;
            pending = Task.Run(async () =>
            {
                await input.WriteLineAsync("{\"op\":\"" + operation + "\",\"payload\":" + JsonString(payload) + "}").ConfigureAwait(false);
                await input.FlushAsync().ConfigureAwait(false);
                return await ReadResponse(output).ConfigureAwait(false);
            });
            if (!pending.Wait(remaining)) return Timeout();
            string response = pending.GetAwaiter().GetResult();
            pending = null;
            CheckJsonDepth(response);
            var root = TacticJsonForRuntime.Parse(response) as Dictionary<string, object>;
            if (root == null) throw new IOException("Pythonからの応答が不正です。");
            if (root.TryGetValue("logs", out var items) && items is List<object> lines)
                foreach (var line in lines)
                {
                    if (logs.Count == JsTacticRuntime.ConsoleLineLimit) break;
                    string text = line as string ?? "";
                    logs.Add(text.Length > 200 ? text.Substring(0, 200) : text);
                }
            if (root.TryGetValue("error", out var error)) throw new InvalidOperationException(error as string ?? "Python戦術が失敗しました。");
            string commandJson = root.TryGetValue("output", out var result) && result is string json ? json : "{\"version\":1,\"commands\":[]}";
            CheckJsonDepth(commandJson);
            if (watch.ElapsedMilliseconds >= CallTimeoutMilliseconds) return Timeout();
            timeouts = 0;
            return commandJson;
        }

        private string CallWin32(string operation, string payload, Stopwatch watch)
        {
            // A timed-out call may still be executing. Wait for and discard exactly its
            // late response before sending the next request, so responses cannot shift by one.
            if (win32ResponsePending)
            {
                int remaining = CallTimeoutMilliseconds - (int)watch.ElapsedMilliseconds;
                if (remaining <= 0) return Timeout();
                string lateResponse;
                if (!win32Process.TryReadStandardOutputLine(remaining, out lateResponse)) return Timeout();
                win32ResponsePending = false;
            }

            int beforeWrite = CallTimeoutMilliseconds - (int)watch.ElapsedMilliseconds;
            if (beforeWrite <= 0) return Timeout();
            var input = StandardInput;
            // The protocol request is normally far below a Windows pipe's capacity, so a
            // synchronous WriteLine+Flush avoids a thread-pool hop. An unusually large input
            // can still block here; it is rejected by the call budget on the following read.
            input.WriteLine("{\"op\":\"" + operation + "\",\"payload\":" + JsonString(payload) + "}");
            input.Flush();
            win32ResponsePending = true;

            int remainingAfterWrite = CallTimeoutMilliseconds - (int)watch.ElapsedMilliseconds;
            if (remainingAfterWrite <= 0) return Timeout();
            string response;
            if (!win32Process.TryReadStandardOutputLine(remainingAfterWrite, out response)) return Timeout();
            win32ResponsePending = false;
            return CompleteResponse(response, watch);
        }

        private string CompleteResponse(string response, Stopwatch watch)
        {
            CheckJsonDepth(response);
            var root = TacticJsonForRuntime.Parse(response) as Dictionary<string, object>;
            if (root == null) throw new IOException("Pythonからの応答が不正です。");
            if (root.TryGetValue("logs", out var items) && items is List<object> lines)
                foreach (var line in lines)
                {
                    if (logs.Count == JsTacticRuntime.ConsoleLineLimit) break;
                    string text = line as string ?? "";
                    logs.Add(text.Length > 200 ? text.Substring(0, 200) : text);
                }
            if (root.TryGetValue("error", out var error)) throw new InvalidOperationException(error as string ?? "Python戦術が失敗しました。");
            string commandJson = root.TryGetValue("output", out var result) && result is string json ? json : "{\"version\":1,\"commands\":[]}";
            CheckJsonDepth(commandJson);
            if (watch.ElapsedMilliseconds >= CallTimeoutMilliseconds) return Timeout();
            timeouts = 0;
            return commandJson;
        }

        private string Timeout()
        {
            if (++timeouts >= 3) Dispose();
            throw new TimeoutException("Python戦術の呼び出しが50msを超えました。");
        }

        private static async Task<string> ReadResponse(StreamReader reader)
        {
            var line = new StringBuilder();
            var buffer = new char[1];
            while (await reader.ReadAsync(buffer, 0, 1).ConfigureAwait(false) != 0)
            {
                if (buffer[0] == '\n') return line.ToString();
                if (line.Length >= 1024 * 1024) throw new IOException("Pythonの応答が1Mi文字を超えています。");
                line.Append(buffer[0]);
            }
            throw new IOException("Pythonの子プロセスが終了しました。");
        }

        public IReadOnlyList<string> TakeConsoleLines() { var result = logs.ToArray(); logs.Clear(); return result; }

        public void Dispose()
        {
            if (stopped) return;
            stopped = true;
            if (win32Process != null)
            {
                try { if (!win32Process.HasExited) win32Process.Kill(); win32Process.WaitForExit(2000); }
                catch (Win32Exception) { }
                catch (ObjectDisposedException) { }
                finally { win32Process.Dispose(); win32Process = null; }
            }
            if (process != null)
            {
                try { if (!process.HasExited) process.Kill(); process.WaitForExit(2000); }
                catch (InvalidOperationException) { }
                finally { process.Dispose(); process = null; }
            }
            pending = null;
            win32ResponsePending = false;
            // This is the exact private directory created above, never a caller-supplied path.
            if (cacheDirectory != null)
            {
                try { Directory.Delete(cacheDirectory, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"").TrimEnd('\\') + "\"";

        private StreamWriter StandardInput => win32Process != null ? win32Process.StandardInput : process.StandardInput;
        private StreamReader StandardOutput => win32Process != null ? win32Process.StandardOutput : process.StandardOutput;
        private StreamReader StandardError => win32Process != null ? win32Process.StandardError : process.StandardError;

        // stdout is untrusted: the tactic can also write via js.Deno.stdout. Bound nesting
        // before either recursive JSON parser runs in the game process.
        private static void CheckJsonDepth(string value)
        {
            int depth = 0;
            bool quoted = false, escaped = false;
            foreach (char c in value)
            {
                if (quoted)
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') quoted = false;
                }
                else if (c == '"') quoted = true;
                else if ((c == '{' || c == '[') && ++depth > 64) throw new FormatException("PythonのJSONの入れ子が64段を超えています。");
                else if (c == '}' || c == ']') depth--;
            }
        }

        private static string JsonString(string value)
        {
            var result = new StringBuilder("\"");
            foreach (char c in value)
            {
                if (c == '\\' || c == '"') result.Append('\\').Append(c);
                else if (c < 32) result.Append("\\u").Append(((int)c).ToString("x4"));
                else result.Append(c);
            }
            return result.Append('"').ToString();
        }
    }
}
