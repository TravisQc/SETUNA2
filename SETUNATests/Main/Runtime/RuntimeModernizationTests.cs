using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SETUNA.Main.Cache;

namespace SETUNA.Main.Runtime.Tests
{
    /// <summary>
    /// Guards the .NET Framework 4.8 runtime configuration.
    /// <para>
    /// The TLS constraints of <c>net48-network-security</c> (no fixed
    /// <c>ServicePointManager.SecurityProtocol</c> list, no process-wide
    /// <c>ServerCertificateValidationCallback</c>) are carried by that spec rather
    /// than asserted here: verifying them needs IL scanning, and the previous
    /// implementation — grepping every .cs file for those identifiers — broke on
    /// harmless edits while never exercising a request path. The positive half of
    /// the requirement (the AppContext switches that enable strong crypto and
    /// system-default TLS) *is* asserted, from app.config, below.
    /// </para>
    /// </summary>
    [TestClass]
    public class RuntimeConfigurationTests
    {
        [TestMethod]
        public void Net48RuntimeConfigurationUsesPerMonitorV2()
        {
            var repositoryRoot = RepositoryPath.FindRoot();
            var config = new XmlDocument();
            config.Load(Path.Combine(repositoryRoot, "SETUNA", "app.config"));

            Assert.AreEqual("true", GetWinFormsSetting(config, "EnableWindowsFormsHighDpiAutoResizing"));
            Assert.AreEqual("PerMonitorV2", GetWinFormsSetting(config, "DpiAwareness"));

            var supportedRuntime = config.SelectSingleNode("/configuration/startup/supportedRuntime") as XmlElement;
            Assert.IsNotNull(supportedRuntime);
            Assert.AreEqual(".NETFramework,Version=v4.8", supportedRuntime.GetAttribute("sku"));

            var appContextSwitches = config.SelectSingleNode("/configuration/runtime/AppContextSwitchOverrides") as XmlElement;
            Assert.IsNotNull(appContextSwitches);
            var switchValue = appContextSwitches.GetAttribute("value");
            StringAssert.Contains(switchValue, "Switch.System.Net.DontEnableSchUseStrongCrypto=false");
            StringAssert.Contains(switchValue, "Switch.System.Net.DontEnableSystemDefaultTlsVersions=false");
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
                    if (entryPoint == "SetProcessDPIAware" || entryPoint == "SetProcessDpiAwareness")
                    {
                        offenders.Add(type.FullName + "." + method.Name + " -> " + entryPoint);
                    }
                }
            }

            Assert.AreEqual(
                0,
                offenders.Count,
                "The legacy process-wide DPI API must not be declared: " + string.Join(", ", offenders));
        }

        [TestMethod]
        public void ManifestDoesNotReintroduceALegacyDpiFallback()
        {
            var manifest = File.ReadAllText(Path.Combine(RepositoryPath.FindRoot(), "SETUNA", "app.manifest"));

            // The manifest is a build artifact whose content *is* the behavior:
            // a dpiAware/dpiAwareness element there overrides the WinForms
            // configuration asserted by Net48RuntimeConfigurationUsesPerMonitorV2.
            Assert.IsFalse(manifest.Contains("SetProcessDPIAware"));
            Assert.IsFalse(manifest.Contains("<dpiAware>"));
            Assert.IsFalse(manifest.Contains("<dpiAwareness>"));
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

        private static string GetWinFormsSetting(XmlDocument config, string key)
        {
            var settings = config.SelectNodes("/configuration/System.Windows.Forms.ApplicationConfigurationSection/add");
            foreach (XmlElement setting in settings)
            {
                if (setting.GetAttribute("key") == key)
                {
                    return setting.GetAttribute("value");
                }
            }

            return null;
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
