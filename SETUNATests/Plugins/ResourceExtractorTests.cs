using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SETUNA.Plugins;

namespace SETUNA.Plugins.Tests
{
    /// <summary>
    /// Pins the provisioning of the embedded libwebp DLL: a fixed app-owned
    /// directory (not the process working directory), a complete write (the old
    /// code trusted a single <c>Stream.Read</c>), and an observable failure when
    /// the embedded resource is missing (it used to throw NullReference).
    /// </summary>
    [TestClass]
    public class ResourceExtractorTests
    {
        string workingDirectory;
        string originalWorkingDirectory;

        [TestInitialize]
        public void CreateWorkingDirectory()
        {
            originalWorkingDirectory = Directory.GetCurrentDirectory();
            workingDirectory = Path.Combine(Path.GetTempPath(), "SETUNATests-native", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workingDirectory);
        }

        [TestCleanup]
        public void RemoveWorkingDirectory()
        {
            Directory.SetCurrentDirectory(originalWorkingDirectory);

            if (workingDirectory != null && Directory.Exists(workingDirectory))
            {
                Directory.Delete(workingDirectory, true);
            }

            var parent = Path.Combine(Path.GetTempPath(), "SETUNATests-native");
            if (Directory.Exists(parent) && Directory.GetFileSystemEntries(parent).Length == 0)
            {
                Directory.Delete(parent);
            }
        }

        static string EmbeddedResourceName =>
            IntPtr.Size == 8 ? "SETUNA.Plugins.libwebp_x64.dll" : "SETUNA.Plugins.libwebp_x86.dll";

        static string ForeignEmbeddedResourceName =>
            IntPtr.Size == 8 ? "SETUNA.Plugins.libwebp_x86.dll" : "SETUNA.Plugins.libwebp_x64.dll";

        /// <summary>
        /// The other architecture's copy is dead payload: the name above is chosen by
        /// <c>IntPtr.Size</c>, so a build of this platform can never ask for it. Half a
        /// megabyte, in every artifact, unreachable — hence the platform condition on the
        /// <c>EmbeddedResource</c> items. This is the check that keeps it off.
        /// </summary>
        [TestMethod]
        public void OnlyThisArchitecturesNativeLibraryIsEmbedded()
        {
            var assembly = typeof(ResourceExtractor).Assembly;
            var names = assembly.GetManifestResourceNames();

            CollectionAssert.Contains(names, EmbeddedResourceName, "The matching libwebp must be embedded.");
            CollectionAssert.DoesNotContain(
                names,
                ForeignEmbeddedResourceName,
                ForeignEmbeddedResourceName + " cannot be loaded from this architecture's build, so embedding it "
                    + "only inflates the artifact. Check the Platform condition on the EmbeddedResource items.");
        }

        static long EmbeddedResourceLength
        {
            get
            {
                var assembly = typeof(ResourceExtractor).Assembly;
                using (var stream = assembly.GetManifestResourceStream(EmbeddedResourceName))
                {
                    Assert.IsNotNull(stream, "The libwebp resource must be embedded: " + EmbeddedResourceName);
                    return stream.Length;
                }
            }
        }

        [TestMethod]
        public void TheNativeDirectoryIsAppOwnedAndArchitectureSpecific()
        {
            var expectedRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SETUNA", "native");

            StringAssert.StartsWith(ResourceExtractor.NativeDirectory, expectedRoot);
            StringAssert.EndsWith(ResourceExtractor.NativeDirectory, IntPtr.Size == 8 ? "x64" : "x86");
            Assert.IsTrue(Path.IsPathRooted(ResourceExtractor.NativeDirectory),
                "A relative path would follow the process working directory.");
        }

        [TestMethod]
        public void TheNativeDirectoryDoesNotFollowTheWorkingDirectory()
        {
            var before = ResourceExtractor.NativeDirectory;

            Directory.SetCurrentDirectory(workingDirectory);

            Assert.AreEqual(before, ResourceExtractor.NativeDirectory);
        }

        [TestMethod]
        public void ExtractionWritesEveryByteOfTheResource()
        {
            var target = Path.Combine(workingDirectory, "libwebp.dll");

            Assert.IsTrue(ResourceExtractor.ExtractResourceToFile(EmbeddedResourceName, target));

            Assert.IsTrue(File.Exists(target));
            Assert.AreEqual(EmbeddedResourceLength, new FileInfo(target).Length,
                "A single Stream.Read is not guaranteed to return the whole resource.");
        }

        [TestMethod]
        public void ExtractionCreatesMissingDirectories()
        {
            var target = Path.Combine(workingDirectory, "nested", "deeper", "libwebp.dll");

            Assert.IsTrue(ResourceExtractor.ExtractResourceToFile(EmbeddedResourceName, target));

            Assert.IsTrue(File.Exists(target));
        }

        [TestMethod]
        public void AnUpToDateFileIsNotRewritten()
        {
            var target = Path.Combine(workingDirectory, "libwebp.dll");
            ResourceExtractor.ExtractResourceToFile(EmbeddedResourceName, target);
            var firstWrite = File.GetLastWriteTimeUtc(target);

            System.Threading.Thread.Sleep(50);
            Assert.IsTrue(ResourceExtractor.ExtractResourceToFile(EmbeddedResourceName, target));

            Assert.AreEqual(firstWrite, File.GetLastWriteTimeUtc(target), "An identical file must be left alone.");
        }

        [TestMethod]
        public void AStaleTruncatedFileIsReplaced()
        {
            // The failure mode the old code created: a half-written DLL on disk was
            // treated as valid forever, because it only checked File.Exists.
            var target = Path.Combine(workingDirectory, "libwebp.dll");
            File.WriteAllBytes(target, new byte[] { 0x4D, 0x5A, 0x00 });

            Assert.IsTrue(ResourceExtractor.ExtractResourceToFile(EmbeddedResourceName, target));

            Assert.AreEqual(EmbeddedResourceLength, new FileInfo(target).Length);
        }

        [TestMethod]
        public void AMissingResourceIsReportedAsFailureNotANullReference()
        {
            var target = Path.Combine(workingDirectory, "nothing.dll");

            var extracted = ResourceExtractor.ExtractResourceToFile("SETUNA.Plugins.does_not_exist.dll", target);

            Assert.IsFalse(extracted);
            Assert.IsFalse(File.Exists(target));
        }

        [TestMethod]
        public void ASuccessfulExtractionLeavesNoTemporaryFile()
        {
            var target = Path.Combine(workingDirectory, "libwebp.dll");

            ResourceExtractor.ExtractResourceToFile(EmbeddedResourceName, target);

            Assert.IsFalse(File.Exists(target + ".tmp"));
        }

        [TestMethod]
        public void ExtractWebPProvisionsAndLoadsTheLibrary()
        {
            // End to end against the real embedded DLL and a real LoadLibrary.
            Assert.IsTrue(ResourceExtractor.ExtractWebP());

            var expected = Path.Combine(
                ResourceExtractor.NativeDirectory,
                IntPtr.Size == 8 ? "libwebp_x64.dll" : "libwebp_x86.dll");
            Assert.IsTrue(File.Exists(expected), "Expected the DLL at " + expected);
            Assert.AreEqual(EmbeddedResourceLength, new FileInfo(expected).Length);
        }

        [TestMethod]
        public void ExtractWebPIsIdempotent()
        {
            Assert.IsTrue(ResourceExtractor.ExtractWebP());
            Assert.IsTrue(ResourceExtractor.ExtractWebP());
        }
    }
}
