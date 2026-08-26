using System;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SETUNA.Main.Localization;
using SETUNA.Main.Option;

namespace SETUNA.Main.Localization.Tests
{
    /// <summary>
    /// The language setting is a new element in an existing serialized format, so the
    /// risk is not that it fails to round-trip but that adding it disturbs everything
    /// around it. These tests work on throwaway files under %TEMP% and never touch
    /// the user's real configuration.
    /// </summary>
    [TestClass]
    public class LanguageOptionPersistenceTests
    {
        string directory;
        string configFile;

        [TestInitialize]
        public void CreateTemporaryConfigDirectory()
        {
            directory = Path.Combine(Path.GetTempPath(), "SETUNATests-language", Guid.NewGuid().ToString("N"));
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

            var parent = Path.Combine(Path.GetTempPath(), "SETUNATests-language");
            if (Directory.Exists(parent) && Directory.GetFileSystemEntries(parent).Length == 0)
            {
                Directory.Delete(parent);
            }
        }

        [TestMethod]
        public void ConfigurationWithoutALanguageElementStillLoads()
        {
            // Exactly the situation on every existing installation: the file was
            // written before this change, so the element simply is not there.
            var original = SetunaOption.GetDefaultOption();
            original.Setuna.DustBoxCapacity = 9;
            original.Setuna.SelectAreaTransparent = 42;
            original.Setuna.TopMostEnabled = true;

            var xml = Serialize(original);
            var stripped = RemoveElement(xml, "Language");
            Assert.AreNotEqual(xml, stripped, "The test fixture must actually remove the element.");

            var restored = Deserialize(stripped);

            // The pre-existing settings must come back untouched...
            Assert.AreEqual(9, restored.Setuna.DustBoxCapacity);
            Assert.AreEqual(42, restored.Setuna.SelectAreaTransparent);
            Assert.IsTrue(restored.Setuna.TopMostEnabled);
            Assert.AreEqual(original.Styles.Count, restored.Styles.Count);

            // ...and the absent element must read as "follow the system".
            Assert.IsNull(restored.Setuna.Language);
            Assert.AreEqual(AppLanguage.Auto, AppLanguages.Parse(restored.Setuna.Language));
        }

        [TestMethod]
        public void LanguageChoiceSurvivesARoundTripThroughDisk()
        {
            var original = SetunaOption.GetDefaultOption();
            original.Setuna.Language = AppLanguages.ToConfigValue(AppLanguage.English);

            File.WriteAllText(configFile, Serialize(original));
            var restored = Deserialize(File.ReadAllText(configFile));

            Assert.AreEqual(AppLanguage.English, AppLanguages.Parse(restored.Setuna.Language));
        }

        [TestMethod]
        public void UnknownLanguageValueLoadsAsFollowTheSystem()
        {
            // A configuration written by a future build that supports more languages
            // must stay loadable rather than failing at startup. Set the value on the
            // object rather than patching the XML: a string replace that silently
            // matches nothing would make this test assert nothing at all.
            var original = SetunaOption.GetDefaultOption();
            original.Setuna.Language = "ja-JP";

            var xml = Serialize(original);
            StringAssert.Contains(xml, "ja-JP", "The fixture value must actually reach the serialized form.");

            var restored = Deserialize(xml);

            Assert.AreEqual("ja-JP", restored.Setuna.Language, "The unknown value round-trips as written...");
            Assert.AreEqual(
                AppLanguage.Auto,
                AppLanguages.Parse(restored.Setuna.Language),
                "...and is interpreted as follow-the-system rather than throwing.");
        }

        [TestMethod]
        public void AddingLanguageDoesNotRenameOrReorderExistingElements()
        {
            // Guards the "no format change" half of option-persistence-integrity: the
            // only difference a pre-change reader should see is one extra element.
            var xml = Serialize(SetunaOption.GetDefaultOption());
            var document = new XmlDocument();
            document.LoadXml(xml);

            var setuna = document.SelectSingleNode("/SetunaOption/Setuna");
            Assert.IsNotNull(setuna, "The Setuna element must keep its name and position.");

            foreach (var expected in new[]
            {
                "AppType", "ShowMainWindow", "DupType", "ShowSplashWindow", "SelectLineSolid",
                "SelectAreaTransparent", "DustBoxEnable", "DustBoxCapacity", "TopMostEnabled",
            })
            {
                Assert.IsNotNull(
                    setuna.SelectSingleNode(expected),
                    expected + " disappeared or was renamed; existing configuration files would lose it.");
            }

            // A default configuration carries the element with an empty value. Left as
            // null it would be omitted entirely, which parses the same but hides the
            // setting from anyone reading the file.
            var language = setuna.SelectSingleNode("Language");
            Assert.IsNotNull(language, "A newly written configuration should show the Language element.");
            Assert.AreEqual(string.Empty, language.InnerText);
        }

        static string Serialize(SetunaOption option)
        {
            var serializer = new XmlSerializer(typeof(SetunaOption), SetunaOption.GetAllType());
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, option);
                return writer.ToString();
            }
        }

        static SetunaOption Deserialize(string xml)
        {
            var serializer = new XmlSerializer(typeof(SetunaOption), SetunaOption.GetAllType());
            using (var reader = new StringReader(xml))
            {
                return (SetunaOption)serializer.Deserialize(reader);
            }
        }

        static string RemoveElement(string xml, string elementName)
        {
            var document = new XmlDocument();
            document.LoadXml(xml);

            foreach (var node in document.SelectNodes("//" + elementName).Cast<XmlNode>().ToList())
            {
                node.ParentNode.RemoveChild(node);
            }

            return document.OuterXml;
        }
    }

    /// <summary>
    /// The other half of the coverage contract: things that MUST NOT move when the
    /// language changes. Registry keys and saved user data look language-adjacent, so
    /// they are the plausible places for a well-meant translation to break behaviour.
    /// </summary>
    [TestClass]
    public class NonLocalizedValueTests
    {
        AppLanguage restoreTo;

        [TestInitialize]
        public void RememberCurrentLanguage()
        {
            restoreTo = Lang.Selected;
        }

        [TestCleanup]
        public void RestoreLanguage()
        {
            Lang.SetLanguage(restoreTo);
        }

        [TestMethod]
        public void AutoStartupRegistryIdentifiersAreLanguageIndependent()
        {
            // Reads the private constants rather than writing to the registry: the
            // suite must not touch the user's real Run key. A translated key name
            // would orphan the existing entry, leaving startup silently enabled.
            var type = typeof(SETUNA.Main.Startup.AutoStartup);
            const System.Reflection.BindingFlags Hidden =
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;

            Lang.SetLanguage(AppLanguage.ChineseSimplified);
            var chineseKey = type.GetField("Key", Hidden).GetValue(null);
            var chinesePath = type.GetField("RunKeyPath", Hidden).GetValue(null);

            Lang.SetLanguage(AppLanguage.English);
            var englishKey = type.GetField("Key", Hidden).GetValue(null);
            var englishPath = type.GetField("RunKeyPath", Hidden).GetValue(null);

            Assert.AreEqual("SETUNA_AutoStartup", chineseKey);
            Assert.AreEqual(chineseKey, englishKey);
            Assert.AreEqual(chinesePath, englishPath);
        }

        [TestMethod]
        public void SavedStyleNamesAreUserDataAndAreNotRewritten()
        {
            // A style name in the config file is whatever the user last saw, possibly
            // renamed by hand. Switching language must not rewrite it.
            var style = new SETUNA.Main.Style.CStyle { StyleName = "我自己命名的操作" };

            Lang.SetLanguage(AppLanguage.English);
            Assert.AreEqual("我自己命名的操作", style.StyleName);
            Assert.AreEqual("我自己命名的操作", style.GetName());

            Lang.SetLanguage(AppLanguage.ChineseSimplified);
            Assert.AreEqual("我自己命名的操作", style.StyleName);
        }

        [TestMethod]
        public void DefaultStyleNamesFollowTheLanguageActiveWhenTheConfigIsCreated()
        {
            // The flip side: names generated for a brand-new configuration should be
            // written in the language in effect at that moment.
            Lang.SetLanguage(AppLanguage.English);
            var english = SetunaOption.GetDefaultOption();

            Lang.SetLanguage(AppLanguage.ChineseSimplified);
            var chinese = SetunaOption.GetDefaultOption();

            Assert.AreEqual("Copy", english.Styles[0].StyleName);
            Assert.AreEqual("复制", chinese.Styles[0].StyleName);
        }

        [TestMethod]
        public void PresetStyleIdentifiersDoNotMoveWithLanguage()
        {
            // Preset styles are referenced from the config by numeric ID; only their
            // display names are localized.
            Lang.SetLanguage(AppLanguage.English);
            var english = new SETUNA.Main.Style.CCaptureStyle();

            Lang.SetLanguage(AppLanguage.ChineseSimplified);
            var chinese = new SETUNA.Main.Style.CCaptureStyle();

            Assert.AreEqual(english.StyleID, chinese.StyleID);
            Assert.AreEqual("New scrap", english.StyleName);
            Assert.AreEqual("制作参考图", chinese.StyleName);
        }
    }
}
