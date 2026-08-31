using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SETUNA.Main.Tests;

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
    /// switch, and always as a change from the Chinese baseline. The measurement itself
    /// lives in <see cref="LayoutSnapshot"/>, shared with the DPI-relayout tests: a
    /// longer caption and a larger font push controls out of their containers the same
    /// way.
    /// </para>
    /// </summary>
    [TestClass]
    public class EnglishLayoutFitTests
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
        public void EnglishKeepsEveryControlInsideItsContainer()
        {
            var measured = Measure();

            var regressions = new List<string>();
            foreach (var grown in measured.English.OverflowGrowth(measured.Chinese))
            {
                regressions.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}: \"{1}\" reaches {2}px past its container (Chinese \"{3}\": {4}px)",
                    grown.Path,
                    measured.English.Text[grown.Path],
                    grown.After,
                    measured.Chinese.Text[grown.Path],
                    grown.Before));
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
            foreach (var grown in measured.English.OverlapGrowth(measured.Chinese))
            {
                regressions.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}: {1}px of overlap in English, {2}px in Chinese",
                    grown.Path,
                    grown.After,
                    grown.Before));
            }

            Assert.AreEqual(
                0,
                regressions.Count,
                "English text grows one control over another; whichever is behind loses the covered "
                    + "part of its caption. Move one of them or shorten the translation:" + Environment.NewLine
                    + string.Join(Environment.NewLine, regressions));
        }

        sealed class Measurement
        {
            public readonly LayoutSnapshot Chinese = new LayoutSnapshot();
            public readonly LayoutSnapshot English = new LayoutSnapshot();
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
                    LayoutSnapshot.ShowOffScreen(form);
                    result.Chinese.Capture(form, form.GetType().Name);

                    Lang.SetLanguage(AppLanguage.English);
                    Application.DoEvents();
                    result.English.Capture(form, form.GetType().Name);

                    Lang.SetLanguage(AppLanguage.ChineseSimplified);
                }
            }

            return result;
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
    }
}
