using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SETUNA.Main.StyleItems;

namespace SETUNA.Main.Localization.Tests
{
    /// <summary>
    /// Measures whether the English text fits the controls that were sized for
    /// Chinese. A Chinese button label is usually 2-4 glyphs; the English equivalent
    /// is often 6-15 characters, so clipping is the expected failure mode of this
    /// change rather than an unlikely one.
    /// <para>
    /// Only fixed-size controls are checked. An <c>AutoSize</c> control grows to fit,
    /// so its text is never clipped — it can only push a neighbour, which is a layout
    /// judgement a test should not try to make.
    /// </para>
    /// </summary>
    [TestClass]
    public class EnglishTextFitTests
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
        public void EnglishTextDoesNotClipWhereChineseFits()
        {
            // Compared against the Chinese baseline rather than measured in isolation:
            // a few controls (the "..." browse buttons) are already tight in Chinese,
            // and reporting those would bury the regressions this change can actually
            // cause. What matters is text that fits before and clips after.
            var chinese = MeasureAll(AppLanguage.ChineseSimplified);
            var english = MeasureAll(AppLanguage.English);

            var regressions = new List<string>();
            foreach (var pair in english)
            {
                if (!chinese.TryGetValue(pair.Key, out var before))
                {
                    continue;
                }

                if (before.Fits && !pair.Value.Fits)
                {
                    regressions.Add(string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "{0}: \"{1}\" needs {2:F0}px in {3}px (Chinese \"{4}\" needed {5:F0}px)",
                        pair.Key,
                        pair.Value.Text,
                        pair.Value.Needed,
                        pair.Value.Available,
                        before.Text,
                        before.Needed));
                }
            }

            Assert.AreEqual(
                0,
                regressions.Count,
                "English translations clip where Chinese fit. Widen the control, enable "
                    + "AutoSize/AutoEllipsis, or shorten the translation:" + Environment.NewLine
                    + string.Join(Environment.NewLine, regressions));
        }

        [TestMethod]
        public void TheBaselineItselfIsMostlyClean()
        {
            // Guards the comparison above from becoming vacuous: if the Chinese
            // baseline were full of clipped controls, "fits before" would rarely hold
            // and the regression check would stop catching anything.
            var chinese = MeasureAll(AppLanguage.ChineseSimplified);
            var clipped = chinese.Values.Count(m => !m.Fits);

            Assert.IsTrue(
                clipped <= 5,
                "The Chinese baseline has " + clipped + " clipped controls; the regression comparison "
                    + "is only meaningful while this stays small.");
        }

        static Dictionary<string, Measurement> MeasureAll(AppLanguage language)
        {
            Lang.SetLanguage(language);

            var result = new Dictionary<string, Measurement>(StringComparer.Ordinal);
            foreach (var form in ConstructibleForms())
            {
                using (form)
                {
                    LocalizationApplier.Apply(form);
                    Measure(form, form.GetType().Name, result);
                }
            }

            return result;
        }

        sealed class Measurement
        {
            public string Text;
            public float Needed;
            public int Available;
            public bool Fits => Needed <= Available;
        }

        static void Measure(Control control, string path, Dictionary<string, Measurement> into)
        {
            foreach (Control child in control.Controls)
            {
                var childPath = path + "/" + child.Name;

                if (ShouldMeasure(child))
                {
                    using (var graphics = child.CreateGraphics())
                    {
                        var available = child.Width - ChromeWidth(child);

                        if (child is Label && child.Height >= child.Font.Height * 2)
                        {
                            // A label tall enough for more than one line wraps, so the
                            // question is whether the wrapped block fits vertically —
                            // measuring single-line width would flag every multi-line
                            // caption as clipped.
                            var wrapped = graphics.MeasureString(child.Text, child.Font, available);
                            into[childPath] = new Measurement
                            {
                                Text = child.Text,
                                Needed = wrapped.Height,
                                Available = child.Height,
                            };
                        }
                        else
                        {
                            into[childPath] = new Measurement
                            {
                                Text = child.Text,
                                Needed = graphics.MeasureString(child.Text, child.Font).Width,
                                Available = available,
                            };
                        }
                    }
                }

                Measure(child, childPath, into);
            }
        }

        static bool ShouldMeasure(Control control)
        {
            if (string.IsNullOrEmpty(control.Text) || !control.Visible && control.Parent == null)
            {
                return false;
            }

            // AutoSize controls grow to fit their text, so they cannot clip.
            if (control is Label label)
            {
                return !label.AutoSize;
            }

            if (control is CheckBox check)
            {
                return !check.AutoSize;
            }

            if (control is RadioButton radio)
            {
                return !radio.AutoSize;
            }

            if (control is Button button)
            {
                return !button.AutoSize;
            }

            // GroupBox text is drawn in the frame and is clipped silently.
            return control is GroupBox;
        }

        static int ChromeWidth(Control control)
        {
            if (control is CheckBox || control is RadioButton)
            {
                return 20;      // indicator glyph plus its gap
            }

            if (control is Button)
            {
                return 8;       // border and internal padding
            }

            if (control is GroupBox)
            {
                return 12;      // the frame corner the caption starts after
            }

            return 2;
        }

        /// <summary>
        /// Every form the suite can build without application state, shared with
        /// <see cref="EnglishLayoutFitTests"/>. The options dialog comes first because it
        /// holds most of the app's text.
        /// </summary>
        internal static IEnumerable<Form> ConstructibleForms()
        {
            yield return new SETUNA.Main.Option.OptionForm(SETUNA.Main.Option.SetunaOption.GetDefaultOption());
            yield return new ToolBoxForm();
            yield return new LayerRenameWindow();
            yield return new LoginInput();
            yield return new SETUNA.Main.HotkeyMsg();

            foreach (var type in typeof(Lang).Assembly.GetTypes()
                .Where(t => !t.IsAbstract && typeof(CStyleItem).IsAssignableFrom(t))
                .OrderBy(t => t.Name, StringComparer.Ordinal))
            {
                if (type.GetConstructor(Type.EmptyTypes) == null)
                {
                    continue;
                }

                CStyleItem item;
                ToolBoxForm panel = null;
                try
                {
                    item = (CStyleItem)Activator.CreateInstance(type);
                    var method = typeof(CStyleItem).GetMethod(
                        "GetToolBoxForm",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    panel = (ToolBoxForm)method.Invoke(item, null);
                }
                catch (Exception)
                {
                    // Needs more context than this test can supply; the sweep test
                    // reports the same set, so nothing is silently skipped here.
                }

                if (panel != null)
                {
                    yield return panel;
                }
            }
        }
    }
}
