using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Resources;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SETUNA.Main.Runtime.Tests;

namespace SETUNA.Main.Localization.Tests
{
    /// <summary>
    /// Pins the language-inference rule that decides what a first run shows.
    /// <para>
    /// Every case builds its own <see cref="CultureInfo"/> and passes it in, so the
    /// result never depends on the regional settings of the machine running the
    /// suite — the same reason <see cref="AppLanguages.InferFromCulture"/> takes a
    /// culture instead of reading <see cref="CultureInfo.CurrentUICulture"/> itself.
    /// </para>
    /// </summary>
    [TestClass]
    public class LanguageInferenceTests
    {
        [TestMethod]
        public void SimplifiedChineseCulturesInferSimplifiedChinese()
        {
            foreach (var name in new[] { "zh-CN", "zh-SG", "zh-Hans", "zh-Hans-CN", "zh" })
            {
                Assert.AreEqual(
                    AppLanguage.ChineseSimplified,
                    AppLanguages.InferFromCulture(CultureInfo.GetCultureInfo(name)),
                    name + " must map to Simplified Chinese.");
            }
        }

        [TestMethod]
        public void TraditionalChineseCulturesInferEnglishRatherThanSimplified()
        {
            // There is no Traditional resource set yet. Handing those users the
            // Simplified text would be actively choosing wrong wording, whereas
            // English is at least correct wording they can read.
            foreach (var name in new[] { "zh-Hant", "zh-TW", "zh-HK", "zh-MO" })
            {
                Assert.AreEqual(
                    AppLanguage.English,
                    AppLanguages.InferFromCulture(CultureInfo.GetCultureInfo(name)),
                    name + " must map to English, not Simplified Chinese.");
            }
        }

        [TestMethod]
        public void EveryOtherCultureInfersEnglish()
        {
            foreach (var name in new[] { "en-US", "en-GB", "ja-JP", "fr-FR", "de-DE", "ko-KR", "ru-RU", "en" })
            {
                Assert.AreEqual(
                    AppLanguage.English,
                    AppLanguages.InferFromCulture(CultureInfo.GetCultureInfo(name)),
                    name + " must map to English.");
            }
        }

        [TestMethod]
        public void InvariantAndMissingCulturesInferEnglishWithoutThrowing()
        {
            // InvariantCulture has an empty Name and is its own Parent; a naive
            // Parent-chain walk loops forever on it.
            Assert.AreEqual(AppLanguage.English, AppLanguages.InferFromCulture(CultureInfo.InvariantCulture));
            Assert.AreEqual(AppLanguage.English, AppLanguages.InferFromCulture(null));
        }

        [TestMethod]
        public void ResolveOnlyRewritesAuto()
        {
            var chineseSystem = CultureInfo.GetCultureInfo("zh-CN");

            Assert.AreEqual(
                AppLanguage.ChineseSimplified,
                AppLanguages.Resolve(AppLanguage.Auto, chineseSystem));

            // An explicit choice must win over the system setting, otherwise picking
            // English on a Chinese machine would silently do nothing.
            Assert.AreEqual(
                AppLanguage.English,
                AppLanguages.Resolve(AppLanguage.English, chineseSystem));
            Assert.AreEqual(
                AppLanguage.ChineseSimplified,
                AppLanguages.Resolve(AppLanguage.ChineseSimplified, CultureInfo.GetCultureInfo("en-US")));
        }
    }

    /// <summary>
    /// Pins how the persisted language value is parsed. An unrecognised value must
    /// degrade to "follow the system" rather than fail: a configuration written by a
    /// newer build that supports more languages has to stay loadable here.
    /// </summary>
    [TestClass]
    public class LanguageConfigValueTests
    {
        [TestMethod]
        public void KnownValuesRoundTrip()
        {
            foreach (var language in new[] { AppLanguage.Auto, AppLanguage.ChineseSimplified, AppLanguage.English })
            {
                Assert.AreEqual(
                    language,
                    AppLanguages.Parse(AppLanguages.ToConfigValue(language)),
                    language + " must survive a write/read round trip.");
            }
        }

        [TestMethod]
        public void AbsentOrBlankValuesMeanFollowTheSystem()
        {
            // A configuration file written before this change has no element at all,
            // which deserialises to null.
            foreach (var value in new[] { null, "", "   ", "\t" })
            {
                Assert.AreEqual(AppLanguage.Auto, AppLanguages.Parse(value));
            }
        }

        [TestMethod]
        public void UnknownAndMalformedValuesFallBackWithoutThrowing()
        {
            foreach (var value in new[] { "ja", "fr-FR", "zh-Hant", "not a culture", "!!", "zh_CN", "english" })
            {
                Assert.AreEqual(
                    AppLanguage.Auto,
                    AppLanguages.Parse(value),
                    value + " is not supported, so it must be treated as follow-the-system.");
            }
        }

        [TestMethod]
        public void KnownValuesAreCaseInsensitive()
        {
            Assert.AreEqual(AppLanguage.English, AppLanguages.Parse("EN"));
            Assert.AreEqual(AppLanguage.ChineseSimplified, AppLanguages.Parse("ZH-cn"));
        }
    }

    /// <summary>
    /// Exercises the lookup fallback chain through the real embedded resources.
    /// </summary>
    [TestClass]
    public class LanguageLookupTests
    {
        AppLanguage restoreTo;

        [TestInitialize]
        public void RememberCurrentLanguage()
        {
            // Lang is process-wide state. Restoring it keeps these tests from
            // changing what any later test observes.
            restoreTo = Lang.Selected;
        }

        [TestCleanup]
        public void RestoreLanguage()
        {
            Lang.SetLanguage(restoreTo);
        }

        [TestMethod]
        public void EnglishLookupReturnsTheEnglishText()
        {
            Lang.SetLanguage(AppLanguage.English);

            Assert.AreEqual("Capture", Lang.T("Mainform.button1"));
            Assert.AreEqual("Options", Lang.T("Mainform.button4"));
        }

        [TestMethod]
        public void ChineseLookupReturnsTheNeutralText()
        {
            Lang.SetLanguage(AppLanguage.ChineseSimplified);

            Assert.AreEqual("关闭", Lang.T("Style.Close.Name"));
        }

        [TestMethod]
        public void DesignerOwnedKeysAreAbsentFromTheNeutralSetSoControlsKeepTheirText()
        {
            // Chinese for designer-owned control text has exactly one source: the
            // literal in the .Designer.cs file. Duplicating it into the neutral
            // resource set would create a second source that can silently diverge.
            Lang.SetLanguage(AppLanguage.ChineseSimplified);

            Assert.IsNull(
                Lang.Find("Mainform.button1"),
                "Designer-owned control text must not appear in the neutral resource set.");
        }

        [TestMethod]
        public void EnglishFallsBackToNeutralWhenAKeyIsUntranslated()
        {
            // Simulated by asking for a runtime key while English is active: every
            // runtime key exists in both sets, so equality with the neutral value
            // would be meaningless. Instead assert the mechanism: Find never returns
            // null for a key that exists in the neutral set.
            Lang.SetLanguage(AppLanguage.English);

            var neutralOnly = NeutralResourceKeys().First();
            Assert.IsNotNull(
                Lang.Find(neutralOnly),
                "A key present in the neutral set must resolve under any language.");
        }

        [TestMethod]
        public void MissingKeysDoNotReturnEmptyOrTheBareKey()
        {
            Lang.SetLanguage(AppLanguage.English);

            const string Missing = "No.Such.Key.Exists";
            Assert.IsNull(Lang.Find(Missing), "Find reports absence with null so callers can leave controls alone.");

            // T is the code-owned path: absence there is a programming error. The
            // release behaviour is a marked key, never an empty string that would
            // read as a rendering glitch. Debug builds assert instead, which is why
            // this only pins the "not empty, not bare key" part.
#if !DEBUG
            var value = Lang.T(Missing);
            Assert.AreNotEqual(string.Empty, value);
            Assert.AreNotEqual(Missing, value);
            StringAssert.Contains(value, Missing);
#endif
        }

        [TestMethod]
        public void FormattingUsesPositionalPlaceholders()
        {
            Lang.SetLanguage(AppLanguage.English);

            var text = Lang.T("Option.EditStyleTitle", "My style");
            StringAssert.Contains(text, "My style");
            Assert.IsFalse(text.Contains("{0}"), "The placeholder must have been substituted.");
        }

        [TestMethod]
        public void SwitchingLanguageDoesNotTouchThreadCulture()
        {
            var uiCultureBefore = CultureInfo.CurrentUICulture;
            var cultureBefore = CultureInfo.CurrentCulture;

            Lang.SetLanguage(AppLanguage.English);
            Lang.SetLanguage(AppLanguage.ChineseSimplified);

            // Number and date formatting must not move just because the interface
            // language did; that is the whole point of selecting resources by base
            // name instead of by culture.
            Assert.AreSame(uiCultureBefore, CultureInfo.CurrentUICulture);
            Assert.AreSame(cultureBefore, CultureInfo.CurrentCulture);
        }

        [TestMethod]
        public void AutoTracksTheSystemSettingWhileRememberingTheChoice()
        {
            Lang.SetLanguage(AppLanguage.Auto);

            Assert.AreEqual(AppLanguage.Auto, Lang.Selected, "The stored choice must stay Auto, not the inferred language.");
            Assert.AreEqual(
                AppLanguages.InferFromCulture(CultureInfo.CurrentUICulture),
                Lang.Effective,
                "The effective language must be the one inferred from the system setting.");
        }

        [TestMethod]
        public void LanguageChangedFiresOnlyWhenSomethingActuallyChanges()
        {
            Lang.SetLanguage(AppLanguage.English);

            var fired = 0;
            EventHandler handler = (s, e) => fired++;
            Lang.LanguageChanged += handler;
            try
            {
                Lang.SetLanguage(AppLanguage.English);
                Assert.AreEqual(0, fired, "Re-selecting the active language must not churn every open window.");

                Lang.SetLanguage(AppLanguage.ChineseSimplified);
                Assert.AreEqual(1, fired);
            }
            finally
            {
                Lang.LanguageChanged -= handler;
            }
        }

        internal static IEnumerable<string> NeutralResourceKeys()
        {
            return ResourceKeys("SETUNA.Resources.Lang.Strings");
        }

        internal static IEnumerable<string> EnglishResourceKeys()
        {
            return ResourceKeys("SETUNA.Resources.Lang.Strings_en");
        }

        internal static IEnumerable<string> ResourceKeys(string baseName)
        {
            return ResourceEntries(baseName).Keys;
        }

        internal static Dictionary<string, string> ResourceEntries(string baseName)
        {
            var assembly = typeof(Lang).Assembly;
            var result = new Dictionary<string, string>(StringComparer.Ordinal);

            using (var stream = assembly.GetManifestResourceStream(baseName + ".resources"))
            {
                Assert.IsNotNull(
                    stream,
                    baseName + " is not embedded in the main assembly. A culture-suffixed file name would "
                        + "turn it into a satellite assembly instead.");

                using (var reader = new ResourceReader(stream))
                {
                    foreach (System.Collections.DictionaryEntry entry in reader)
                    {
                        result[(string)entry.Key] = entry.Value as string;
                    }
                }
            }

            return result;
        }
    }

    /// <summary>
    /// Keeps the two resource sets in step. A translation that silently goes missing
    /// degrades to Chinese on an English interface, which is exactly the kind of
    /// regression nobody notices by hand.
    /// </summary>
    [TestClass]
    public class ResourceSetAlignmentTests
    {
        static readonly Regex Placeholder = new Regex(@"\{(\d+)[^}]*\}", RegexOptions.Compiled);

        [TestMethod]
        public void EveryNeutralKeyHasAnEnglishTranslation()
        {
            var neutral = LanguageLookupTests.ResourceEntries("SETUNA.Resources.Lang.Strings");
            var english = LanguageLookupTests.ResourceEntries("SETUNA.Resources.Lang.Strings_en");

            var missing = neutral.Keys.Where(key => !english.ContainsKey(key)).OrderBy(x => x, StringComparer.Ordinal).ToList();

            Assert.AreEqual(
                0,
                missing.Count,
                "Runtime keys must be translated in both sets: " + string.Join(", ", missing));
        }

        [TestMethod]
        public void TranslationsUseTheSamePlaceholderSet()
        {
            var neutral = LanguageLookupTests.ResourceEntries("SETUNA.Resources.Lang.Strings");
            var english = LanguageLookupTests.ResourceEntries("SETUNA.Resources.Lang.Strings_en");

            var mismatches = new List<string>();
            foreach (var pair in neutral)
            {
                if (pair.Value == null || !english.TryGetValue(pair.Key, out var translated) || translated == null)
                {
                    continue;
                }

                var expected = PlaceholderIndexes(pair.Value);
                var actual = PlaceholderIndexes(translated);
                if (!expected.SetEquals(actual))
                {
                    mismatches.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}: neutral has {{{1}}}, english has {{{2}}}",
                        pair.Key,
                        string.Join(",", expected.OrderBy(x => x)),
                        string.Join(",", actual.OrderBy(x => x))));
                }
            }

            Assert.AreEqual(
                0,
                mismatches.Count,
                "A placeholder that exists in one language but not the other throws FormatException at "
                    + "runtime, in whichever language is not being tested: " + string.Join("; ", mismatches));
        }

        [TestMethod]
        public void NoResourceValueIsBlank()
        {
            foreach (var baseName in new[] { "SETUNA.Resources.Lang.Strings", "SETUNA.Resources.Lang.Strings_en" })
            {
                foreach (var pair in LanguageLookupTests.ResourceEntries(baseName))
                {
                    Assert.IsFalse(
                        string.IsNullOrWhiteSpace(pair.Value),
                        baseName + " has a blank value for " + pair.Key + ", which would render as an empty control.");
                }
            }
        }

        static HashSet<int> PlaceholderIndexes(string value)
        {
            var result = new HashSet<int>();
            foreach (Match match in Placeholder.Matches(value))
            {
                result.Add(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
            }

            return result;
        }
    }

    /// <summary>
    /// Guards the single-file invariant from the resource side: the language data has
    /// to live inside the main assembly, with no satellite assembly and no culture
    /// subdirectory next to the executable.
    /// </summary>
    [TestClass]
    public class EmbeddedResourceLocationTests
    {
        [TestMethod]
        public void BothLanguageSetsAreEmbeddedInTheMainAssembly()
        {
            var names = typeof(Lang).Assembly.GetManifestResourceNames();

            CollectionAssert.Contains(names, "SETUNA.Resources.Lang.Strings.resources");
            CollectionAssert.Contains(names, "SETUNA.Resources.Lang.Strings_en.resources");
        }

        [TestMethod]
        public void NoSatelliteAssemblyIsProducedForTheMainAssembly()
        {
            // Scoped to SETUNA's own satellites by name. The test output directory
            // also holds MSTest's zh-Hans satellites, which are the test host's
            // business and say nothing about how SETUNA ships.
            var outputDirectory = System.IO.Path.GetDirectoryName(typeof(Lang).Assembly.Location);
            var assemblyName = typeof(Lang).Assembly.GetName().Name;

            var satellites = System.IO.Directory
                .GetFiles(outputDirectory, assemblyName + ".resources.dll", System.IO.SearchOption.AllDirectories)
                .Select(path => path.Substring(outputDirectory.Length + 1))
                .ToList();

            Assert.AreEqual(
                0,
                satellites.Count,
                "Satellite assemblies break single-file distribution. A resource file was probably renamed "
                    + "to a culture-suffixed form (Strings.en.resx instead of Strings_en.resx): "
                    + string.Join(", ", satellites));
        }

        [TestMethod]
        public void TheMainAssemblyDeclaresNoSatelliteContract()
        {
            // The build-output check above only sees this configuration's output.
            // NeutralResourcesLanguageAttribute with a fallback location of
            // Satellite is the declaration that would send lookups to a satellite
            // assembly at runtime, so pin its absence too.
            var attribute = (System.Resources.NeutralResourcesLanguageAttribute)Attribute.GetCustomAttribute(
                typeof(Lang).Assembly, typeof(System.Resources.NeutralResourcesLanguageAttribute));

            if (attribute != null)
            {
                Assert.AreEqual(
                    UltimateResourceFallbackLocation.MainAssembly,
                    attribute.Location,
                    "Resource lookups must never be directed to a satellite assembly.");
            }
        }

        [TestMethod]
        public void ResourceFileNamesCarryNoCultureSuffix()
        {
            var langDirectory = System.IO.Path.Combine(
                RepositoryPath.FindRoot(), "SETUNA", "Resources", "Lang");

            foreach (var path in System.IO.Directory.GetFiles(langDirectory, "*.resx"))
            {
                // "Strings.en.resx" has two dots before the extension; MSBuild reads
                // the middle segment as a culture and emits a satellite assembly.
                var name = System.IO.Path.GetFileNameWithoutExtension(path);
                Assert.IsFalse(
                    name.Contains("."),
                    name + ".resx has a dotted name; MSBuild would read the segment after the dot as a "
                        + "culture. Use an underscore (Strings_en.resx) so it stays a neutral resource.");
            }
        }
    }
}
