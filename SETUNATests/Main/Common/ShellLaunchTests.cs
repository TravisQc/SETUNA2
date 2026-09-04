using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SETUNA.Main.Common;
using SETUNA.Main.Localization;
using SETUNA.Main.Runtime.Tests;

namespace SETUNA.Main.Common.Tests
{
    /// <summary>
    /// Covers the two "open something outside the app" entry points: the About-page
    /// links and the Open-cache-folder button.
    /// <para>
    /// Both stopped working when the project moved to .NET 8, with
    /// <c>Win32Exception (5): An error occurred trying to start process</c>.
    /// <see cref="ProcessStartInfo.UseShellExecute"/> defaults to <c>false</c> on .NET
    /// while it defaulted to <c>true</c> on .NET Framework 4.8, so
    /// <c>Process.Start(url)</c> silently changed meaning from "hand this to the shell"
    /// to "run this file" — and neither a URL nor a directory is an executable.
    /// </para>
    /// <para>
    /// The launch itself is not asserted: it would open a browser and an Explorer window
    /// on whatever machine runs the suite. What is asserted is everything around it —
    /// the start info that broke, the accept/reject rule for targets, the folder
    /// preparation, and the invariant that no call site bypasses the helper.
    /// </para>
    /// </summary>
    [TestClass]
    public class ShellLaunchTests
    {
        /// <summary>
        /// Matches <c>Process.Start(</c> and <c>System.Diagnostics.Process.Start(</c> in
        /// any overload, but not the instance call <c>process.Start()</c> — that is a
        /// different API and not the one whose default flipped. The lookbehind excludes
        /// only word characters, so a qualified call still matches while
        /// <c>MyProcess.Start(</c> does not.
        /// </summary>
        const string DirectProcessStart = @"(?<!\w)Process\s*\.\s*Start\s*\(";

        string temporaryRoot;

        // Shared with the other suites that need scratch directories, so the last one
        // out removes the shell.
        static string TemporaryRootParent => Path.Combine(Path.GetTempPath(), "SETUNATests");

        [TestInitialize]
        public void CreateTemporaryRoot()
        {
            // One directory per test method, removed afterwards, so a failing assertion
            // cannot leave litter that a later run picks up.
            temporaryRoot = Path.Combine(TemporaryRootParent, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporaryRoot);
        }

        [TestCleanup]
        public void RemoveTemporaryRoot()
        {
            if (temporaryRoot != null && Directory.Exists(temporaryRoot))
            {
                Directory.Delete(temporaryRoot, true);
            }

            try
            {
                if (Directory.Exists(TemporaryRootParent)
                    && Directory.GetFileSystemEntries(TemporaryRootParent).Length == 0)
                {
                    Directory.Delete(TemporaryRootParent);
                }
            }
            catch (IOException)
            {
                // Another test class still owns it; harmless.
            }
        }

        [TestMethod]
        public void TheRuntimeDefaultIsTheDefectBeingFixed()
        {
            // Not a tautology: this is the runtime behaviour the helper exists to
            // override, and the assumption that would have to be revisited if a future
            // runtime ever restored the .NET Framework default.
            Assert.IsFalse(
                new ProcessStartInfo("https://example.com").UseShellExecute,
                "Process.Start would try to execute the URL as a program.");
        }

        [TestMethod]
        public void StartInfoAsksTheShellToOpenTheTargetUnchanged()
        {
            foreach (var target in new[]
            {
                "https://github.com/TravisQc/SETUNA2",
                @"C:\Users\Someone\AppData\Local\SETUNA",
                @"D:\folder with spaces\cache",
            })
            {
                var startInfo = ShellUtils.StartInfoFor(target);

                Assert.IsTrue(startInfo.UseShellExecute, target + " must go through ShellExecute.");

                // ShellExecute takes the target as a single name rather than a command
                // line, so a path with spaces needs no quoting — and MUST NOT get any.
                Assert.AreEqual(target, startInfo.FileName);
                Assert.AreEqual(string.Empty, startInfo.Arguments);
            }
        }

        [TestMethod]
        public void TheLinksShippedInTheInterfaceAreAccepted()
        {
            // The exact strings the About page and the Picasa panel hand to the helper.
            foreach (var url in new[]
            {
                "http://www.clearunit.com/clearup/setuna2/",
                "https://github.com/TravisQc/SETUNA2",
                "https://picasaweb.google.com/",
            })
            {
                Assert.IsTrue(ShellUtils.IsHttpUrl(url), url + " is one of the links in the interface.");
            }
        }

        [TestMethod]
        public void OnlyHttpAndHttpsAreAccepted()
        {
            // ShellExecute resolves its target through the registry, so it will happily
            // launch an executable, a script or a registered custom protocol. The links
            // reach the helper as control data, which must not carry that much authority.
            foreach (var target in new[]
            {
                @"C:\Windows\System32\cmd.exe",
                "file:///C:/Windows/System32/cmd.exe",
                "shell:startup",
                "javascript:alert(1)",
                "ftp://example.com/",
                "mailto:someone@example.com",
                "www.example.com",
                "example.com/page",
                "not a url at all",
                "   ",
                "",
                null,
            })
            {
                Assert.IsFalse(
                    ShellUtils.IsHttpUrl(target),
                    "Accepted a target that is not an http(s) address: " + (target ?? "<null>"));
            }
        }

        [TestMethod]
        public void AMissingFolderIsCreatedRatherThanReportedAsAFailure()
        {
            // What "Open cache folder" does after the user has deleted the directory:
            // the button means "show me this location", and ShellExecute cannot open a
            // path that does not exist.
            var folder = Path.Combine(temporaryRoot, "created-on-demand");
            Assert.IsFalse(Directory.Exists(folder));

            Assert.IsTrue(ShellUtils.EnsureFolder(folder));
            Assert.IsTrue(Directory.Exists(folder));

            // Idempotent: the normal case is that the folder is already there.
            Assert.IsTrue(ShellUtils.EnsureFolder(folder));
        }

        [TestMethod]
        public void NestedFoldersAreCreatedInOnePass()
        {
            var folder = Path.Combine(temporaryRoot, "one", "two", "three");

            Assert.IsTrue(ShellUtils.EnsureFolder(folder));
            Assert.IsTrue(Directory.Exists(folder));
        }

        [TestMethod]
        public void AnExistingFileIsNotTreatedAsAFolder()
        {
            var path = Path.Combine(temporaryRoot, "not-a-folder.txt");
            File.WriteAllText(path, "x");

            Assert.IsFalse(ShellUtils.EnsureFolder(path), "A file must be reported as unopenable, not created over.");
            Assert.IsTrue(File.Exists(path), "The file must survive the attempt.");
        }

        [TestMethod]
        public void BlankFolderPathsAreRejectedWithoutThrowing()
        {
            foreach (var path in new[] { null, "", "   " })
            {
                Assert.IsFalse(ShellUtils.EnsureFolder(path));
            }
        }

        [TestMethod]
        public void BothFailureMessagesAreTranslatedAndCarryTheTarget()
        {
            var restoreTo = Lang.Selected;
            try
            {
                foreach (var language in new[] { AppLanguage.ChineseSimplified, AppLanguage.English })
                {
                    Lang.SetLanguage(language);

                    foreach (var key in new[] { "Message.OpenUrlFailed", "Message.OpenFolderFailed" })
                    {
                        var text = Lang.T(key, "TARGET");

                        Assert.IsFalse(
                            text.StartsWith("!", StringComparison.Ordinal),
                            language + " has no text for " + key + ".");
                        StringAssert.Contains(
                            text,
                            "TARGET",
                            key + " must name what could not be opened, in " + language + ".");

                        // The neutral .resx mixes structural \r\r\n line endings with the
                        // plain \r\n that separates the two lines *inside* a value. Editing
                        // it with a tool that normalises line endings swaps one for the
                        // other, and the message grows a blank line — the ResX reader hands
                        // the value back verbatim rather than folding CR LF pairs. Both keys
                        // are exactly "sentence, then target", like Message.SaveFailed.
                        var normalized = text.Replace("\r\n", "\n");

                        Assert.AreEqual(
                            2,
                            normalized.Split('\n').Length,
                            key + " must be two lines in " + language + ", got: " + Escape(text));
                        Assert.IsFalse(
                            normalized.Contains('\r'),
                            key + " has a stray carriage return in " + language + ", so the .resx line "
                                + "endings got normalised: " + Escape(text));
                    }
                }
            }
            finally
            {
                Lang.SetLanguage(restoreTo);
            }
        }

        /// <summary>
        /// No application code may call <see cref="Process.Start(string)"/> or any of its
        /// overloads directly.
        /// <para>
        /// This is the regression guard for the whole class of defect: the call compiles,
        /// looks exactly like the .NET Framework code it was ported from, and fails only
        /// when a user clicks it. Routing every launch through <see cref="ShellUtils"/>
        /// means the decision about <see cref="ProcessStartInfo.UseShellExecute"/> is
        /// made once, in a place that explains itself.
        /// </para>
        /// </summary>
        [TestMethod]
        public void NoApplicationCodeCallsProcessStartDirectly()
        {
            var offenders = new List<string>();
            var applicationRoot = Path.Combine(RepositoryPath.FindRoot(), "SETUNA");

            foreach (var file in Directory.GetFiles(applicationRoot, "*.cs", SearchOption.AllDirectories))
            {
                if (IsBuildOutput(file) || Path.GetFileName(file) == "ShellUtils.cs")
                {
                    continue;
                }

                var lines = File.ReadAllLines(file);
                for (var i = 0; i < lines.Length; i++)
                {
                    if (Regex.IsMatch(lines[i], DirectProcessStart))
                    {
                        offenders.Add(Path.GetFileName(file) + ":" + (i + 1) + " " + lines[i].Trim());
                    }
                }
            }

            Assert.AreEqual(
                0,
                offenders.Count,
                "Process.Start defaults to UseShellExecute = false on .NET, so a URL or a folder path "
                    + "throws Win32Exception (5) at the click. Call ShellUtils.OpenUrl / ShellUtils.OpenFolder "
                    + "instead, or extend ShellUtils if a genuine child process is needed:"
                    + Environment.NewLine + string.Join(Environment.NewLine, offenders));
        }

        [TestMethod]
        public void TheGuardWouldActuallyCatchTheOriginalCallSites()
        {
            // Proves the pattern is not vacuous: these are the lines that shipped the
            // defect, verbatim, plus a spaced variant.
            foreach (var line in new[]
            {
                "            System.Diagnostics.Process.Start(linkLabel2.Links[0].LinkData.ToString());",
                "            System.Diagnostics.Process.Start(Cache.CacheManager.Path);",
                "            Process.Start(linkLabel1.Text);",
                "            Process.Start (path);",
            })
            {
                Assert.IsTrue(
                    Regex.IsMatch(line, DirectProcessStart),
                    "The guard pattern missed: " + line.Trim());
            }

            // And that it does not fire on the shapes it is not about.
            foreach (var line in new[]
            {
                "            using (var process = new Process()) { process.Start(); }",
                "            ShellUtils.OpenUrl(url);",
                "            var info = new ProcessStartInfo(target);",
                "            MyProcess.Start(x);",
            })
            {
                Assert.IsFalse(
                    Regex.IsMatch(line, DirectProcessStart),
                    "The guard pattern over-matched: " + line.Trim());
            }
        }

        static bool IsBuildOutput(string path)
        {
            return path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)
                || path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar);
        }

        static string Escape(string value)
        {
            return value.Replace("\r", "\\r").Replace("\n", "\\n");
        }
    }
}
