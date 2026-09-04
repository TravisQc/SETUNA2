using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SETUNA.Main.Runtime.Tests;
using SETUNA.Main.Tests;
using SETUNA.Main.Window;

namespace SETUNATests.Main.Window
{
    /// <summary>
    /// Pins the designer half of the DPI pipeline: a logical dialog scales correctly only
    /// if it asks for <see cref="AutoScaleMode.Dpi"/> *and* declares its baseline in DPI.
    /// <para>
    /// The two failures need separate checks. A form left on
    /// <see cref="AutoScaleMode.Font"/> is visible at runtime, so it is asserted on real
    /// instances. A form on <see cref="AutoScaleMode.Dpi"/> with a *font* baseline such as
    /// <c>(6F, 12F)</c> is not: WinForms scales the tree by 96/6 and then overwrites
    /// <c>AutoScaleDimensions</c> with the value it just used, so the instance no longer
    /// remembers what the designer said. That one is asserted against the designer source,
    /// which is also where it comes back from — Visual Studio rewrites the baseline
    /// whenever the form is opened with a different ambient font.
    /// </para>
    /// </summary>
    [TestClass]
    public class DesignerDpiBaselineTests
    {
        const string LogicalBaseline = "96F, 96F";

        [TestMethod]
        public void EveryConstructibleLogicalUiFormUsesTheDpiPipeline()
        {
            var offenders = new List<string>();

            foreach (var form in ApplicationForms.All())
            {
                using (form)
                {
                    if (form is BaseForm window && window.Policy != DpiPolicy.LogicalUi)
                    {
                        continue;
                    }

                    if (form.AutoScaleMode != AutoScaleMode.Dpi)
                    {
                        offenders.Add(form.GetType().Name + " is on AutoScaleMode." + form.AutoScaleMode);
                    }
                }
            }

            Assert.AreEqual(
                0,
                offenders.Count,
                "A logical dialog on any mode but Dpi scales by its ambient font instead of the monitor:"
                    + Environment.NewLine + string.Join(Environment.NewLine, offenders));
        }

        [TestMethod]
        public void EveryLogicalUiDesignerDeclaresItsBaselineInDpi()
        {
            var offenders = new List<string>();

            foreach (var designer in LogicalUiDesignerFiles())
            {
                var source = File.ReadAllText(designer);

                foreach (Match declared in Regex.Matches(
                    source, @"AutoScaleDimensions\s*=\s*new System\.Drawing\.SizeF\((?<dims>[^)]*)\)"))
                {
                    var dimensions = declared.Groups["dims"].Value.Trim();
                    if (dimensions != LogicalBaseline)
                    {
                        offenders.Add(Path.GetFileName(designer) + " declares SizeF(" + dimensions + ")");
                    }
                }

                foreach (Match declared in Regex.Matches(
                    source, @"this\.AutoScaleMode\s*=\s*System\.Windows\.Forms\.AutoScaleMode\.(?<mode>\w+)"))
                {
                    var mode = declared.Groups["mode"].Value;
                    if (mode != nameof(AutoScaleMode.Dpi))
                    {
                        offenders.Add(Path.GetFileName(designer) + " declares AutoScaleMode." + mode);
                    }
                }
            }

            Assert.AreEqual(
                0,
                offenders.Count,
                "A font baseline under AutoScaleMode.Dpi scales the control tree by 96/6, and the instance "
                    + "cannot report it back afterwards. Set the designer baseline to " + LogicalBaseline + ":"
                    + Environment.NewLine + string.Join(Environment.NewLine, offenders));
        }

        /// <summary>
        /// No designer may hide the control box while it also gives the form a caption.
        /// <para>
        /// A window carries <c>WS_CAPTION</c> when its text is non-empty *or* its control box
        /// is shown. Designer files assign properties alphabetically, so <c>ControlBox</c> and
        /// <c>FormBorderStyle</c> both land before <c>Text</c> — and <c>FormBorderStyle</c>
        /// recomputes <see cref="Form.Size"/> from <c>ClientSize</c> at a moment when the form
        /// is captionless, giving a frame of one pixel per edge. Assigning <c>Text</c>
        /// afterwards brings the caption back without recomputing anything, so the caption and
        /// the real border are taken out of the client area.
        /// </para>
        /// <para>
        /// Autoscaling then multiplies the error, because <c>Form.ScaleSize</c> scales
        /// <c>Size</c> minus the *current* frame: measured at 168 DPI on the paint palettes
        /// that used to live here, one was born 808x0 and another 382x42 instead of 420x150,
        /// with its buttons below the bottom edge at every scale factor.
        /// <c>BaseForm.HideControlBoxAfterInitialize</c> is the fix and explains why.
        /// </para>
        /// <para>
        /// This has to be a source check. Measured in this host, with and without a created
        /// handle, a real instance reports the designer's client area however the properties
        /// were ordered: the autoscale factor is 1, so nothing recomputes the size, and the
        /// form re-applies the client size it was asked for when its window is created. The
        /// consequence is only visible where the factor is not 1, which needs the manifest
        /// that <c>probes/DialogRelayoutProbe</c> links.
        /// </para>
        /// </summary>
        [TestMethod]
        public void NoDesignerHidesTheControlBoxWhileGivingTheFormACaption()
        {
            var offenders = new List<string>();
            var designers = Directory.GetFiles(
                Path.Combine(RepositoryPath.FindRoot(), "SETUNA"),
                "*.Designer.cs",
                SearchOption.AllDirectories);

            foreach (var designer in designers)
            {
                var source = File.ReadAllText(designer);

                // Only a Form has a border style, which keeps user controls out of this.
                var border = Regex.Match(
                    source,
                    @"this\.FormBorderStyle\s*=\s*System\.Windows\.Forms\.FormBorderStyle\.(?<style>\w+)");

                if (!border.Success || border.Groups["style"].Value == nameof(FormBorderStyle.None))
                {
                    continue;
                }

                if (Regex.IsMatch(source, @"this\.ControlBox\s*=\s*false")
                    && Regex.IsMatch(source, @"this\.Text\s*=\s*""[^""]+"""))
                {
                    offenders.Add(Path.GetFileName(designer) + " (" + border.Groups["style"].Value + ")");
                }
            }

            Assert.AreEqual(
                0,
                offenders.Count,
                "These designers assign ControlBox = false before Text, so the caption and border are "
                    + "taken out of the client area. Drop the assignment and call "
                    + "BaseForm.HideControlBoxAfterInitialize() after InitializeComponent instead:"
                    + Environment.NewLine + string.Join(Environment.NewLine, offenders));
        }

        /// <summary>
        /// Nowhere in the application may an autoscale baseline be anything but 96 DPI.
        /// <para>
        /// The logical-designer check above cannot see all of them: it maps
        /// <c>BaseForm</c> subclasses to <c>*.Designer.cs</c>, which misses user controls and
        /// the ones whose <c>InitializeComponent</c> sits inline in the <c>.cs</c> file — that
        /// is where the paint layer rows that used to live here kept a
        /// <c>(6F, 12F)</c> font baseline. Under <see cref="AutoScaleMode.Dpi"/> that baseline
        /// is read as a DPI and the tree is scaled by 96/6; under
        /// <see cref="AutoScaleMode.Font"/> the control scales itself by the ambient font,
        /// which was already 1.17x at 96 DPI. Neither is ever wanted here, and a physical
        /// surface does not declare a baseline at all, so one rule covers the repository.
        /// </para>
        /// </summary>
        [TestMethod]
        public void NoSourceFileDeclaresABaselineOtherThan96Dpi()
        {
            var offenders = new List<string>();
            var seen = 0;
            var sources = Directory.GetFiles(
                Path.Combine(RepositoryPath.FindRoot(), "SETUNA"),
                "*.cs",
                SearchOption.AllDirectories);

            foreach (var source in sources)
            {
                if (IsBuildOutput(source))
                {
                    continue;
                }

                foreach (Match declared in Regex.Matches(
                    File.ReadAllText(source),
                    @"AutoScaleDimensions\s*=\s*new System\.Drawing\.SizeF\((?<dims>[^)]*)\)"))
                {
                    seen++;

                    var dimensions = declared.Groups["dims"].Value.Trim();
                    if (dimensions != LogicalBaseline)
                    {
                        offenders.Add(Path.GetFileName(source) + " declares SizeF(" + dimensions + ")");
                    }
                }
            }

            Assert.IsTrue(seen > 0, "No autoscale baseline was found at all, so this proves nothing.");
            Assert.AreEqual(
                0,
                offenders.Count,
                "The only autoscale baseline this project uses is " + LogicalBaseline + ":"
                    + Environment.NewLine + string.Join(Environment.NewLine, offenders));
        }

        /// <summary>
        /// A physical surface has to say so in its designer, not only in the registry.
        /// <para>
        /// <c>BaseForm</c>'s constructor applies the registry's answer before
        /// <c>InitializeComponent</c> runs, so a designer that then assigns
        /// <see cref="AutoScaleMode.Font"/> or <see cref="AutoScaleMode.Dpi"/> silently wins
        /// and the surface starts scaling its own pixel coordinates. Declaring
        /// <see cref="AutoScaleMode.None"/> makes the two agree where a reader will look;
        /// declaring nothing is also accepted, since the constructor already set it.
        /// </para>
        /// </summary>
        [TestMethod]
        public void NoPhysicalSurfaceDesignerAsksForAutoscaling()
        {
            var offenders = new List<string>();

            foreach (var designer in DesignerFilesFor(DpiPolicy.PhysicalSurface))
            {
                foreach (Match declared in Regex.Matches(
                    File.ReadAllText(designer),
                    @"this\.AutoScaleMode\s*=\s*System\.Windows\.Forms\.AutoScaleMode\.(?<mode>\w+)"))
                {
                    var mode = declared.Groups["mode"].Value;
                    if (mode != nameof(AutoScaleMode.None))
                    {
                        offenders.Add(Path.GetFileName(designer) + " declares AutoScaleMode." + mode);
                    }
                }
            }

            Assert.AreEqual(
                0,
                offenders.Count,
                "A physical surface whose designer asks for autoscaling has its bitmap and canvas "
                    + "coordinates scaled behind the registry's back:"
                    + Environment.NewLine + string.Join(Environment.NewLine, offenders));
        }

        static bool IsBuildOutput(string path)
        {
            return path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)
                || path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar);
        }

        /// <summary>
        /// The designer file of every form the registry classifies as logical UI. Forms
        /// without a designer file (none today) and physical surfaces are skipped; the
        /// latter declare <see cref="AutoScaleMode.None"/> on purpose.
        /// </summary>
        static IEnumerable<string> LogicalUiDesignerFiles()
        {
            return DesignerFilesFor(DpiPolicy.LogicalUi);
        }

        static IEnumerable<string> DesignerFilesFor(DpiPolicy wanted)
        {
            var applicationRoot = Path.Combine(RepositoryPath.FindRoot(), "SETUNA");
            var designers = Directory
                .GetFiles(applicationRoot, "*.Designer.cs", SearchOption.AllDirectories)
                .Where(path => !IsBuildOutput(path))
                .ToDictionary(Path.GetFileName, StringComparer.Ordinal);

            var classified = typeof(BaseForm).Assembly
                .GetTypes()
                .Where(type => type != typeof(BaseForm)
                    && typeof(BaseForm).IsAssignableFrom(type)
                    && !type.IsAbstract
                    && DpiPolicyRegistry.TryGetPolicy(type, out var policy)
                    && policy == wanted)
                .OrderBy(type => type.Name, StringComparer.Ordinal)
                .ToArray();

            Assert.IsTrue(classified.Length > 0, "No " + wanted + " forms were discovered.");

            var found = new List<string>();
            foreach (var type in classified)
            {
                if (designers.TryGetValue(type.Name + ".Designer.cs", out var path))
                {
                    found.Add(path);
                }
            }

            return found;
        }
    }
}
