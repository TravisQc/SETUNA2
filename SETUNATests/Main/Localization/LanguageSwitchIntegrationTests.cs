using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SETUNA.Main.Option;
using SETUNA.Main.StyleItems;

namespace SETUNA.Main.Localization.Tests
{
    /// <summary>
    /// Covers the two properties a walkthrough is normally relied on for: that a
    /// language switch reaches windows that are <em>already open</em>, and that it
    /// disturbs nothing else while doing so.
    /// <para>
    /// The switch is triggered the way the application triggers it —
    /// <see cref="Lang.SetLanguage(AppLanguage)"/>, whose event the forms subscribe to
    /// in their constructor — rather than by calling the applier directly. That is the
    /// part worth testing: calling the applier by hand would pass even if the wiring
    /// were missing entirely.
    /// </para>
    /// </summary>
    [TestClass]
    public class ImmediateLanguageSwitchTests
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
        public void AnOpenFormRetranslatesWithoutBeingReopened()
        {
            Lang.SetLanguage(AppLanguage.ChineseSimplified);

            using (var form = new ToolBoxForm())
            {
                // Force the first application the way OnLoad would, so the test starts
                // from the state a shown window is in.
                ShowAndPump(form);

                var button = FindControl(form, "cmdOK");
                var chinese = button.Text;

                Lang.SetLanguage(AppLanguage.English);

                Assert.AreEqual(
                    "OK",
                    button.Text,
                    "The open form did not pick up the switch; the LanguageChanged wiring is missing.");
                Assert.AreNotEqual(chinese, button.Text);
            }
        }

        [TestMethod]
        public void EveryOpenFormRetranslatesFromASingleSwitch()
        {
            Lang.SetLanguage(AppLanguage.ChineseSimplified);

            var forms = new List<Form>
            {
                new ToolBoxForm(),
                new SETUNA.Main.HotkeyMsg(),
                new LoginInput(),
            };

            try
            {
                foreach (var form in forms)
                {
                    ShowAndPump(form);
                }

                var before = forms.ConvertAll(f => TextSnapshot(f));

                Lang.SetLanguage(AppLanguage.English);

                for (var i = 0; i < forms.Count; i++)
                {
                    var after = TextSnapshot(forms[i]);
                    Assert.AreNotEqual(
                        before[i],
                        after,
                        forms[i].GetType().Name + " did not change; a switch must reach every open window.");
                }
            }
            finally
            {
                forms.ForEach(f => f.Dispose());
            }
        }

        [TestMethod]
        public void ADropdownFilledByCodeRetranslatesAndKeepsItsSelection()
        {
            // The scale panel's interpolation list is built in code, so the applier — which
            // only ever sees designer text — cannot touch it. Rebuilding it is the panel's
            // own job, and rebuilding is where a selection is easy to lose: the items are
            // wrapper objects, so neither the index nor the caption survives on its own.
            Lang.SetLanguage(AppLanguage.ChineseSimplified);

            using (var panel = StylePanelFor("CScaleStyleItem"))
            {
                ShowAndPump(panel);

                var combo = (ComboBox)FindControl(panel, "cmbInterpolation");
                Assert.IsTrue(combo.Items.Count > 2, "Expected the interpolation list to be filled.");

                combo.SelectedIndex = 2;
                var chinese = combo.Text;

                Lang.SetLanguage(AppLanguage.English);

                Assert.AreEqual(2, combo.SelectedIndex, "The switch must not move the selection.");
                Assert.AreNotEqual(chinese, combo.Text, "The selected item is still showing Chinese.");
                foreach (var item in combo.Items)
                {
                    Assert.IsFalse(
                        System.Text.RegularExpressions.Regex.IsMatch(item.ToString(), @"[一-鿿]"),
                        "Item \"" + item + "\" was not rebuilt in English.");
                }
            }
        }

        /// <summary>
        /// The settings panel a style item hands out, obtained the way the application
        /// obtains it. Both the item types and the panels are internal, so this goes
        /// through reflection rather than a constructor.
        /// </summary>
        static Form StylePanelFor(string styleItemTypeName)
        {
            var type = typeof(Lang).Assembly.GetType("SETUNA.Main.StyleItems." + styleItemTypeName, throwOnError: true);
            var item = Activator.CreateInstance(type);
            var method = typeof(SETUNA.Main.StyleItems.CStyleItem).GetMethod(
                "GetToolBoxForm",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(method, "GetToolBoxForm not found; was it renamed?");

            return (Form)method.Invoke(item, null);
        }

        [TestMethod]
        public void SwitchingLanguageLeavesAScrapImageAndPositionAlone()
        {
            // The scrap windows hold the user's captured pixels. A refresh that
            // reloaded or re-laid-out their content would be a data-visible bug, not a
            // cosmetic one.
            Lang.SetLanguage(AppLanguage.ChineseSimplified);

            using (var scrap = new ScrapBase())
            using (var source = new Bitmap(37, 23))
            {
                source.SetPixel(5, 6, Color.Crimson);
                scrap.Image = source;
                scrap.Location = new Point(311, 207);

                var sizeBefore = scrap.Size;
                var locationBefore = scrap.Location;
                var imageBefore = scrap.Image;

                Lang.SetLanguage(AppLanguage.English);

                Assert.AreSame(imageBefore, scrap.Image, "The scrap's bitmap must not be replaced.");
                Assert.AreEqual(locationBefore, scrap.Location);
                Assert.AreEqual(sizeBefore, scrap.Size);
                Assert.AreEqual(Color.Crimson.ToArgb(), ((Bitmap)scrap.Image).GetPixel(5, 6).ToArgb());
            }
        }

        [TestMethod]
        public void TheLanguageDropdownMapsToTheLanguageItApplies()
        {
            // The dialog's OK handler cannot be invoked from a test: WriteSetunaOption
            // writes to the user's real HKCU Run key through AutoStartup.Set and then
            // dereferences the Mainform singleton. So the promise is verified in two
            // halves — this test covers "the dropdown selection resolves to the right
            // language", and TheOkHandlerAppliesTheLanguageImmediately covers "OK acts
            // on it rather than deferring to the next launch".
            Lang.SetLanguage(AppLanguage.ChineseSimplified);

            var option = SetunaOption.GetDefaultOption();

            using (var dialog = new OptionForm(option))
            {
                ShowAndPump(dialog);

                var combo = (ComboBox)FindControl(dialog, "cmbLanguage");
                var selected = typeof(OptionForm).GetProperty(
                    "SelectedLanguage",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Assert.IsNotNull(selected, "SelectedLanguage not found; was it renamed?");

                // Item order is the contract between the dropdown and the language list.
                var expected = new[] { AppLanguage.Auto, AppLanguage.ChineseSimplified, AppLanguage.English };
                Assert.AreEqual(expected.Length, combo.Items.Count, "Unexpected number of language choices.");

                for (var i = 0; i < expected.Length; i++)
                {
                    combo.SelectedIndex = i;
                    Assert.AreEqual(
                        expected[i],
                        (AppLanguage)selected.GetValue(dialog),
                        "Dropdown item " + i + " (\"" + combo.Items[i] + "\") maps to the wrong language.");
                }
            }
        }

        [TestMethod]
        public void TheOkHandlerAppliesTheLanguageImmediately()
        {
            // Asserted against the compiled method body rather than by invoking it,
            // for the reason above. Reading IL instead of source text follows what
            // RuntimeConfigurationTests already does for the forbidden DPI P/Invokes:
            // a rename or reformat cannot break this, but deleting the call does.
            var handler = typeof(OptionForm).GetMethod(
                "btnOK_Click",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(handler, "btnOK_Click not found; was the handler renamed?");

            var expected = typeof(Lang).GetMethod("SetLanguage", new[] { typeof(AppLanguage) });
            Assert.IsNotNull(expected);

            Assert.IsTrue(
                CalledMethods(handler).Contains(expected),
                "Confirming the options dialog must apply the language on the spot. Without this call the "
                    + "new language would only appear after a restart.");
        }

        /// <summary>
        /// Every method the given method body calls, by walking <c>call</c>/<c>callvirt</c>
        /// operands. Enough to answer "does A call B" without an IL library.
        /// </summary>
        static HashSet<System.Reflection.MethodBase> CalledMethods(System.Reflection.MethodBase method)
        {
            const byte Call = 0x28;
            const byte CallVirt = 0x6F;

            var result = new HashSet<System.Reflection.MethodBase>();
            var il = method.GetMethodBody().GetILAsByteArray();
            var module = method.Module;

            // Operands are 4-byte metadata tokens. Scanning every position rather than
            // decoding the full instruction stream can only add spurious entries, never
            // hide a real call, so it cannot turn a missing call into a pass.
            for (var i = 0; i + 4 < il.Length; i++)
            {
                if (il[i] != Call && il[i] != CallVirt)
                {
                    continue;
                }

                var token = BitConverter.ToInt32(il, i + 1);
                try
                {
                    var called = module.ResolveMethod(token);
                    if (called != null)
                    {
                        result.Add(called);
                    }
                }
                catch (ArgumentException)
                {
                    // Not a method token; this position was operand data, not an opcode.
                }
            }

            return result;
        }

        /// <summary>
        /// Drives the form far enough for <c>OnLoad</c> to have run, without leaving a
        /// window on screen for the length of the suite.
        /// </summary>
        static void ShowAndPump(Form form)
        {
            form.WindowState = FormWindowState.Minimized;
            form.ShowInTaskbar = false;
            form.Show();
            Application.DoEvents();
            form.Hide();
        }

        static string TextSnapshot(Control root)
        {
            var builder = new System.Text.StringBuilder();
            Append(root, builder);
            return builder.ToString();
        }

        static void Append(Control control, System.Text.StringBuilder into)
        {
            into.Append(control.Name).Append('=').Append(control.Text).Append(';');
            foreach (Control child in control.Controls)
            {
                Append(child, into);
            }
        }

        static Control FindControl(Control root, string name)
        {
            var found = root.Controls.Find(name, true);
            Assert.AreNotEqual(0, found.Length, "No control named " + name);
            return found[0];
        }
    }

    /// <summary>
    /// Exercises the startup path that decides a first run's language: the value a
    /// freshly created configuration carries, handed to <see cref="Lang"/> exactly as
    /// <c>Mainform</c> hands it over after loading the config.
    /// </summary>
    [TestClass]
    public class FirstRunLanguageTests
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
        public void ADeletedConfigurationStartsInTheSystemLanguage()
        {
            // No config file means GetDefaultOption, whose Language is the follow-the-
            // system value. Feeding that through the same overload Mainform calls.
            var freshInstall = SetunaOption.GetDefaultOption();

            Lang.SetLanguage(freshInstall.Setuna.Language);

            Assert.AreEqual(AppLanguage.Auto, Lang.Selected);
            Assert.AreEqual(
                AppLanguages.InferFromCulture(CultureInfo.CurrentUICulture),
                Lang.Effective,
                "A first run must land on the language inferred from the system UI culture.");
        }

        [TestMethod]
        public void AConfigurationFromBeforeThisChangeStartsInTheSystemLanguage()
        {
            // An existing installation's file has no Language element at all, which
            // deserialises to null.
            Lang.SetLanguage((string)null);

            Assert.AreEqual(AppLanguage.Auto, Lang.Selected);
            Assert.AreEqual(AppLanguages.InferFromCulture(CultureInfo.CurrentUICulture), Lang.Effective);
        }

        [TestMethod]
        public void AnExplicitChoiceSurvivesStartupUnchanged()
        {
            foreach (var language in new[] { AppLanguage.English, AppLanguage.ChineseSimplified })
            {
                var stored = SetunaOption.GetDefaultOption();
                stored.Setuna.Language = AppLanguages.ToConfigValue(language);

                Lang.SetLanguage(stored.Setuna.Language);

                Assert.AreEqual(language, Lang.Selected);
                Assert.AreEqual(language, Lang.Effective);
            }
        }

        [TestMethod]
        public void AConfigurationThatFailedToLoadStillStartsInTheSystemLanguage()
        {
            // Mainform's catch block falls back to GetDefaultOption and then runs the
            // same SetLanguage call in its finally, so a corrupt file must not leave
            // the interface in a stale language from a previous switch.
            Lang.SetLanguage(AppLanguage.English);

            var fallback = SetunaOption.GetDefaultOption();
            Lang.SetLanguage(fallback.Setuna.Language);

            Assert.AreEqual(AppLanguage.Auto, Lang.Selected);
            Assert.AreEqual(AppLanguages.InferFromCulture(CultureInfo.CurrentUICulture), Lang.Effective);
        }

        [TestMethod]
        public void AFirstRunOnAnEnglishSystemShowsEnglish()
        {
            // The tests above assert against whatever culture this machine has, so on a
            // Chinese machine they only ever exercise the Chinese branch. Overriding the
            // thread's UI culture drives the same startup path down the other branch,
            // which is otherwise only reachable by reinstalling Windows.
            RunWithUiCulture("en-US", () =>
            {
                Lang.SetLanguage(SetunaOption.GetDefaultOption().Setuna.Language);

                Assert.AreEqual(AppLanguage.Auto, Lang.Selected);
                Assert.AreEqual(AppLanguage.English, Lang.Effective);

                // Asserted on a real control, not just on Lang: this is the whole
                // point of the first-run inference.
                using (var form = new ToolBoxForm())
                {
                    ShowAndPump(form);
                    Assert.AreEqual("OK", FindControl(form, "cmdOK").Text);
                }
            });
        }

        [TestMethod]
        public void AFirstRunOnAChineseSystemShowsChinese()
        {
            RunWithUiCulture("zh-CN", () =>
            {
                Lang.SetLanguage(SetunaOption.GetDefaultOption().Setuna.Language);

                Assert.AreEqual(AppLanguage.Auto, Lang.Selected);
                Assert.AreEqual(AppLanguage.ChineseSimplified, Lang.Effective);

                using (var form = new ToolBoxForm())
                {
                    ShowAndPump(form);
                    Assert.AreEqual(
                        "确定",
                        FindControl(form, "cmdOK").Text,
                        "A Chinese first run must keep the designer's text.");
                }
            });
        }

        [TestMethod]
        public void ADefaultConfigurationRecordsFollowTheSystemRatherThanTheInferredLanguage()
        {
            // What actually lands in SetunaConfig.xml on a first run. Storing the
            // inferred language instead would freeze the choice, so moving the install
            // to a differently-localized machine would no longer follow it.
            RunWithUiCulture("en-US", () =>
            {
                var fresh = SetunaOption.GetDefaultOption();

                Assert.AreEqual(
                    AppLanguages.AutoValue,
                    fresh.Setuna.Language,
                    "A fresh configuration must record follow-the-system, not the language it resolved to.");
            });
        }

        static void RunWithUiCulture(string cultureName, Action body)
        {
            var previous = System.Threading.Thread.CurrentThread.CurrentUICulture;
            System.Threading.Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
            try
            {
                body();
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentUICulture = previous;
            }
        }

        /// <summary>
        /// Drives the form far enough for <c>OnLoad</c> to have run, without leaving a
        /// window on screen for the length of the suite.
        /// </summary>
        static void ShowAndPump(Form form)
        {
            form.WindowState = FormWindowState.Minimized;
            form.ShowInTaskbar = false;
            form.Show();
            Application.DoEvents();
            form.Hide();
        }

        static Control FindControl(Control root, string name)
        {
            var found = root.Controls.Find(name, true);
            Assert.AreNotEqual(0, found.Length, "No control named " + name);
            return found[0];
        }
    }
}
