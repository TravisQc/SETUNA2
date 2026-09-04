using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SETUNA.Main.Runtime.Tests
{
    /// <summary>
    /// Guards the release publish configuration — the build *inputs* only.
    /// <para>
    /// The output invariants (one executable, no companion file, every format decoding
    /// from a copy standing alone in an empty directory) cannot be observed from a test
    /// host that runs against a directory build: `SelfContained` and `PublishSingleFile`
    /// are publish-time properties, and the whole point of the checks is what happens
    /// inside a bundle. Those live in <c>scripts/verify-publish.ps1</c>, which publishes
    /// both architectures in both runtime modes and then runs each executable's own
    /// <c>--self-test</c>.
    /// </para>
    /// </summary>
    [TestClass]
    public class ReleasePublishTests
    {
        [TestMethod]
        public void TheProjectProvidesAReleasePublishTargetThatValidatesBeforeCopying()
        {
            var project = LoadApplicationProject();

            var publish = SelectTarget(project, "PublishReleaseSingleFile");
            Assert.IsNotNull(publish, "The project must own the release publish entry point.");
            Assert.IsNotNull(
                SelectTarget(project, "ValidateReleasePublishOutput"),
                "The companion-file invariant needs an enforcement point.");

            // Order matters: a validation that ran after the copy would publish the very
            // artifact it was meant to reject.
            var body = publish.InnerXml;
            var validate = body.IndexOf("ValidateReleasePublishOutput", StringComparison.Ordinal);
            var copy = body.IndexOf("<Copy", StringComparison.Ordinal);

            Assert.IsTrue(validate >= 0 && copy >= 0, "The target must both validate and copy.");
            Assert.IsTrue(validate < copy, "Validation must run before the artifact is copied to publish\\.");
        }

        [TestMethod]
        public void ThePublishTargetAsksForASingleFileBundleInEitherMode()
        {
            var publish = SelectTarget(LoadApplicationProject(), "PublishReleaseSingleFile").InnerXml;

            foreach (var property in new[]
            {
                "PublishSingleFile=true",
                "IncludeNativeLibrariesForSelfExtract=true",
                "SelfContained=$(ReleaseSelfContained)",
                "EnableCompressionInSingleFile=$(ReleaseEnableCompression)",
            })
            {
                StringAssert.Contains(publish, property, "The nested Publish must pass " + property + ".");
            }
        }

        /// <summary>
        /// The two release variants, and the single property that separates them.
        /// </summary>
        [TestMethod]
        public void TheDefaultModeIsSelfContainedAndPortableTurnsItOff()
        {
            var project = LoadApplicationProject();

            // Publishing without naming a mode must keep producing what it always produced,
            // under the name it always had.
            var mode = SelectProperty(project, "ReleaseRuntimeMode");
            Assert.IsNotNull(mode, "The publish target needs a default runtime mode.");
            Assert.AreEqual("selfcontained", mode.InnerText.Trim());
            StringAssert.Contains(
                mode.GetAttribute("Condition"),
                "'$(ReleaseRuntimeMode)' == ''",
                "The default must yield to an explicit -p:ReleaseRuntimeMode.");

            var selfContained = SelectProperties(project, "ReleaseSelfContained");
            Assert.AreEqual(2, selfContained.Count, "One unconditional default plus one portable override.");
            Assert.AreEqual("true", selfContained[0].InnerText.Trim());
            Assert.AreEqual(
                string.Empty,
                selfContained[0].GetAttribute("Condition"),
                "The default must not be conditional, or an unknown mode leaves it undefined.");
            Assert.AreEqual("false", selfContained[1].InnerText.Trim());
            StringAssert.Contains(selfContained[1].GetAttribute("Condition"), "portable");
        }

        /// <summary>
        /// NETSDK1176: compression inside a single-file bundle exists only for
        /// self-contained apps, so asking for it on the portable variant is a hard build
        /// error. Deriving the one switch from the other is what makes them impossible to
        /// desynchronise.
        /// </summary>
        [TestMethod]
        public void CompressionIsAskedForExactlyWhenTheBundleIsSelfContained()
        {
            var compression = SelectProperty(LoadApplicationProject(), "ReleaseEnableCompression");

            Assert.IsNotNull(compression, "The nested Publish reads this, so it must be defined.");
            Assert.AreEqual("$(ReleaseSelfContained)", compression.InnerText.Trim());
        }

        /// <summary>
        /// An unrecognised mode must stop the publish rather than silently produce the
        /// wrong thing: <c>ReleaseSelfContained</c> defaults to <c>true</c>, so a typo
        /// would publish a self-contained bundle under whichever name the suffix
        /// condition happened to yield.
        /// </summary>
        [TestMethod]
        public void AnUnknownModeIsRejectedRatherThanPublishedUnderTheWrongName()
        {
            var publish = SelectTarget(LoadApplicationProject(), "PublishReleaseSingleFile");
            var conditions = new List<string>();

            foreach (XmlElement guard in publish.SelectNodes("*[local-name()='Error']"))
            {
                conditions.Add(guard.GetAttribute("Condition"));
            }

            Assert.IsTrue(
                conditions.Exists(condition => condition.Contains("ReleaseRuntimeMode")),
                "No guard rejects an unknown runtime mode: " + string.Join(" | ", conditions));
            Assert.IsTrue(
                conditions.Exists(condition => condition.Contains("Platform")),
                "The platform guard must stay — the runtime identifier is derived from it.");
        }

        /// <summary>
        /// Both variants publish a file called <c>SETUNA.exe</c>. Sharing a staging
        /// directory would let one variant's validation pass on the other's output, and
        /// the copy step would take whichever ran last.
        /// </summary>
        [TestMethod]
        public void EachVariantStagesIntoItsOwnDirectory()
        {
            var staging = SelectProperty(LoadApplicationProject(), "ReleasePublishStagingDirectory")?.InnerText;

            Assert.IsNotNull(staging, "The publish target stages before it validates.");
            StringAssert.Contains(staging, "'$(Platform)'");
            StringAssert.Contains(staging, "'$(ReleaseRuntimeMode)'");
        }

        /// <summary>
        /// The same three properties must NOT sit in a plain property group.
        /// <para>
        /// <c>SelfContained</c> there applies to <c>dotnet build</c> as well, which drags
        /// the entire runtime into <c>bin\</c> and — through the test project's reference —
        /// into the test output beside a second copy of it. Keeping them in the publish
        /// target is what lets a developer build and test without paying for a 190 MB
        /// layout every time.
        /// </para>
        /// </summary>
        [TestMethod]
        public void TheSingleFilePropertiesStayOutOfTheDefaultBuild()
        {
            var project = LoadApplicationProject();

            foreach (var property in new[] { "SelfContained", "PublishSingleFile", "IncludeNativeLibrariesForSelfExtract" })
            {
                Assert.IsNull(
                    SelectProperty(project, property),
                    property + " in a PropertyGroup applies to dotnet build too. Pass it to the nested "
                        + "Publish in PublishReleaseSingleFile instead.");
            }
        }

        [TestMethod]
        public void TheArtifactNameCarriesConfigurationPlatformAndRuntimeMode()
        {
            // All four artifacts land in one directory, so the name is what keeps them
            // apart — and what a release note can refer to.
            var project = LoadApplicationProject();
            var name = SelectProperty(project, "ReleasePublishFile")?.InnerText;

            Assert.IsNotNull(name, "The project must name the release artifact.");
            StringAssert.Contains(name, "$(AssemblyName)_$(Configuration)_$(Platform)$(ReleaseArtifactSuffix).exe");

            // Only the portable variant is suffixed, so the self-contained artifact keeps
            // the unsuffixed name that already-published releases link to.
            var suffixes = SelectProperties(project, "ReleaseArtifactSuffix");
            Assert.AreEqual(
                1,
                suffixes.Count,
                "An unconditional suffix would rename the self-contained artifact and break those links.");
            Assert.AreEqual("_Portable", suffixes[0].InnerText.Trim());
            StringAssert.Contains(suffixes[0].GetAttribute("Condition"), "portable");
        }

        [TestMethod]
        public void NoFodyOrCosturaBuildTaskIsRequired()
        {
            var project = LoadApplicationProject();

            foreach (XmlNode reference in project.SelectNodes(
                "/*[local-name()='Project']/*[local-name()='ItemGroup']/*[local-name()='PackageReference']"))
            {
                var include = reference.Attributes?["Include"]?.Value ?? string.Empty;

                Assert.IsFalse(
                    include.IndexOf("Fody", StringComparison.OrdinalIgnoreCase) >= 0
                        || include.IndexOf("Costura", StringComparison.OrdinalIgnoreCase) >= 0,
                    "SDK single-file publishing replaced Costura/Fody; " + include + " must not come back.");
            }

            foreach (var weaver in new[] { "FodyWeavers.xml", "FodyWeavers.xsd" })
            {
                Assert.IsFalse(
                    File.Exists(Path.Combine(RepositoryPath.FindRoot(), "SETUNA", weaver)),
                    weaver + " is a Fody build asset and must not be in the repository.");
            }
        }

        /// <summary>
        /// A `net8.0-windows10.0.*` target framework implies the Windows SDK projection runtime
        /// pack, and that pack is 23 MB of assemblies this application never calls into — it was
        /// 78% of the _Portable bundle. The suffix buys only CA1416 version analysis, which is
        /// suppressed here anyway; app.manifest is what states the minimum OS version.
        /// </summary>
        [TestMethod]
        public void NeitherProjectTargetsAnOsVersionSuffixedFramework()
        {
            foreach (var project in new[] { "SETUNA\\SETUNA.csproj", "SETUNATests\\SETUNATests.csproj" })
            {
                var document = new XmlDocument();
                document.Load(Path.Combine(RepositoryPath.FindRoot(), project));

                var targetFramework = SelectProperty(document, "TargetFramework")?.InnerText.Trim();

                Assert.AreEqual(
                    "net8.0-windows",
                    targetFramework,
                    project + " must target net8.0-windows without an OS version suffix, or the "
                        + "Windows SDK projection assemblies come back into every artifact.");
            }

            Assert.IsNull(
                SelectProperty(LoadApplicationProject(), "SupportedOSPlatformVersion"),
                "SupportedOSPlatformVersion cannot exceed the TFM's platform version, and the TFM "
                    + "no longer carries one. The minimum OS version lives in app.manifest.");
        }

        /// <summary>
        /// The WPF exclusion has to hook <c>ResolveRuntimePackAssets</c> and touch both item
        /// lists that target fills. Removing only <c>RuntimePackAsset</c> cleans
        /// <c>deps.json</c> while the files still ship; removing only
        /// <c>ReferenceCopyLocalPaths</c> drops the files while <c>deps.json</c> keeps listing
        /// them, and a self-contained host then fails at startup on a missing dependency.
        /// </summary>
        [TestMethod]
        public void TheRuntimePackExclusionRunsAfterResolutionAndTouchesBothItemLists()
        {
            var project = LoadApplicationProject();

            var names = SelectProperty(project, "ExcludedRuntimePackAssemblies")?.InnerText;
            Assert.IsFalse(
                string.IsNullOrWhiteSpace(names),
                "The exclusion list must be declared; without it a self-contained publish carries WPF.");
            StringAssert.StartsWith(names, ";", "The list is probed with Contains(';name;').");
            StringAssert.EndsWith(names, ";", "The list is probed with Contains(';name;').");

            var target = SelectTarget(project, "RemoveExcludedRuntimePackAssets");
            Assert.IsNotNull(target, "The exclusion needs a target.");
            Assert.AreEqual(
                "ResolveRuntimePackAssets",
                target.GetAttribute("AfterTargets"),
                "Running any earlier means the assets are not resolved yet, and any later means "
                    + "the publish list has already been computed.");

            var body = target.InnerXml;
            foreach (var list in new[] { "RuntimePackAsset", "ReferenceCopyLocalPaths" })
            {
                StringAssert.Contains(
                    body,
                    "<" + list + " Remove=",
                    "The exclusion must remove from " + list + " as well; one list alone ships a "
                        + "broken artifact.");
            }
        }

        /// <summary>
        /// Rename a file in a future runtime pack and the list stops matching — silently, with
        /// nothing but a bigger artifact to show for it. That has to fail the build.
        /// </summary>
        [TestMethod]
        public void AStaleExclusionListStopsTheBuildRatherThanTheShrinking()
        {
            var target = SelectTarget(LoadApplicationProject(), "RemoveExcludedRuntimePackAssets");
            var guards = new List<string>();

            foreach (XmlElement guard in target.SelectNodes("*[local-name()='Error']"))
            {
                guards.Add(guard.GetAttribute("Condition"));
            }

            Assert.IsTrue(
                guards.Exists(condition =>
                    condition.Contains("SelfContained") && condition.Contains("_ExcludedRuntimePackAsset")),
                "No guard fails the build when the exclusion list matches nothing during a "
                    + "self-contained publish: " + string.Join(" | ", guards));
            Assert.IsTrue(
                guards.Exists(condition => condition.Contains("@(RuntimePackAsset)")),
                "The guard must yield when no runtime pack was resolved at all, or a "
                    + "framework-dependent publish would fail on a check that does not apply to it.");
        }

        /// <summary>
        /// Only the building platform's libwebp may be embedded: the extractor picks the name by
        /// <c>IntPtr.Size</c>, so the other copy is half a megabyte that can never be loaded.
        /// </summary>
        [TestMethod]
        public void EachLibwebpResourceIsScopedToItsPlatform()
        {
            var project = LoadApplicationProject();
            var found = 0;

            foreach (XmlElement resource in project.SelectNodes(
                "/*[local-name()='Project']/*[local-name()='ItemGroup']/*[local-name()='EmbeddedResource']"))
            {
                var include = resource.GetAttribute("Include");
                if (include.IndexOf("libwebp", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                found++;
                var platform = include.IndexOf("x64", StringComparison.OrdinalIgnoreCase) >= 0 ? "x64" : "x86";
                StringAssert.Contains(
                    resource.GetAttribute("Condition"),
                    "'$(Platform)' == '" + platform + "'",
                    include + " must only be embedded when building " + platform + ".");
            }

            Assert.AreEqual(2, found, "Both libwebp resources must stay declared, one per platform.");
        }

        static XmlDocument LoadApplicationProject()
        {
            var project = new XmlDocument();
            project.Load(Path.Combine(RepositoryPath.FindRoot(), "SETUNA", "SETUNA.csproj"));

            return project;
        }

        /// <summary>
        /// SDK-style projects declare no default xmlns, so an XPath written against the
        /// legacy MSBuild namespace matches nothing and turns every assertion vacuous.
        /// </summary>
        static XmlElement SelectProperty(XmlDocument project, string name)
        {
            return project.SelectSingleNode(
                "/*[local-name()='Project']/*[local-name()='PropertyGroup']/*[local-name()='" + name + "']")
                as XmlElement;
        }

        /// <summary>
        /// Every declaration of a property, in document order. A property that is defined
        /// once unconditionally and then overridden under a condition needs all of them:
        /// <see cref="SelectProperty"/> would only ever show the default.
        /// </summary>
        static List<XmlElement> SelectProperties(XmlDocument project, string name)
        {
            var result = new List<XmlElement>();

            foreach (XmlElement property in project.SelectNodes(
                "/*[local-name()='Project']/*[local-name()='PropertyGroup']/*[local-name()='" + name + "']"))
            {
                result.Add(property);
            }

            return result;
        }

        static XmlElement SelectTarget(XmlDocument project, string name)
        {
            return project.SelectSingleNode(
                "/*[local-name()='Project']/*[local-name()='Target'][@Name='" + name + "']") as XmlElement;
        }
    }
}
