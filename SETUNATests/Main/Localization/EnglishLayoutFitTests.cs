using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SETUNA.Main.Option;

namespace SETUNA.Main.Localization.Tests
{
    /// <summary>
    /// Catches the other half of the translation layout problem:
    /// <see cref="EnglishTextFitTests"/> measures whether a control's text fits the
    /// control, and deliberately skips <c>AutoSize</c> ones because they grow to fit.
    /// Growing is exactly what makes them dangerous here — the dialogs place controls at
    /// absolute coordinates, so an <c>AutoSize</c> label that triples in width does not
    /// clip itself, it runs off the edge of its group box or paints over the control
    /// standing next to it. Both failures are silent: the text is laid out correctly and
    /// then covered.
    /// <para>
    /// Measured on a real form after a real <see cref="Lang.SetLanguage(AppLanguage)"/>
    /// switch, and always as a change from the Chinese baseline. The designer's
    /// coordinates already carry a few pixels of slop (a checkbox whose right edge sits
    /// a hair outside its group box, a label butting against a spin box), and reporting
    /// that inherited slop would bury the regressions this actually exists to find.
    /// </para>
    /// </summary>
    [TestClass]
    public class EnglishLayoutFitTests
    {
        /// <summary>
        /// An <c>AutoSize</c> control's width includes a few pixels of padding after the
        /// last glyph, so an overlap or overflow of that order costs no visible text.
        /// Anything larger eats into the glyphs themselves.
        /// </summary>
        const int SlopPixels = 2;

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
        public void EnglishKeepsEveryControlInsideItsContainer()
        {
            var measured = Measure();

            var regressions = new List<string>();
            foreach (var pair in measured.English.Overflow)
            {
                int before;
                if (!measured.Chinese.Overflow.TryGetValue(pair.Key, out before))
                {
                    continue;
                }

                // Only growth counts. A control already outside its container in Chinese
                // is inherited, not caused by the translation.
                if (pair.Value > Math.Max(before, 0) + SlopPixels)
                {
                    regressions.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}: \"{1}\" reaches {2}px past its container (Chinese \"{3}\": {4}px)",
                        pair.Key,
                        measured.English.Text[pair.Key],
                        pair.Value,
                        measured.Chinese.Text[pair.Key],
                        before));
                }
            }

            Assert.AreEqual(
                0,
                regressions.Count,
                "English text pushes controls out of their group box, where they are clipped. Move the "
                    + "control, anchor it to the right edge, or shorten the translation:" + Environment.NewLine
                    + string.Join(Environment.NewLine, regressions));
        }

        [TestMethod]
        public void EnglishKeepsControlsOffEachOther()
        {
            var measured = Measure();

            var regressions = new List<string>();
            foreach (var pair in measured.English.Overlap)
            {
                int before;
                measured.Chinese.Overlap.TryGetValue(pair.Key, out before);

                // Controls that already share space in Chinese are doing so by design
                // (the tab pages all sit on top of each other, for one).
                if (pair.Value > before + SlopPixels)
                {
                    regressions.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}: {1}px of overlap in English, {2}px in Chinese",
                        pair.Key,
                        pair.Value,
                        before));
                }
            }

            Assert.AreEqual(
                0,
                regressions.Count,
                "English text grows one control over another; whichever is behind loses the covered "
                    + "part of its caption. Move one of them or shorten the translation:" + Environment.NewLine
                    + string.Join(Environment.NewLine, regressions));
        }

        sealed class Layout
        {
            /// <summary>Pixels by which a control reaches past its container's right edge.</summary>
            public readonly Dictionary<string, int> Overflow = new Dictionary<string, int>(StringComparer.Ordinal);

            /// <summary>Shared pixels per pair of siblings, keyed by both their paths.</summary>
            public readonly Dictionary<string, int> Overlap = new Dictionary<string, int>(StringComparer.Ordinal);

            public readonly Dictionary<string, string> Text = new Dictionary<string, string>(StringComparer.Ordinal);
        }

        sealed class Measurement
        {
            public readonly Layout Chinese = new Layout();
            public readonly Layout English = new Layout();
        }

        static Measurement Measure()
        {
            var result = new Measurement();

            foreach (var form in FormsToMeasure())
            {
                using (form)
                {
                    // Constructed in Chinese, then switched while open: the switch is the
                    // path that has to keep the layout intact, and measuring the same
                    // instance twice keeps the two snapshots comparable.
                    Lang.SetLanguage(AppLanguage.ChineseSimplified);
                    ShowAndPump(form);
                    Walk(form, form.GetType().Name, result.Chinese);

                    Lang.SetLanguage(AppLanguage.English);
                    Application.DoEvents();
                    Walk(form, form.GetType().Name, result.English);

                    Lang.SetLanguage(AppLanguage.ChineseSimplified);
                }
            }

            return result;
        }

        static void Walk(Control container, string path, Layout into)
        {
            var children = new List<Control>();
            foreach (Control child in container.Controls)
            {
                children.Add(child);
            }

            for (var i = 0; i < children.Count; i++)
            {
                var child = children[i];
                var childPath = path + "/" + child.Name;

                into.Text[childPath] = child.Text ?? string.Empty;
                into.Overflow[childPath] = child.Right - container.ClientSize.Width;

                for (var j = i + 1; j < children.Count; j++)
                {
                    var other = children[j];
                    var shared = Rectangle.Intersect(child.Bounds, other.Bounds);
                    if (!shared.IsEmpty)
                    {
                        // The smaller dimension is what a caption actually loses: two
                        // controls side by side overlap over their full height, and it is
                        // the horizontal bite that hides text.
                        into.Overlap[childPath + " / " + other.Name] = Math.Min(shared.Width, shared.Height);
                    }
                }

                Walk(child, childPath, into);
            }
        }

        /// <summary>
        /// Every window the suite can build, shared with
        /// <see cref="EnglishTextFitTests.ConstructibleForms"/> so the two measurements
        /// cover the same set: the options dialog, the small standalone dialogs, and one
        /// settings panel per style item.
        /// </summary>
        static IEnumerable<Form> FormsToMeasure()
        {
            return EnglishTextFitTests.ConstructibleForms();
        }

        /// <summary>
        /// Drives the form far enough for <c>OnLoad</c> and the first layout pass to have
        /// run, off the side of every monitor so no window appears for the length of the
        /// suite.
        /// <para>
        /// Deliberately not minimized, which is how the rest of the suite hides its
        /// windows: a minimized form reports an empty client area, and docked panels
        /// inside it collapse to nothing, so the containers this test measures against
        /// would all be zero-sized.
        /// </para>
        /// </summary>
        static void ShowAndPump(Form form)
        {
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-30000, -30000);
            form.ShowInTaskbar = false;
            form.Show();
            Application.DoEvents();
            form.Hide();
        }
    }
}
