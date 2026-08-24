using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SETUNA.Main.Cache;

namespace SETUNA.Main.Runtime.Tests
{
    /// <summary>
    /// Guards the .NET Framework 4.8 runtime configuration of the single-file build.
    /// <para>
    /// There is no <c>app.config</c> any more: DPI awareness is declared by the
    /// manifest that gets linked into <c>SETUNA.exe</c>, and the TLS switches are
    /// applied by <see cref="RuntimeConfiguration"/> at startup. The negative half
    /// of <c>net48-network-security</c> (no fixed
    /// <c>ServicePointManager.SecurityProtocol</c> list, no process-wide
    /// <c>ServerCertificateValidationCallback</c>) is carried by that spec rather
    /// than asserted here: verifying it needs IL scanning, and the previous
    /// implementation — grepping every .cs file for those identifiers — broke on
    /// harmless edits while never exercising a request path. The positive half *is*
    /// asserted, as observable process state, below.
    /// </para>
    /// <para>
    /// These tests guard the build *inputs* (manifest, project file, absence of
    /// app.config). The corresponding output invariant — no <c>SETUNA.exe.config</c>
    /// next to the exe — is enforced by the <c>CreateReleasePackage</c> target,
    /// which the test host cannot observe.
    /// </para>
    /// </summary>
    [TestClass]
    public class RuntimeConfigurationTests
    {
        [TestMethod]
        public void StartupAppliesSystemDefaultTlsPolicy()
        {
            RuntimeConfiguration.Apply();

            Assert.IsTrue(
                AppContext.TryGetSwitch("Switch.System.Net.DontEnableSchUseStrongCrypto", out var dontEnableStrongCrypto),
                "The switch must be set explicitly, not left to the target-framework default.");
            Assert.IsFalse(dontEnableStrongCrypto);

            Assert.IsTrue(
                AppContext.TryGetSwitch("Switch.System.Net.DontEnableSystemDefaultTlsVersions", out var dontEnableSystemDefaults),
                "The switch must be set explicitly, not left to the target-framework default.");
            Assert.IsFalse(dontEnableSystemDefaults);

            // SystemDefault is "whatever the Windows policy allows", not a fixed
            // protocol list, so this assertion does not conflict with the
            // net48-network-security requirement that forbids pinning protocols.
            Assert.AreEqual(SecurityProtocolType.SystemDefault, ServicePointManager.SecurityProtocol);
        }

        [TestMethod]
        public void SingleFileBuildCarriesNoApplicationConfiguration()
        {
            var repositoryRoot = RepositoryPath.FindRoot();

            Assert.IsFalse(
                File.Exists(Path.Combine(repositoryRoot, "SETUNA", "app.config")),
                "app.config would be copied next to the exe as SETUNA.exe.config, breaking single-file distribution.");

            // MSBuild copies a referenced exe's .config alongside the exe itself, so
            // the test output directory shows whether the build produced one.
            var deployedExe = typeof(CacheItem).Assembly.Location;
            Assert.IsFalse(
                File.Exists(deployedExe + ".config"),
                "The build emitted " + Path.GetFileName(deployedExe) + ".config. Either a stale artifact needs cleaning, "
                    + "or a configuration file has been reintroduced.");

            // Read as XML rather than text: these two properties are the only
            // mechanism that generates a SETUNA.exe.config out of thin air when no
            // app.config exists, so their absence is the invariant worth pinning.
            var project = new XmlDocument();
            project.Load(Path.Combine(repositoryRoot, "SETUNA", "SETUNA.csproj"));
            var msbuild = new XmlNamespaceManager(project.NameTable);
            msbuild.AddNamespace("msb", "http://schemas.microsoft.com/developer/msbuild/2003");

            foreach (var property in new[] { "AutoGenerateBindingRedirects", "GenerateBindingRedirectsOutputType" })
            {
                Assert.IsNull(
                    project.SelectSingleNode("/msb:Project/msb:PropertyGroup/msb:" + property, msbuild),
                    property + " must stay disabled: it emits SETUNA.exe.config as soon as a dependency needs a redirect.");
            }
        }

        [TestMethod]
        public void LegacyDpiFallbackIsAbsentFromTheCompiledAssembly()
        {
            // Asserted against the compiled assembly rather than the .cs text: a
            // rename or reformat cannot break this, but re-adding the P/Invoke —
            // under any C# method name, including via an explicit EntryPoint — does.
            var offenders = new List<string>();

            foreach (var type in GetLoadableTypes(typeof(CacheItem).Assembly))
            {
                const BindingFlags Everything = BindingFlags.Public | BindingFlags.NonPublic
                    | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

                foreach (var method in type.GetMethods(Everything))
                {
                    var import = method.GetCustomAttribute<DllImportAttribute>();
                    if (import == null)
                    {
                        continue;
                    }

                    var entryPoint = string.IsNullOrEmpty(import.EntryPoint) ? method.Name : import.EntryPoint;
                    if (entryPoint == "SetProcessDPIAware"
                        || entryPoint == "SetProcessDpiAwareness"
                        || entryPoint == "SetProcessDpiAwarenessContext")
                    {
                        offenders.Add(type.FullName + "." + method.Name + " -> " + entryPoint);
                    }
                }
            }

            Assert.AreEqual(
                0,
                offenders.Count,
                "Process DPI awareness comes from the manifest; no API may set it: " + string.Join(", ", offenders));
        }

        [TestMethod]
        public void ManifestDeclaresPerMonitorV2DpiAwareness()
        {
            var manifest = new XmlDocument();
            manifest.Load(Path.Combine(RepositoryPath.FindRoot(), "SETUNA", "app.manifest"));

            // The manifest is a build artifact whose content *is* the behavior: it is
            // linked into SETUNA.exe and read by the OS at process creation, which is
            // what makes per-monitor-v2 awareness survive single-file distribution.
            var namespaces = new XmlNamespaceManager(manifest.NameTable);
            namespaces.AddNamespace("asmv1", "urn:schemas-microsoft-com:asm.v1");
            namespaces.AddNamespace("asmv3", "urn:schemas-microsoft-com:asm.v3");
            namespaces.AddNamespace("ws2005", "http://schemas.microsoft.com/SMI/2005/WindowsSettings");
            namespaces.AddNamespace("ws2016", "http://schemas.microsoft.com/SMI/2016/WindowsSettings");

            const string WindowsSettings = "/asmv1:assembly/asmv3:application/asmv3:windowsSettings/";
            Assert.AreEqual(
                "true/pm",
                GetSettingValue(manifest, WindowsSettings + "ws2005:dpiAware", namespaces));
            Assert.AreEqual(
                "PerMonitorV2",
                GetSettingValue(manifest, WindowsSettings + "ws2016:dpiAwareness", namespaces));

            // Windows 10 compatibility is a precondition for PerMonitorV2 taking effect.
            Assert.IsNotNull(manifest.SelectSingleNode(
                "/asmv1:assembly/*[local-name()='compatibility']/*[local-name()='application']"
                    + "/*[local-name()='supportedOS'][@Id='{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}']",
                namespaces));
        }

        static string GetSettingValue(XmlDocument manifest, string xpath, XmlNamespaceManager namespaces)
        {
            var element = manifest.SelectSingleNode(xpath, namespaces) as XmlElement;
            Assert.IsNotNull(element, "Missing manifest element: " + xpath);

            return element.InnerText.Trim();
        }

        static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.Where(x => x != null);
            }
        }
    }

    /// <summary>
    /// Exercises cache-item round-tripping against a throwaway root so the suite
    /// never reads or writes the user's real %LOCALAPPDATA%\SETUNA data.
    /// </summary>
    [TestClass]
    public class CacheItemTests
    {
        string cacheRoot;

        static string TestRootParent => Path.Combine(Path.GetTempPath(), "SETUNATests");

        [TestInitialize]
        public void RedirectCacheRootToATemporaryDirectory()
        {
            cacheRoot = Path.Combine(TestRootParent, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(cacheRoot);
            CacheManager.SetRoot(cacheRoot);
        }

        [TestCleanup]
        public void RestoreCacheRoot()
        {
            // Runs after passing *and* failing tests, so a failed assertion cannot
            // leave a directory behind that a later run would pick up.
            CacheManager.SetRoot(null);

            if (cacheRoot != null && Directory.Exists(cacheRoot))
            {
                Directory.Delete(cacheRoot, true);
            }

            // Remove the shared parent too, but only once the last test has
            // released its own directory — leaving an empty shell in %TEMP%
            // would still be litter.
            TryDeleteIfEmpty(TestRootParent);
        }

        static void TryDeleteIfEmpty(string directory)
        {
            try
            {
                if (Directory.Exists(directory)
                    && Directory.GetFileSystemEntries(directory).Length == 0)
                {
                    Directory.Delete(directory);
                }
            }
            catch (IOException)
            {
                // A concurrently running test class still owns it; harmless.
            }
        }

        [TestMethod]
        public void CacheRootIsRedirectedAwayFromTheUsersRealData()
        {
            Assert.AreEqual(cacheRoot, CacheManager.Path);

            var realRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SETUNA");
            Assert.AreNotEqual(realRoot, CacheManager.Path);
        }

        [TestMethod]
        public void CacheRootFallsBackToTheDefaultWhenCleared()
        {
            CacheManager.SetRoot(null);

            var realRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SETUNA");
            Assert.AreEqual(realRoot, CacheManager.Path);
        }

        [TestMethod]
        public void ReadImageReturnsBitmapIndependentFromCacheFile()
        {
            CacheItem item;

            using (var source = new Bitmap(13, 7, PixelFormat.Format32bppArgb))
            {
                source.SetPixel(3, 2, Color.CornflowerBlue);
                item = CacheItem.Create(DateTime.Now, source, Point.Empty, new SETUNA.Main.Cache.Style(0, Point.Empty));
            }

            var imagePath = Path.Combine(item.FolderPath, "Image.png");
            using (var restored = item.ReadImage())
            {
                Assert.IsNotNull(restored);
                File.Delete(imagePath);

                Assert.AreEqual(13, restored.Width);
                Assert.AreEqual(7, restored.Height);
                using (var output = new MemoryStream())
                {
                    restored.Save(output, ImageFormat.Png);
                    Assert.IsTrue(output.Length > 0);
                }
            }
        }

        [TestMethod]
        public void ReadImageReturnsNullForCorruptCacheWithoutLockingFile()
        {
            CacheItem item;

            using (var source = new Bitmap(2, 2))
            {
                item = CacheItem.Create(DateTime.Now, source, Point.Empty, new SETUNA.Main.Cache.Style(0, Point.Empty));
            }

            var imagePath = Path.Combine(item.FolderPath, "Image.png");
            File.WriteAllText(imagePath, "not an image");

            Assert.IsNull(item.ReadImage());
            using (File.Open(imagePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
            }
        }

        [TestMethod]
        public void CacheItemsAreWrittenUnderTheRedirectedRoot()
        {
            CacheItem item;

            using (var source = new Bitmap(4, 4))
            {
                item = CacheItem.Create(DateTime.Now, source, new Point(11, 22), new SETUNA.Main.Cache.Style(0, Point.Empty));
            }

            StringAssert.StartsWith(item.FolderPath, cacheRoot);
            Assert.IsTrue(File.Exists(Path.Combine(item.FolderPath, "Image.png")));
            Assert.IsTrue(item.IsValid);
        }
    }

    internal static class RepositoryPath
    {
        public static string FindRoot()
        {
            var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "SETUNA.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate the SETUNA repository root.");
        }
    }
}
