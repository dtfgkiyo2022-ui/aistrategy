using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace Rts.TacticsJs
{
    /// <summary>
    /// Starts a redirected child process without System.Diagnostics.Process.Start.
    /// Unity's Windows IL2CPP does not implement that API.
    /// </summary>
    public sealed class Win32ChildProcess : IDisposable
    {
        private const uint CreateSuspended = 0x00000004;
        private const uint CreateUnicodeEnvironment = 0x00000400;
        private const uint CreateNoWindow = 0x08000000;
        private const uint HandleFlagInherit = 0x00000001;
        private const uint JobObjectExtendedLimitInformation = 9;
        private const uint JobObjectLimitKillOnJobClose = 0x00002000;
        private const uint WaitObject0 = 0;
        private const uint WaitTimeout = 0x00000102;
        private const uint Infinite = 0xffffffff;
        private const uint StartupfUseStdHandles = 0x00000100;
        private const int MaxLineCharacters = 1024 * 1024;
        private const uint PipeBufferSize = 64 * 1024;

        private IntPtr processHandle;
        private IntPtr jobHandle;
        private SafeFileHandle standardInputHandle;
        private SafeFileHandle standardOutputHandle;
        private SafeFileHandle standardErrorHandle;
        private FileStream standardInputStream;
        private FileStream standardOutputStream;
        private FileStream standardErrorStream;
        private StreamWriter standardInput;
        private StreamReader standardOutput;
        private StreamReader standardOutputReader;
        private StreamReader standardError;
        private BlockingCollection<OutputLine> outputLines;
        private Thread outputThread;
        private Thread errorThread;
        private bool disposed;

        private Win32ChildProcess()
        {
        }

        public static bool IsSupported => Environment.OSVersion.Platform == PlatformID.Win32NT;

        public int Id { get; private set; }

        public StreamWriter StandardInput
        {
            get
            {
                ThrowIfDisposed();
                return standardInput;
            }
        }

        public StreamReader StandardOutput
        {
            get
            {
                ThrowIfDisposed();
                return standardOutput;
            }
        }

        public StreamReader StandardError
        {
            get
            {
                ThrowIfDisposed();
                return standardError;
            }
        }

        public static Win32ChildProcess Start(string fileName, string arguments, string workingDirectory, IDictionary<string, string> environment)
        {
            if (!IsSupported) throw new PlatformNotSupportedException("Win32 の子プロセスは Windows でだけ使用できます。");
            if (string.IsNullOrEmpty(fileName)) throw new ArgumentNullException(nameof(fileName));

            var child = new Win32ChildProcess();
            IntPtr parentInput = IntPtr.Zero;
            IntPtr childInput = IntPtr.Zero;
            IntPtr parentOutput = IntPtr.Zero;
            IntPtr childOutput = IntPtr.Zero;
            IntPtr parentError = IntPtr.Zero;
            IntPtr childError = IntPtr.Zero;
            IntPtr threadHandle = IntPtr.Zero;
            PROCESS_INFORMATION processInfo = new PROCESS_INFORMATION();
            try
            {
                var security = new SECURITY_ATTRIBUTES
                {
                    nLength = Marshal.SizeOf(typeof(SECURITY_ATTRIBUTES)),
                    bInheritHandle = 1
                };

                CreatePipeOrThrow(out childInput, out parentInput, ref security, "標準入力のパイプ");
                CreatePipeOrThrow(out parentOutput, out childOutput, ref security, "標準出力のパイプ");
                CreatePipeOrThrow(out parentError, out childError, ref security, "標準エラーのパイプ");
                SetNonInheritableOrThrow(parentInput, "標準入力の親ハンドル");
                SetNonInheritableOrThrow(parentOutput, "標準出力の親ハンドル");
                SetNonInheritableOrThrow(parentError, "標準エラーの親ハンドル");

                string commandLine = Quote(fileName) + (string.IsNullOrEmpty(arguments) ? string.Empty : " " + arguments);
                StringBuilder mutableCommandLine = new StringBuilder(commandLine);
                IntPtr environmentBlock = BuildEnvironmentBlock(environment);
                try
                {
                    var startup = new STARTUPINFO
                    {
                        cb = Marshal.SizeOf(typeof(STARTUPINFO)),
                        dwFlags = StartupfUseStdHandles,
                        hStdInput = childInput,
                        hStdOutput = childOutput,
                        hStdError = childError
                    };
                    if (!CreateProcessW(
                        fileName,
                        mutableCommandLine,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        1,
                        CreateSuspended | CreateNoWindow | CreateUnicodeEnvironment,
                        environmentBlock,
                        workingDirectory,
                        ref startup,
                        out processInfo))
                        ThrowLastError("CreateProcessW");
                }
                finally
                {
                    if (environmentBlock != IntPtr.Zero) Marshal.FreeHGlobal(environmentBlock);
                    CloseHandleIfNeeded(childInput, ref childInput);
                    CloseHandleIfNeeded(childOutput, ref childOutput);
                    CloseHandleIfNeeded(childError, ref childError);
                }

                child.processHandle = processInfo.hProcess;
                processInfo.hProcess = IntPtr.Zero;
                threadHandle = processInfo.hThread;
                processInfo.hThread = IntPtr.Zero;
                child.Id = unchecked((int)processInfo.dwProcessId);

                child.jobHandle = CreateJobObjectW(IntPtr.Zero, null);
                if (child.jobHandle == IntPtr.Zero) ThrowLastError("CreateJobObject");
                var limits = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
                {
                    BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
                    {
                        LimitFlags = JobObjectLimitKillOnJobClose
                    }
                };
                if (!SetInformationJobObject(
                    child.jobHandle,
                    JobObjectExtendedLimitInformation,
                    ref limits,
                    (uint)Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION))))
                    ThrowLastError("SetInformationJobObject");
                if (!AssignProcessToJobObject(child.jobHandle, child.processHandle)) ThrowLastError("AssignProcessToJobObject");
                if (ResumeThread(threadHandle) == uint.MaxValue) ThrowLastError("ResumeThread");
                CloseHandleIfNeeded(threadHandle, ref threadHandle);

                child.standardInputHandle = new SafeFileHandle(parentInput, true);
                parentInput = IntPtr.Zero;
                child.standardInputStream = new FileStream(child.standardInputHandle, FileAccess.Write, 4096, false);
                child.standardInput = new StreamWriter(child.standardInputStream, new UTF8Encoding(false), 1024)
                {
                    NewLine = "\n"
                };

                child.standardOutputHandle = new SafeFileHandle(parentOutput, true);
                parentOutput = IntPtr.Zero;
                child.standardOutputStream = new FileStream(child.standardOutputHandle, FileAccess.Read, 4096, false);
                child.standardOutputReader = new StreamReader(child.standardOutputStream, new UTF8Encoding(false), false, 1024);
                child.outputLines = new BlockingCollection<OutputLine>();
                // Keep the old StreamReader-shaped property for the small headless tests and
                // callers outside the tactic runtime. It is backed by the same line queue.
                child.standardOutput = new StreamReader(new QueuedLineStream(child.outputLines), new UTF8Encoding(false), false, 1024);

                child.standardErrorHandle = new SafeFileHandle(parentError, true);
                parentError = IntPtr.Zero;
                child.standardErrorStream = new FileStream(child.standardErrorHandle, FileAccess.Read, 4096, false);
                child.standardError = new StreamReader(child.standardErrorStream, new UTF8Encoding(false), false, 1024);
                child.outputThread = new Thread(child.ReadOutputLines)
                {
                    IsBackground = true,
                    Name = "Win32ChildProcess stdout"
                };
                child.errorThread = new Thread(child.DrainError)
                {
                    IsBackground = true,
                    Name = "Win32ChildProcess stderr"
                };
                child.outputThread.Start();
                child.errorThread.Start();
                return child;
            }
            catch
            {
                CloseHandleIfNeeded(processInfo.hThread, ref processInfo.hThread);
                CloseHandleIfNeeded(processInfo.hProcess, ref processInfo.hProcess);
                CloseHandleIfNeeded(threadHandle, ref threadHandle);
                CloseHandleIfNeeded(parentInput, ref parentInput);
                CloseHandleIfNeeded(childInput, ref childInput);
                CloseHandleIfNeeded(parentOutput, ref parentOutput);
                CloseHandleIfNeeded(childOutput, ref childOutput);
                CloseHandleIfNeeded(parentError, ref parentError);
                CloseHandleIfNeeded(childError, ref childError);
                child.Dispose();
                throw;
            }
        }

        public bool HasExited
        {
            get
            {
                ThrowIfDisposed();
                uint result = WaitForSingleObject(processHandle, 0);
                if (result == WaitObject0) return true;
                if (result == WaitTimeout) return false;
                ThrowLastError("WaitForSingleObject");
                return false;
            }
        }

        public bool WaitForExit(int milliseconds)
        {
            ThrowIfDisposed();
            uint timeout = milliseconds < 0 ? Infinite : checked((uint)milliseconds);
            uint result = WaitForSingleObject(processHandle, timeout);
            if (result == WaitObject0) return true;
            if (result == WaitTimeout) return false;
            ThrowLastError("WaitForSingleObject");
            return false;
        }

        public void Kill()
        {
            ThrowIfDisposed();
            if (HasExited) return;
            if (!TerminateProcess(processHandle, 1)) ThrowLastError("TerminateProcess");
        }

        /// <summary>
        /// Takes one complete stdout line. A timeout is not an error; EOF and a reader
        /// failure are reported as IOException so the caller cannot mistake them for a
        /// slow tactic response.
        /// </summary>
        public bool TryReadStandardOutputLine(int milliseconds, out string line)
        {
            ThrowIfDisposed();
            OutputLine item;
            try
            {
                if (!outputLines.TryTake(out item, milliseconds))
                {
                    line = null;
                    return false;
                }
            }
            catch (InvalidOperationException)
            {
                throw new IOException("Pythonの子プロセスが終了しました。");
            }
            if (item.Error != null) throw new IOException("Pythonの標準出力の読取りに失敗しました。", item.Error);
            if (item.EndOfStream) throw new IOException("Pythonの子プロセスが終了しました。");
            line = item.Text;
            return true;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try
            {
                if (processHandle != IntPtr.Zero)
                {
                    uint state = WaitForSingleObject(processHandle, 0);
                    if (state == WaitTimeout) TerminateProcess(processHandle, 1);
                    WaitForSingleObject(processHandle, 2000);
                }
            }
            catch (Exception) { }

            try { if (standardInput != null) standardInput.Dispose(); } catch (Exception) { }
            try { if (standardOutput != null) standardOutput.Dispose(); } catch (Exception) { }
            try { if (standardOutputReader != null) standardOutputReader.Dispose(); } catch (Exception) { }
            try { if (standardError != null) standardError.Dispose(); } catch (Exception) { }
            try { if (standardInputStream != null) standardInputStream.Dispose(); } catch (Exception) { }
            try { if (standardOutputStream != null) standardOutputStream.Dispose(); } catch (Exception) { }
            try { if (standardErrorStream != null) standardErrorStream.Dispose(); } catch (Exception) { }
            try { if (standardInputHandle != null) standardInputHandle.Dispose(); } catch (Exception) { }
            try { if (standardOutputHandle != null) standardOutputHandle.Dispose(); } catch (Exception) { }
            try { if (standardErrorHandle != null) standardErrorHandle.Dispose(); } catch (Exception) { }
            try { if (outputThread != null) outputThread.Join(1000); } catch (Exception) { }
            try { if (errorThread != null) errorThread.Join(1000); } catch (Exception) { }
            CloseHandleIfNeeded(processHandle, ref processHandle);
            // Closing this job is the final guarantee that descendants do not outlive the game.
            CloseHandleIfNeeded(jobHandle, ref jobHandle);
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(Win32ChildProcess));
        }

        private void ReadOutputLines()
        {
            try
            {
                while (true)
                {
                    string line = standardOutputReader.ReadLine();
                    if (line == null)
                    {
                        PublishOutputLine(OutputLine.End());
                        return;
                    }
                    if (line.Length > MaxLineCharacters)
                    {
                        PublishOutputLine(OutputLine.Failure(new IOException("Pythonの応答が1Mi文字を超えています。")));
                        return;
                    }
                    PublishOutputLine(OutputLine.TextLine(line));
                }
            }
            catch (Exception error)
            {
                if (!disposed) PublishOutputLine(OutputLine.Failure(error));
            }
            finally
            {
                try { outputLines.CompleteAdding(); } catch (InvalidOperationException) { }
            }
        }

        private void DrainError()
        {
            var buffer = new char[1024];
            try
            {
                while (standardError.Read(buffer, 0, buffer.Length) != 0) { }
            }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
        }

        private void PublishOutputLine(OutputLine line)
        {
            try { outputLines.Add(line); } catch (InvalidOperationException) { }
        }

        private sealed class OutputLine
        {
            public string Text { get; private set; }
            public Exception Error { get; private set; }
            public bool EndOfStream { get; private set; }

            public static OutputLine TextLine(string text) => new OutputLine { Text = text };
            public static OutputLine Failure(Exception error) => new OutputLine { Error = error };
            public static OutputLine End() => new OutputLine { EndOfStream = true };
        }

        private sealed class QueuedLineStream : Stream
        {
            private readonly BlockingCollection<OutputLine> lines;
            private byte[] current;
            private int offset;

            public QueuedLineStream(BlockingCollection<OutputLine> lines)
            {
                this.lines = lines;
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (count == 0) return 0;
                while (current == null || this.offset == current.Length)
                {
                    OutputLine item;
                    try { item = lines.Take(); }
                    catch (InvalidOperationException) { return 0; }
                    if (item.Error != null) throw new IOException("Pythonの標準出力の読取りに失敗しました。", item.Error);
                    if (item.EndOfStream) return 0;
                    current = Encoding.UTF8.GetBytes(item.Text + "\n");
                    this.offset = 0;
                }
                int length = Math.Min(count, current.Length - this.offset);
                Array.Copy(current, this.offset, buffer, offset, length);
                this.offset += length;
                return length;
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        private static void CreatePipeOrThrow(out IntPtr read, out IntPtr write, ref SECURITY_ATTRIBUTES security, string name)
        {
            if (!CreatePipe(out read, out write, ref security, PipeBufferSize)) ThrowLastError("CreatePipe(" + name + ")");
        }

        private static void SetNonInheritableOrThrow(IntPtr handle, string name)
        {
            if (!SetHandleInformation(handle, HandleFlagInherit, 0)) ThrowLastError("SetHandleInformation(" + name + ")");
        }

        private static IntPtr BuildEnvironmentBlock(IDictionary<string, string> environment)
        {
            var names = environment == null ? new List<string>() : new List<string>(environment.Keys);
            names.Sort(StringComparer.OrdinalIgnoreCase);
            var block = new StringBuilder();
            foreach (string name in names)
            {
                if (string.IsNullOrEmpty(name) || name.IndexOf('=') >= 0 || name.IndexOf('\0') >= 0)
                    throw new ArgumentException("環境変数名が不正です。", nameof(environment));
                string value = environment[name] ?? string.Empty;
                if (value.IndexOf('\0') >= 0) throw new ArgumentException("環境変数の値が不正です。", nameof(environment));
                block.Append(name).Append('=').Append(value).Append('\0');
            }
            block.Append('\0');
            byte[] bytes = Encoding.Unicode.GetBytes(block.ToString());
            IntPtr result = Marshal.AllocHGlobal(bytes.Length);
            try
            {
                Marshal.Copy(bytes, 0, result, bytes.Length);
                return result;
            }
            catch
            {
                Marshal.FreeHGlobal(result);
                throw;
            }
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"").TrimEnd('\\') + "\"";
        }

        private static void ThrowLastError(string operation)
        {
            int error = Marshal.GetLastWin32Error();
            throw new Win32Exception(error, operation + " に失敗しました。Native error=" + error);
        }

        private static void CloseHandleIfNeeded(IntPtr handle, ref IntPtr location)
        {
            if (handle != IntPtr.Zero)
            {
                CloseHandle(handle);
                location = IntPtr.Zero;
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CreatePipe(out IntPtr readPipe, out IntPtr writePipe, ref SECURITY_ATTRIBUTES attributes, uint size);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);

        [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateProcessW(
            string applicationName,
            StringBuilder commandLine,
            IntPtr processAttributes,
            IntPtr threadAttributes,
            int inheritHandles,
            uint creationFlags,
            IntPtr environment,
            string currentDirectory,
            ref STARTUPINFO startupInfo,
            out PROCESS_INFORMATION processInformation);

        [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateJobObjectW(IntPtr attributes, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(IntPtr job, uint informationClass, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION information, uint informationLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint ResumeThread(IntPtr thread);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool TerminateProcess(IntPtr process, uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [StructLayout(LayoutKind.Sequential)]
        private struct SECURITY_ATTRIBUTES
        {
            public int nLength;
            public IntPtr lpSecurityDescriptor;
            public int bInheritHandle;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct STARTUPINFO
        {
            public int cb;
            public IntPtr lpReserved;
            public IntPtr lpDesktop;
            public IntPtr lpTitle;
            public int dwX;
            public int dwY;
            public int dwXSize;
            public int dwYSize;
            public int dwXCountChars;
            public int dwYCountChars;
            public int dwFillAttribute;
            public uint dwFlags;
            public short wShowWindow;
            public short cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_INFORMATION
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public uint dwProcessId;
            public uint dwThreadId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }
    }
}
