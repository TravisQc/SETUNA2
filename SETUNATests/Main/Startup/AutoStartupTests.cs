using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SETUNA.Main.Startup;

namespace SETUNA.Main.Startup.Tests
{
    /// <summary>
    /// Pins the format of the Run-key value. Windows parses it as a command line,
    /// so an unquoted path with spaces (<c>C:\Program Files\SETUNA\SETUNA.exe</c>)
    /// is read as <c>C:\Program</c> plus arguments and the app never starts.
    /// <para>
    /// Writing to the real Run key is not something a test should do, so the
    /// registry paths (missing key, key-release, error presentation) belong to the
    /// manual checklist; the value-format rule is asserted here.
    /// </para>
    /// </summary>
    [TestClass]
    public class AutoStartupTests
    {
        [TestMethod]
        public void APathWithSpacesIsQuoted()
        {
            var quoted = AutoStartup.QuoteExecutablePath(@"C:\Program Files\SETUNA\SETUNA.exe");

            Assert.AreEqual("\"C:\\Program Files\\SETUNA\\SETUNA.exe\"", quoted);
        }

        [TestMethod]
        public void APathWithoutSpacesIsAlsoQuoted()
        {
            // Quoting unconditionally is valid for both and keeps one code path.
            var quoted = AutoStartup.QuoteExecutablePath(@"C:\Tools\SETUNA.exe");

            Assert.AreEqual("\"C:\\Tools\\SETUNA.exe\"", quoted);
        }

        [TestMethod]
        public void AnAlreadyQuotedPathIsNotDoubleQuoted()
        {
            var quoted = AutoStartup.QuoteExecutablePath("\"C:\\Program Files\\SETUNA\\SETUNA.exe\"");

            Assert.AreEqual("\"C:\\Program Files\\SETUNA\\SETUNA.exe\"", quoted);
        }

        [TestMethod]
        public void TheQuotedValueRoundTripsBackToTheOriginalPath()
        {
            // What Windows does: strip the surrounding quotes, then launch.
            foreach (var path in new[]
            {
                @"C:\Program Files\SETUNA\SETUNA.exe",
                @"C:\Tools\SETUNA.exe",
                @"D:\my apps\setuna 3\SETUNA.exe",
            })
            {
                var quoted = AutoStartup.QuoteExecutablePath(path);

                Assert.AreEqual(path, quoted.Trim('"'));
                Assert.AreEqual(Path.GetFileName(path), Path.GetFileName(quoted.Trim('"')));
            }
        }

        [TestMethod]
        public void AnUnquotedPathWithSpacesWouldResolveToTheWrongExecutable()
        {
            // Documents the defect being fixed: the first whitespace-delimited token
            // of the old value is not the executable.
            const string PathWithSpaces = @"C:\Program Files\SETUNA\SETUNA.exe";

            var firstTokenUnquoted = PathWithSpaces.Split(' ')[0];
            Assert.AreEqual(@"C:\Program", firstTokenUnquoted);

            var quoted = AutoStartup.QuoteExecutablePath(PathWithSpaces);
            Assert.AreEqual(PathWithSpaces, quoted.Trim('"'), "The quoted form survives command-line parsing.");
        }

        [TestMethod]
        public void EmptyAndNullPathsArePassedThroughUnchanged()
        {
            Assert.IsNull(AutoStartup.QuoteExecutablePath(null));
            Assert.AreEqual(string.Empty, AutoStartup.QuoteExecutablePath(string.Empty));
        }

        [TestMethod]
        public void QueryingStartupStateDoesNotThrow()
        {
            // Read-only against the real Run key: must return a value, never throw,
            // even if the key cannot be opened.
            AutoStartup.IsSetup();
        }
    }
}
