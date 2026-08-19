using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SETUNA.Main.Cache;

namespace SETUNA.Main.Runtime.Tests
{
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
        public void LegacyDpiAndNetworkOverridesAreAbsent()
        {
            var repositoryRoot = RepositoryPath.FindRoot();
            var programSource = File.ReadAllText(Path.Combine(repositoryRoot, "SETUNA", "Program.cs"));
            var windowsApiSource = File.ReadAllText(Path.Combine(repositoryRoot, "SETUNA", "Main", "Common", "WindowsAPI.cs"));
            var manifest = File.ReadAllText(Path.Combine(repositoryRoot, "SETUNA", "app.manifest"));

            Assert.IsFalse(programSource.Contains("Environment.OSVersion"));
            Assert.IsFalse(programSource.Contains("SetProcessDPIAware"));
            Assert.IsFalse(windowsApiSource.Contains("SetProcessDPIAware"));
            Assert.IsFalse(manifest.Contains("SetProcessDPIAware"));

            var sourceFiles = Directory.GetFiles(Path.Combine(repositoryRoot, "SETUNA"), "*.cs", SearchOption.AllDirectories);
            foreach (var sourceFile in sourceFiles)
            {
                var source = File.ReadAllText(sourceFile);
                Assert.IsFalse(source.Contains("ServerCertificateValidationCallback"), sourceFile);
                Assert.IsFalse(source.Contains("ServicePointManager.SecurityProtocol"), sourceFile);
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

    [TestClass]
    public class CacheItemTests
    {
        [TestMethod]
        public void ReadImageReturnsBitmapIndependentFromCacheFile()
        {
            var createTime = CreateUniqueCacheTimestamp();
            CacheItem item = null;

            try
            {
                using (var source = new Bitmap(13, 7, PixelFormat.Format32bppArgb))
                {
                    source.SetPixel(3, 2, Color.CornflowerBlue);
                    item = CacheItem.Create(createTime, source, Point.Empty, new SETUNA.Main.Cache.Style(0, Point.Empty));
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
            finally
            {
                DeleteCacheItemDirectory(item);
            }
        }

        [TestMethod]
        public void ReadImageReturnsNullForCorruptCacheWithoutLockingFile()
        {
            var createTime = CreateUniqueCacheTimestamp();
            CacheItem item = null;

            try
            {
                using (var source = new Bitmap(2, 2))
                {
                    item = CacheItem.Create(createTime, source, Point.Empty, new SETUNA.Main.Cache.Style(0, Point.Empty));
                }

                var imagePath = Path.Combine(item.FolderPath, "Image.png");
                File.WriteAllText(imagePath, "not an image");

                Assert.IsNull(item.ReadImage());
                using (File.Open(imagePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                }
            }
            finally
            {
                DeleteCacheItemDirectory(item);
            }
        }

        private static DateTime CreateUniqueCacheTimestamp()
        {
            var firstDay = new DateTime(2099, 1, 1);
            for (var attempt = 0; attempt < 20; attempt++)
            {
                var slot = (int)((uint)Guid.NewGuid().GetHashCode() % 8640000);
                var candidate = firstDay.AddMilliseconds(slot * 10L);
                var folderName = candidate.ToString("yyyy-MM-dd HH-mm-ss-ff");
                if (!Directory.Exists(Path.Combine(CacheManager.Path, folderName)))
                {
                    return candidate;
                }
            }

            throw new InvalidOperationException("Could not allocate a unique cache test directory.");
        }

        private static void DeleteCacheItemDirectory(CacheItem item)
        {
            if (item != null && Directory.Exists(item.FolderPath))
            {
                Directory.Delete(item.FolderPath, true);
            }
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
