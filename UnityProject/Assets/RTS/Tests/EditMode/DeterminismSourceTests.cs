using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Rts.Tests.EditMode
{
    public sealed class DeterminismSourceTests
    {
        private static readonly string[] PureFolders = { "Contracts", "Decision", "Simulation", "Replay", "Application" };

        // Lexical guard for the project's C# 9 source, not a semantic/type-flow analyzer.
        // Ban static imports of Math so unqualified floating-point functions cannot bypass the guard.
        private static readonly Regex Forbidden = new Regex(
            @"\b(?:UnityEngine|UnityEditor)\s*(?:\.|::)|\busing\s+(?:UnityEngine|UnityEditor)\b"
            + @"|\b(?:float|double|decimal|Single|Double|Decimal|Mathf|MathF|Stopwatch)\b"
            + @"|\bMath\s*\.\s*(?:Sqrt|Cbrt|Sin|Cos|Tan|Asin|Acos|Atan|Atan2|Sinh|Cosh|Tanh|Asinh|Acosh|Atanh|Pow|Exp|Log|Log2|Log10|Ceiling|Floor|Round|Truncate|IEEERemainder|FusedMultiplyAdd|ScaleB|ILogB|BitIncrement|BitDecrement|CopySign|ReciprocalEstimate|ReciprocalSqrtEstimate|SinCos|PI|E|Tau)\b"
            + @"|\busing\s+static\s+(?:global\s*::\s*)?(?:System\s*\.\s*)?Math\b"
            + @"|\bSystem\s*\.\s*Random\b|\bnew\s+Random\s*\("
            + @"|\bDateTime(?:Offset)?\s*\.\s*(?:Now|UtcNow)\b"
            + @"|\bEnvironment\s*\.\s*TickCount(?:64)?\b"
            + @"|\bGuid\s*\.\s*NewGuid\b|\bThread\s*\.\s*Sleep\b|\bTask\s*\.\s*Delay\b"
            + @"|^\s*#\s*(?:if|elif|else|endif)\b",
            RegexOptions.Multiline | RegexOptions.CultureInvariant);

        [Test]
        public void PureSourcesContainNoForbiddenApis()
        {
            string root = FindRepositoryRoot();
            var violations = new List<string>();
            foreach (string folder in PureFolders)
            {
                string directory = Path.Combine(root, "UnityProject", "Assets", "RTS", folder);
                Assert.That(Directory.Exists(directory), Is.True, directory);
                string[] files = Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories);
                Assert.That(files, Is.Not.Empty, directory);
                foreach (string file in files.OrderBy(f => f, StringComparer.Ordinal))
                    violations.AddRange(Inspect(file.Substring(root.Length + 1), File.ReadAllText(file)));
            }
            Assert.That(violations, Is.Empty, string.Join("\n", violations));
        }

        private static string FindRepositoryRoot()
        {
            foreach (string start in new[] { TestContext.CurrentContext.TestDirectory, Directory.GetCurrentDirectory() })
                for (var directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
                    if (File.Exists(Path.Combine(directory.FullName, "Headless", "Rts.Headless.slnx"))
                        && Directory.Exists(Path.Combine(directory.FullName, "UnityProject", "Assets", "RTS")))
                        return directory.FullName;
            throw new DirectoryNotFoundException("Cannot locate the repository source tree for determinism checks.");
        }

        private static string[] Inspect(string file, string source)
        {
            var lexer = new SourceMask(source);
            string code = lexer.Read();
            string[] lines = source.Split('\n');
            var failures = new List<string>();
            foreach (Match match in Forbidden.Matches(code))
            {
                // A directive's leading whitespace may include previous blank lines.
                int start = match.Index;
                while (start < match.Index + match.Length && char.IsWhiteSpace(code[start])) start++;
                int line = 1;
                for (int i = 0; i < start; i++) if (source[i] == '\n') line++;
                if (!lexer.AllowedLines.Contains(line))
                    failures.Add(file + ":" + line + ": " + lines[line - 1].TrimEnd('\r').Trim()
                        + " [" + match.Value.Trim() + "]");
            }
            return failures.ToArray();
        }

        // Preserve offsets and newlines, including in multiline comments and verbatim strings.
        // Interpolation text is masked; interpolation expressions are recursively scanned as code.
        private sealed class SourceMask
        {
            private readonly string source;
            private readonly char[] code;
            private int position;
            public readonly HashSet<int> AllowedLines = new HashSet<int>();

            public SourceMask(string source)
            {
                this.source = source;
                code = source.Select(c => c == '\n' || c == '\r' ? c : ' ').ToArray();
            }

            public string Read()
            {
                ReadCode(false);
                return new string(code);
            }

            private bool At(string text) => string.CompareOrdinal(source, position, text, 0, text.Length) == 0;

            private void ReadCode(bool interpolation)
            {
                int braces = 0;
                int parentheses = 0;
                while (position < source.Length)
                {
                    if (At("//"))
                    {
                        int start = position;
                        while (position < source.Length && source[position] != '\n') position++;
                        string comment = source.Substring(start + 2, position - start - 2).Trim();
                        const string marker = "determinism-allow:";
                        if (comment.StartsWith(marker, StringComparison.Ordinal)
                            && !string.IsNullOrWhiteSpace(comment.Substring(marker.Length)))
                            AllowedLines.Add(1 + source.Take(start).Count(c => c == '\n'));
                    }
                    else if (At("/*"))
                    {
                        position += 2;
                        while (position < source.Length && !At("*/")) position++;
                        position = Math.Min(source.Length, position + 2);
                    }
                    else if (At("$@\"") || At("@$\"")) { position += 3; ReadString(true, true); }
                    else if (At("$\"")) { position += 2; ReadString(false, true); }
                    else if (At("@\"")) { position += 2; ReadString(true, false); }
                    else if (source[position] == '"') { position++; ReadString(false, false); }
                    else if (source[position] == '\'')
                    {
                        position++;
                        while (position < source.Length)
                        {
                            char c = source[position++];
                            if (c == '\\' && position < source.Length) position++;
                            else if (c == '\'') break;
                        }
                    }
                    else
                    {
                        char c = source[position];
                        if (interpolation && c == '}' && braces == 0) { position++; return; }
                        if (interpolation && c == ':' && braces == 0 && parentheses == 0 && !At("::"))
                        {
                            // A format specifier is literal text, not executable code.
                            while (position < source.Length && source[position] != '}') position++;
                            if (position < source.Length) position++;
                            return;
                        }
                        if (c == '{') braces++;
                        if (c == '}') braces--;
                        if (c == '(' || c == '[') parentheses++;
                        if (c == ')' || c == ']') parentheses--;
                        code[position++] = c;
                        if (interpolation && c == ':' && position < source.Length && source[position] == ':')
                            code[position++] = ':';
                    }
                }
            }

            private void ReadString(bool verbatim, bool interpolated)
            {
                while (position < source.Length)
                {
                    char c = source[position++];
                    if (c == '"')
                    {
                        if (verbatim && position < source.Length && source[position] == '"') position++;
                        else return;
                    }
                    else if (!verbatim && c == '\\' && position < source.Length) position++;
                    else if (interpolated && c == '{')
                    {
                        if (position < source.Length && source[position] == '{') position++;
                        else ReadCode(true);
                    }
                    else if (interpolated && c == '}' && position < source.Length && source[position] == '}') position++;
                }
            }
        }

        [TestCase("using UnityEngine;")]
        [TestCase("using U = global::UnityEditor.Editor;")]
        [TestCase("UnityEngine /* comment */ . Vector3 value;")]
        [TestCase("float value;")]
        [TestCase("double value;")]
        [TestCase("decimal value;")]
        [TestCase("System.Double value;")]
        [TestCase("Math.Sqrt(2);")]
        [TestCase("System.Math.Sin(2);")]
        [TestCase("Math.Pow(2, 3);")]
        [TestCase("using static System.Math;")]
        [TestCase("Mathf.Abs(1);")]
        [TestCase("MathF.Cos(1);")]
        [TestCase("using R = System.Random;")]
        [TestCase("new Random(1);")]
        [TestCase("DateTime.Now;")]
        [TestCase("DateTime.UtcNow;")]
        [TestCase("Environment.TickCount;")]
        [TestCase("Stopwatch.StartNew();")]
        [TestCase("Guid.NewGuid();")]
        [TestCase("Thread.Sleep(1);")]
        [TestCase("Task.Delay(1);")]
        [TestCase("#if UNITY_EDITOR\n#endif")]
        [TestCase("#if !CUSTOM\n#elif UNITY_STANDALONE\n#endif")]
        [TestCase("$\"literal {DateTime.Now}\"")]
        [TestCase("$@\"literal {Math.Sqrt(2)}\"")]
        [TestCase("$\"nested {$\"{Guid.NewGuid()}\"}\"")]
        [TestCase("$\"{global::System.DateTime.Now}\"")]
        public void ForbiddenCodeIsDetected(string source)
        {
            Assert.That(Inspect("Sample.cs", source), Is.Not.Empty, source);
        }

        [TestCase("// float DateTime.Now\n/* UnityEngine.\nTask.Delay(1); */ int x;")]
        [TestCase("\"escaped \\\" float DateTime.Now\"")]
        [TestCase("@\"multiline\n\"\" double UnityEditor.\"")]
        [TestCase("'\\\"'; '\\''; '\\n';")]
        [TestCase("$\"float {{DateTime.Now}} {1:double}\"")]
        [TestCase("@$\"UnityEngine. {{Task.Delay}} {1}\"")]
        [TestCase("$\"{\"DateTime.Now\"}\"")]
        [TestCase("int floating; int doubleValue; Math.Abs(-1); Math.Min(1, 2);")]
        [TestCase("DateTime.Now; // determinism-allow: test-only clock\nint x;")]
        public void CommentsLiteralsAndReasonedExceptionsAreIgnored(string source)
        {
            Assert.That(Inspect("Sample.cs", source), Is.Empty, source);
        }

        [Test]
        public void ExceptionsApplyOnlyToTheirOwnLineAndRequireAnActualCommentAndReason()
        {
            var failures = Inspect("Sample.cs", "DateTime.Now; // determinism-allow: reason\r\n"
                + "DateTime.UtcNow;\r\nfloat x; // determinism-allow:\r\n"
                + "double y; /* determinism-allow: reason */\r\n"
                + "decimal z; var s = \"// determinism-allow: reason\";");
            Assert.That(failures.Length, Is.EqualTo(4));
            for (int i = 0; i < failures.Length; i++)
                Assert.That(failures[i], Does.StartWith("Sample.cs:" + (i + 2) + ":"));
        }

        [Test]
        public void DiagnosticsRetainLineNumbersAcrossCommentsStringsAndSplitMemberAccess()
        {
            var failures = Inspect("Sample.cs", "/* ignored\nfloat */\n@\"double\ntext\";\n"
                + "DateTime /* split\nmember */ . Now;\n\n#if UNITY_EDITOR");
            Assert.That(failures.Length, Is.EqualTo(2));
            Assert.That(failures[0], Does.StartWith("Sample.cs:5:"));
            Assert.That(failures[1], Does.StartWith("Sample.cs:8:"));
        }
    }
}
