using System;
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
    /// both architectures and then runs the executable's own <c>--self-test</c>.
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
        public void ThePublishTargetAsksForASelfContainedSingleFile()
        {
            var publish = SelectTarget(LoadApplicationProject(), "PublishReleaseSingleFile").InnerXml;

            foreach (var property in new[]
            {
                "SelfContained=true",
                "PublishSingleFile=true",
                "IncludeNativeLibrariesForSelfExtract=true"
            })
            {
                StringAssert.Contains(publish, property, "The nested Publish must pass " + property + ".");
            }
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
        public void TheArtifactNameCarriesConfigurationAndPlatform()
        {
            // Both architectures land in one directory, so the name is what keeps them
            // apart — and what a release note can refer to.
            var name = SelectProperty(LoadApplicationProject(), "ReleasePublishFile")?.InnerText;

            Assert.IsNotNull(name, "The project must name the release artifact.");
            StringAssert.Contains(name, "$(AssemblyName)_$(Configuration)_$(Platform).exe");
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

        static XmlElement SelectTarget(XmlDocument project, string name)
        {
            return project.SelectSingleNode(
                "/*[local-name()='Project']/*[local-name()='Target'][@Name='" + name + "']") as XmlElement;
        }
    }
}
