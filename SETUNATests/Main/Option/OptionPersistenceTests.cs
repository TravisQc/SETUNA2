using System;
using System.IO;
using System.Xml.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SETUNA.Main.Common;
using SETUNA.Main.Option;

namespace SETUNA.Main.Option.Tests
{
    /// <summary>
    /// Pins the atomic-write contract behind SaveOption and the backward
    /// compatibility of the on-disk config format.
    /// </summary>
    [TestClass]
    public class OptionPersistenceTests
    {
        string directory;
        string configFile;

        [TestInitialize]
        public void CreateTemporaryConfigDirectory()
        {
            directory = Path.Combine(Path.GetTempPath(), "SETUNATests-config", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            configFile = Path.Combine(directory, "SetunaConfig.xml");
        }

        [TestCleanup]
        public void RemoveTemporaryConfigDirectory()
        {
            if (directory != null && Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }

            var parent = Path.Combine(Path.GetTempPath(), "SETUNATests-config");
            if (Directory.Exists(parent) && Directory.GetFileSystemEntries(parent).Length == 0)
            {
                Directory.Delete(parent);
            }
        }

        [TestMethod]
        public void WriteCreatesTheTargetWhenItDoesNotExist()
        {
            AtomicFile.Write(configFile, stream => WriteText(stream, "first version"));

            Assert.AreEqual("first version", File.ReadAllText(configFile));
        }

        [TestMethod]
        public void WriteReplacesAnExistingTarget()
        {
            File.WriteAllText(configFile, "old version");

            AtomicFile.Write(configFile, stream => WriteText(stream, "new version"));

            Assert.AreEqual("new version", File.ReadAllText(configFile));
        }

        [TestMethod]
        public void AFailedWriteLeavesTheExistingConfigIntact()
        {
            File.WriteAllText(configFile, "the user's real settings");

            var thrown = Assert.ThrowsException<InvalidOperationException>(() =>
                AtomicFile.Write(configFile, stream =>
                {
                    // Write a partial payload, then fail — exactly the shape of a
                    // serializer blowing up midway through.
                    WriteText(stream, "half a co");
                    throw new InvalidOperationException("serialization failed");
                }));

            Assert.AreEqual("serialization failed", thrown.Message);
            Assert.AreEqual("the user's real settings", File.ReadAllText(configFile),
                "A failed save must not truncate or replace the previous config.");
        }

        [TestMethod]
        public void AFailedWriteDoesNotCreateTheTargetOrLeaveATemporaryFile()
        {
            Assert.ThrowsException<InvalidOperationException>(() =>
                AtomicFile.Write(configFile, stream => throw new InvalidOperationException("boom")));

            Assert.IsFalse(File.Exists(configFile), "No config file should appear after a failed first save.");
            Assert.IsFalse(File.Exists(configFile + ".tmp"), "The temporary file must be cleaned up.");
        }

        [TestMethod]
        public void ASuccessfulWriteLeavesNoTemporaryFileBehind()
        {
            AtomicFile.Write(configFile, stream => WriteText(stream, "content"));

            Assert.IsFalse(File.Exists(configFile + ".tmp"));
            CollectionAssert.AreEqual(
                new[] { "SetunaConfig.xml" },
                Array.ConvertAll(Directory.GetFiles(directory), Path.GetFileName));
        }

        [TestMethod]
        public void RepeatedWritesDoNotLeakFileHandles()
        {
            for (var i = 0; i < 50; i++)
            {
                var payload = "version " + i;
                AtomicFile.Write(configFile, stream => WriteText(stream, payload));
            }

            Assert.AreEqual("version 49", File.ReadAllText(configFile));

            // If a handle were still open the exclusive open below would throw.
            using (File.Open(configFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
            }
        }

        [TestMethod]
        public void AnOptionWrittenAtomicallyRoundTripsThroughTheRealSerializer()
        {
            var option = new SetunaOption
            {
                MainWindowWidth = 520,
                MainWindowHeight = 300
            };
            var serializer = new XmlSerializer(option.GetType(), SetunaOption.GetAllType());

            AtomicFile.Write(configFile, stream => serializer.Serialize(stream, option));

            using (var stream = new FileStream(configFile, FileMode.Open, FileAccess.Read))
            {
                var restored = (SetunaOption)serializer.Deserialize(stream);
                Assert.AreEqual(520, restored.MainWindowWidth);
                Assert.AreEqual(300, restored.MainWindowHeight);
            }
        }

        [TestMethod]
        public void AConfigWithoutTheWindowSizeElementsStillLoads()
        {
            // Backward compatibility: MainWindowWidth/Height were added by a later
            // change, so a config written before it has no such elements. It must
            // still deserialize, with the "no saved size" signal of 0.
            var serializer = new XmlSerializer(typeof(SetunaOption), SetunaOption.GetAllType());

            File.WriteAllText(configFile, BuildLegacyConfig());

            using (var stream = new FileStream(configFile, FileMode.Open, FileAccess.Read))
            {
                var restored = (SetunaOption)serializer.Deserialize(stream);

                Assert.IsNotNull(restored);
                Assert.AreEqual(0, restored.MainWindowWidth);
                Assert.AreEqual(0, restored.MainWindowHeight);
            }
        }

        /// <summary>
        /// Serializes a default option, then strips the window-size elements to
        /// reproduce a config file written by a pre-persistence build.
        /// </summary>
        static string BuildLegacyConfig()
        {
            var serializer = new XmlSerializer(typeof(SetunaOption), SetunaOption.GetAllType());
            string xml;
            using (var buffer = new MemoryStream())
            {
                serializer.Serialize(buffer, new SetunaOption());
                xml = System.Text.Encoding.UTF8.GetString(buffer.ToArray());
            }

            foreach (var element in new[] { "MainWindowWidth", "MainWindowHeight" })
            {
                xml = System.Text.RegularExpressions.Regex.Replace(
                    xml, "\\s*<" + element + ">[^<]*</" + element + ">", string.Empty);
                xml = System.Text.RegularExpressions.Regex.Replace(
                    xml, "\\s*<" + element + " />", string.Empty);
            }

            Assert.IsFalse(xml.Contains("MainWindowWidth"), "The legacy fixture must not carry the new elements.");
            return xml;
        }

        static void WriteText(Stream stream, string text)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(text);
            stream.Write(bytes, 0, bytes.Length);
        }
    }
}
