using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SETUNA.Main.Runtime.Tests
{
    /// <summary>
    /// Guards the runtime pack pruning in <c>SETUNA.csproj</c> from both directions.
    /// <para>
    /// A self-contained publish drops the WPF half of <c>Microsoft.WindowsDesktop.App</c>,
    /// which nothing here loads. That removal is a hand-maintained list, so it can go wrong
    /// two ways: something kept could start referencing something removed (the artifact then
    /// throws <c>FileNotFoundException</c> at whatever moment the user reaches that code), or
    /// a package could be declared and never used (the artifact then carries it forever).
    /// Both are checked here against the same data — the assembly reference closure of
    /// <c>SETUNA.dll</c>.
    /// </para>
    /// <para>
    /// The closure resolves against <see cref="AppContext.BaseDirectory"/> because the test
    /// project publishes self-contained: its output directory already is a complete
    /// WindowsDesktop layout plus every package assembly, so no publish step is needed to
    /// have something to walk.
    /// </para>
    /// </summary>
    [TestClass]
    public class RuntimePackPruningTests
    {
        const string ApplicationAssembly = "SETUNA";

        /// <summary>
        /// The list is only safe while nothing reachable from the application refers to it.
        /// Checked mechanically rather than by eye: this is the assertion that turns "we
        /// reviewed the list once" into "the list is still true".
        /// </summary>
        [TestMethod]
        public void TheExclusionListIsDisjointFromTheReferenceClosure()
        {
            var excluded = ReadExcludedAssemblies();
            Assert.IsTrue(
                excluded.Count > 0,
                "SETUNA.csproj must declare ExcludedRuntimePackAssemblies; without it the "
                    + "self-contained artifact silently regains the WPF payload.");

            var closure = ComputeReferenceClosure();

            var collisions = excluded
                .Where(name => closure.Reached.ContainsKey(name))
                .Select(name => name + " (referenced by " + closure.Reached[name] + ")")
                .OrderBy(text => text, StringComparer.Ordinal)
                .ToArray();

            Assert.AreEqual(
                0,
                collisions.Length,
                "Excluded assemblies are reachable from " + ApplicationAssembly + ".dll, so the "
                    + "published artifact would fail to load them at runtime. Either the code must "
                    + "stop using them or they must leave ExcludedRuntimePackAssemblies: "
                    + string.Join(", ", collisions));
        }

        /// <summary>
        /// The mirror image: a package reference whose assembly never shows up in the closure
        /// is payload nobody asked for. <c>System.Configuration.ConfigurationManager</c> was
        /// exactly that — declared for an application-settings file that contained no settings,
        /// and dragging <c>System.Diagnostics.EventLog</c> along with it.
        /// </summary>
        [TestMethod]
        public void EveryPackageReferenceAppearsInTheReferenceClosure()
        {
            var closure = ComputeReferenceClosure();
            var annotated = ReadDeclaredExceptions();

            var unused = new List<string>();
            foreach (var package in ReadPackageReferences())
            {
                if (annotated.Contains(package) || IsInClosure(package, closure))
                {
                    continue;
                }

                unused.Add(package);
            }

            Assert.AreEqual(
                0,
                unused.Count,
                "Package reference(s) in SETUNA.csproj that no code reaches: "
                    + string.Join(", ", unused.OrderBy(name => name, StringComparer.Ordinal))
                    + ". Remove them, or — if the package is only ever loaded by reflection — add "
                    + "it to UnreferencedPackageReferences in SETUNA.csproj with the reason "
                    + "written next to it.");
        }

        /// <summary>
        /// The disjointness claim is only worth anything if the walk is transitive — a
        /// one-level walk would pass while missing exactly the indirect reference that breaks
        /// a pruned artifact. <c>ExCSS</c> is the anchor: nothing here names it, it is reachable
        /// only as <c>Svg</c>'s own dependency.
        /// </summary>
        [TestMethod]
        public void TheReferenceClosureFollowsIndirectReferences()
        {
            var closure = ComputeReferenceClosure();

            Assert.IsTrue(
                closure.Reached.ContainsKey("ExCSS"),
                "ExCSS is reachable only through Svg, so its absence means the walk stopped at "
                    + "direct references and the exclusion check proves nothing.");
            Assert.AreEqual(
                "Svg",
                closure.Reached["ExCSS"],
                "ExCSS must have been reached through Svg.");
        }

        /// <summary>
        /// A package id and its assembly name usually match, but not always:
        /// <c>Prowl.Aperture</c> ships <c>Aperture.dll</c>. Both spellings count.
        /// </summary>
        static bool IsInClosure(string packageId, ReferenceClosure closure)
        {
            if (closure.Reached.ContainsKey(packageId))
            {
                return true;
            }

            var lastSegment = packageId.Substring(packageId.LastIndexOf('.') + 1);
            return closure.Reached.ContainsKey(lastSegment);
        }

        /// <summary>
        /// Every assembly reachable from the application, mapped to the assembly that first
        /// referenced it — the referrer is what makes a failure actionable.
        /// </summary>
        sealed class ReferenceClosure
        {
            public Dictionary<string, string> Reached { get; } =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            public SortedSet<string> Unresolved { get; } =
                new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        static ReferenceClosure ComputeReferenceClosure()
        {
            var closure = new ReferenceClosure();
            var pending = new Queue<string>();

            closure.Reached[ApplicationAssembly] = "(root)";
            pending.Enqueue(ApplicationAssembly);

            while (pending.Count > 0)
            {
                var current = pending.Dequeue();
                var path = Path.Combine(AppContext.BaseDirectory, current + ".dll");

                if (!File.Exists(path))
                {
                    closure.Unresolved.Add(current);
                    continue;
                }

                foreach (var reference in ReadAssemblyReferences(path))
                {
                    if (closure.Reached.ContainsKey(reference))
                    {
                        continue;
                    }

                    closure.Reached[reference] = current;
                    pending.Enqueue(reference);
                }
            }

            Assert.IsTrue(
                closure.Reached.Count > 1,
                ApplicationAssembly + ".dll must sit beside the test assembly and reference "
                    + "something; found nothing, so the walk proved nothing.");

            // An unresolved name is a hole in the walk: whatever that assembly references was
            // never inspected, so "disjoint from the exclusion list" would be an untested claim.
            // The test project publishes self-contained, so everything reachable is on disk.
            Assert.AreEqual(
                0,
                closure.Unresolved.Count,
                "Reference closure could not be completed; these assemblies are missing from the "
                    + "test output: " + string.Join(", ", closure.Unresolved));

            return closure;
        }

        static IEnumerable<string> ReadAssemblyReferences(string assemblyPath)
        {
            var names = new List<string>();

            using (var stream = File.OpenRead(assemblyPath))
            using (var peReader = new PEReader(stream))
            {
                // A native library that happens to share a managed assembly's name has no
                // metadata; it contributes no references rather than throwing.
                if (!peReader.HasMetadata)
                {
                    return names;
                }

                var reader = peReader.GetMetadataReader();
                foreach (var handle in reader.AssemblyReferences)
                {
                    names.Add(reader.GetString(reader.GetAssemblyReference(handle).Name));
                }
            }

            return names;
        }

        static List<string> ReadExcludedAssemblies()
        {
            var property = SelectProperty("ExcludedRuntimePackAssemblies");

            return (property ?? string.Empty)
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(name => name.Trim())
                .Where(name => name.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        static HashSet<string> ReadDeclaredExceptions()
        {
            var property = SelectProperty("UnreferencedPackageReferences");

            return new HashSet<string>(
                (property ?? string.Empty)
                    .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(name => name.Trim())
                    .Where(name => name.Length > 0),
                StringComparer.OrdinalIgnoreCase);
        }

        static List<string> ReadPackageReferences()
        {
            var packages = new List<string>();

            foreach (XmlNode reference in LoadApplicationProject().SelectNodes(
                "/*[local-name()='Project']/*[local-name()='ItemGroup']/*[local-name()='PackageReference']"))
            {
                var include = reference.Attributes?["Include"]?.Value;
                if (!string.IsNullOrWhiteSpace(include))
                {
                    packages.Add(include.Trim());
                }
            }

            Assert.IsTrue(packages.Count > 0, "The project must still declare package references.");
            return packages;
        }

        static string SelectProperty(string name)
        {
            var node = LoadApplicationProject().SelectSingleNode(
                "/*[local-name()='Project']/*[local-name()='PropertyGroup']/*[local-name()='" + name + "']");

            return node?.InnerText;
        }

        static XmlDocument LoadApplicationProject()
        {
            var project = new XmlDocument();
            project.Load(Path.Combine(RepositoryPath.FindRoot(), "SETUNA", "SETUNA.csproj"));
            return project;
        }
    }
}
