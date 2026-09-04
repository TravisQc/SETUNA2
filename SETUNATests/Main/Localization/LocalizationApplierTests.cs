using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using SETUNA.Main.StyleItems;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SETUNA.Main.Localization.Tests
{
    /// <summary>
    /// Guards the English overlay against control renames.
    /// <para>
    /// Every designer-owned key is <c>&lt;DeclaringType&gt;.&lt;controlName&gt;</c>, and the
    /// applier only writes text when the key resolves. So a renamed control makes the
    /// English text silently fall back to Chinese — the interface still looks
    /// plausible, which is precisely why this needs a test rather than a walkthrough.
    /// </para>
    /// <para>
    /// The check is done against assembly metadata (the designer declares one field
    /// per control, named exactly like the control) instead of by instantiating all
    /// 29 forms: most of them need constructor arguments, and building fake style
    /// items to satisfy them would test the fakes. <see cref="LocalizationApplierTests"/>
    /// covers the applier's actual behaviour on the forms that can be constructed.
    /// </para>
    /// </summary>
    [TestClass]
    public class DesignerKeyCoverageTests
    {
        const string SelfKey = "$this";
        const string ToolTipSuffix = ".ToolTip";
        const string ItemsInfix = ".Items.";

        [TestMethod]
        public void EveryDesignerKeyResolvesToARealTypeAndControl()
        {
            var problems = new List<string>();

            foreach (var key in DesignerKeys())
            {
                var separator = key.IndexOf('.');
                var scopeName = key.Substring(0, separator);
                var member = key.Substring(separator + 1);

                var scope = FindType(scopeName);
                if (scope == null)
                {
                    problems.Add(key + ": no type named " + scopeName);
                    continue;
                }

                if (member == SelfKey)
                {
                    continue;
                }

                var controlName = ControlNameOf(member);
                if (FindControlField(scope, controlName) == null)
                {
                    problems.Add(key + ": " + scopeName + " has no control named " + controlName);
                }
            }

            Assert.AreEqual(
                0,
                problems.Count,
                "English text would silently fall back to Chinese for these keys:" + Environment.NewLine
                    + string.Join(Environment.NewLine, problems));
        }

        [TestMethod]
        public void ListItemKeysTargetListControls()
        {
            // Item keys are indexed, so they only make sense on a control that has an
            // Items collection. Anything else means the key was mistyped.
            var problems = new List<string>();

            foreach (var key in DesignerKeys().Where(k => k.Contains(ItemsInfix)))
            {
                var separator = key.IndexOf('.');
                var scope = FindType(key.Substring(0, separator));
                var field = FindControlField(scope, ControlNameOf(key.Substring(separator + 1)));
                if (field == null)
                {
                    continue;   // already reported by the coverage test
                }

                if (!typeof(ComboBox).IsAssignableFrom(field.FieldType)
                    && !typeof(ListBox).IsAssignableFrom(field.FieldType))
                {
                    problems.Add(key + ": " + field.FieldType.Name + " has no localizable Items collection");
                }
            }

            Assert.AreEqual(0, problems.Count, string.Join(Environment.NewLine, problems));
        }

        [TestMethod]
        public void ListItemKeysFormAContiguousRunFromZero()
        {
            // The applier replaces the whole item list or none of it, and maps item N
            // to index N. A gap would leave one entry untranslated and shift nothing,
            // producing a silently mixed-language dropdown.
            var groups = new Dictionary<string, List<int>>(StringComparer.Ordinal);

            foreach (var key in DesignerKeys().Where(k => k.Contains(ItemsInfix)))
            {
                var at = key.LastIndexOf(ItemsInfix, StringComparison.Ordinal);
                var prefix = key.Substring(0, at);
                var index = int.Parse(key.Substring(at + ItemsInfix.Length), CultureInfo.InvariantCulture);

                if (!groups.TryGetValue(prefix, out var indexes))
                {
                    groups[prefix] = indexes = new List<int>();
                }

                indexes.Add(index);
            }

            Assert.AreNotEqual(0, groups.Count, "The designer-preset dropdown items should be covered.");

            foreach (var group in groups)
            {
                var sorted = group.Value.OrderBy(x => x).ToList();
                CollectionAssert.AreEqual(
                    Enumerable.Range(0, sorted.Count).ToList(),
                    sorted,
                    group.Key + " item indexes must run 0..n-1 without gaps, got "
                        + string.Join(",", sorted));
            }
        }

        [TestMethod]
        public void ToolTipKeysTargetFormsThatOwnAToolTip()
        {
            // A tooltip key on a form with no ToolTip component can never be applied.
            var problems = new List<string>();

            foreach (var key in DesignerKeys().Where(k => k.EndsWith(ToolTipSuffix, StringComparison.Ordinal)))
            {
                var scopeName = key.Substring(0, key.IndexOf('.'));
                var scope = FindType(scopeName);
                if (scope == null)
                {
                    continue;   // already reported by the coverage test
                }

                if (!HasToolTipField(scope))
                {
                    problems.Add(key + ": " + scopeName + " owns no ToolTip component");
                }
            }

            Assert.AreEqual(0, problems.Count, string.Join(Environment.NewLine, problems));
        }

        static string ControlNameOf(string member)
        {
            if (member.EndsWith(ToolTipSuffix, StringComparison.Ordinal))
            {
                return member.Substring(0, member.Length - ToolTipSuffix.Length);
            }

            var at = member.LastIndexOf(ItemsInfix, StringComparison.Ordinal);
            return at >= 0 ? member.Substring(0, at) : member;
        }

        /// <summary>Designer-owned keys: the English set minus the runtime keys.</summary>
        internal static IEnumerable<string> DesignerKeys()
        {
            var runtime = new HashSet<string>(LanguageLookupTests.NeutralResourceKeys(), StringComparer.Ordinal);
            var keys = LanguageLookupTests.EnglishResourceKeys()
                .Where(key => !runtime.Contains(key))
                .ToList();

            Assert.AreNotEqual(0, keys.Count, "The English overlay should carry designer-owned keys.");
            return keys;
        }

        static Type FindType(string name)
        {
            return typeof(Lang).Assembly.GetTypes().FirstOrDefault(t => t.Name == name);
        }

        static FieldInfo FindControlField(Type scope, string controlName)
        {
            const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Public
                | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

            // Walk the base chain: 19 setting panels inherit cmdOK/cmdCancel from
            // ToolBoxForm's designer, so the field lives on the base type.
            for (var type = scope; type != null; type = type.BaseType)
            {
                var field = type.GetField(controlName, Declared);
                if (field != null && typeof(Control).IsAssignableFrom(field.FieldType))
                {
                    return field;
                }
            }

            return null;
        }

        static bool HasToolTipField(Type scope)
        {
            const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Public
                | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

            for (var type = scope; type != null; type = type.BaseType)
            {
                if (type.GetFields(Declared).Any(f => typeof(ToolTip).IsAssignableFrom(f.FieldType)))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Exercises the applier on forms that can be constructed without fixtures, so
    /// the key-driven overwrite is verified as behaviour and not just as metadata.
    /// </summary>
    [TestClass]
    public class LocalizationApplierTests
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
        public void EnglishOverwritesDesignerTextOnInheritedControls()
        {
            Lang.SetLanguage(AppLanguage.English);

            using (var form = new SETUNA.Main.StyleItems.ToolBoxForm())
            {
                LocalizationApplier.Apply(form);

                // cmdOK/cmdCancel are declared by ToolBoxForm's own designer, so this
                // also covers the base-chain key lookup the 19 panels rely on.
                Assert.AreEqual("OK", FindControl(form, "cmdOK").Text);
                Assert.AreEqual("Cancel", FindControl(form, "cmdCancel").Text);
            }
        }

        [TestMethod]
        public void ChineseLeavesDesignerTextAlone()
        {
            Lang.SetLanguage(AppLanguage.ChineseSimplified);

            using (var form = new SETUNA.Main.StyleItems.ToolBoxForm())
            {
                var before = FindControl(form, "cmdOK").Text;
                LocalizationApplier.Apply(form);

                // Chinese has no entry for designer-owned text, and a missing key must
                // mean "leave the control as the designer built it".
                Assert.AreEqual(before, FindControl(form, "cmdOK").Text);
            }
        }

        [TestMethod]
        public void FormTitleAndLabelsAreLocalizedTogether()
        {
            Lang.SetLanguage(AppLanguage.English);

            using (var form = new SETUNA.Main.HotkeyMsg())
            {
                LocalizationApplier.Apply(form);

                Assert.AreEqual("SETUNA Hotkeys", form.Text, "$this must localize the form's own title.");
                Assert.AreEqual("Hotkeys disabled", FindControl(form, "label1").Text);
                Assert.AreEqual("Close", FindControl(form, "btnClose").Text);
            }
        }

        [TestMethod]
        public void ApplyingToADisposedFormDoesNotThrow()
        {
            Lang.SetLanguage(AppLanguage.English);

            var form = new SETUNA.Main.StyleItems.ToolBoxForm();
            form.Dispose();

            LocalizationApplier.Apply(form);
        }

        [TestMethod]
        public void ReplacingListItemsKeepsTheSelectedIndex()
        {
            // Dropdown values are read by index, so a language switch that reset the
            // selection would quietly change the user's saved setting.
            using (var form = new Form())
            {
                var combo = new ComboBox { Name = "cmbTest" };
                combo.Items.AddRange(new object[] { "one", "two", "three" });
                form.Controls.Add(combo);
                combo.SelectedIndex = 2;

                Lang.SetLanguage(AppLanguage.English);
                LocalizationApplier.Apply(form);

                // No keys exist for this ad-hoc control, so the items must be intact
                // *and* the selection preserved.
                Assert.AreEqual(2, combo.SelectedIndex);
                Assert.AreEqual("three", combo.Items[2]);
            }
        }

        [TestMethod]
        public void SwitchingBackToChineseRestoresTheDesignerText()
        {
            // Chinese has no resource entry for designer-owned text on purpose, so
            // "no key means leave it alone" is only correct for a freshly built form.
            // Once English has overwritten a control, going back needs the original
            // text from somewhere — the applier snapshots it before the first write.
            using (var form = new SETUNA.Main.StyleItems.ToolBoxForm())
            {
                var button = FindControl(form, "cmdOK");
                var designerText = button.Text;

                Lang.SetLanguage(AppLanguage.English);
                LocalizationApplier.Apply(form);
                Assert.AreEqual("OK", button.Text);

                Lang.SetLanguage(AppLanguage.ChineseSimplified);
                LocalizationApplier.Apply(form);
                Assert.AreEqual(
                    designerText,
                    button.Text,
                    "Switching back must restore the designer's Chinese, not leave English in place.");
            }
        }

        [TestMethod]
        public void RepeatedSwitchesDoNotDriftTheText()
        {
            using (var form = new SETUNA.Main.HotkeyMsg())
            {
                var label = FindControl(form, "label1");
                var designerText = label.Text;

                for (var i = 0; i < 5; i++)
                {
                    Lang.SetLanguage(AppLanguage.English);
                    LocalizationApplier.Apply(form);
                    Assert.AreEqual("Hotkeys disabled", label.Text);

                    Lang.SetLanguage(AppLanguage.ChineseSimplified);
                    LocalizationApplier.Apply(form);
                    Assert.AreEqual(designerText, label.Text);
                }
            }
        }

        [TestMethod]
        public void TranslatedListItemsUpdateBothTheItemsAndTheDisplayedText()
        {
            // ComboBox.Text is what the user actually sees. Assigning items[i] in place
            // translates the list without refreshing that line, and re-assigning the
            // same SelectedIndex is a no-op, so the control ends up showing the old
            // language over a translated list.
            Lang.SetLanguage(AppLanguage.English);

            using (var form = new ImageBmpStyleItemPanel(new CImageBmpStyleItem()))
            {
                var combo = (ComboBox)FindControl(form, "cmbDupli");
                combo.SelectedIndex = 0;

                LocalizationApplier.Apply(form);

                Assert.AreEqual("Overwrite", combo.Items[0]);
                Assert.AreEqual(0, combo.SelectedIndex, "The user's selection must survive the swap.");
                Assert.AreEqual("Overwrite", combo.Text, "The visible line must show the new language too.");
            }
        }

        [TestMethod]
        public void SwitchingLanguageBackAndForthKeepsTheSelectedItemStable()
        {
            using (var form = new ImageBmpStyleItemPanel(new CImageBmpStyleItem()))
            {
                var combo = (ComboBox)FindControl(form, "cmbDupli");

                Lang.SetLanguage(AppLanguage.English);
                LocalizationApplier.Apply(form);
                combo.SelectedIndex = 2;

                Lang.SetLanguage(AppLanguage.ChineseSimplified);
                LocalizationApplier.Apply(form);
                Assert.AreEqual(2, combo.SelectedIndex, "Index-addressed settings must not drift on a switch.");
                Assert.AreEqual("重复时指定", combo.Items[2]);

                Lang.SetLanguage(AppLanguage.English);
                LocalizationApplier.Apply(form);
                Assert.AreEqual(2, combo.SelectedIndex);
                Assert.AreEqual("Ask each time", combo.Items[2]);
            }
        }

        [TestMethod]
        public void ControlsHoldingUserDataAreNotTouched()
        {
            Lang.SetLanguage(AppLanguage.English);

            using (var form = new SETUNA.Main.StyleItems.ToolBoxForm())
            {
                // A control whose name matches no key stands in for the ones that show
                // user data: scrap names, user-defined style names.
                var label = new Label { Name = "userDataLabel", Text = "用户自己的名字" };
                form.Controls.Add(label);

                LocalizationApplier.Apply(form);

                Assert.AreEqual("用户自己的名字", label.Text);
            }
        }

        static Control FindControl(Control root, string name)
        {
            var found = root.Controls.Find(name, true);
            Assert.AreNotEqual(0, found.Length, "No control named " + name + " on " + root.GetType().Name);
            return found[0];
        }
    }

    /// <summary>
    /// <see cref="Lang.LanguageChanged"/> is a static event, so a form that forgets to
    /// unsubscribe is kept alive forever and gets called back after disposal. Both
    /// halves are pinned here because both are silent in normal use.
    /// </summary>
    [TestClass]
    public class LanguageChangedSubscriptionTests
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
        public void SubscriberCountReturnsToItsBaselineAfterFormsAreDisposed()
        {
            var baseline = SubscriberCount();

            for (var i = 0; i < 10; i++)
            {
                using (var form = new SETUNA.Main.StyleItems.ToolBoxForm())
                {
                    Assert.AreEqual(baseline + 1, SubscriberCount(), "A live form must be subscribed.");
                }
            }

            Assert.AreEqual(
                baseline,
                SubscriberCount(),
                "Disposed forms are still subscribed; the static event is leaking them.");
        }

        [TestMethod]
        public void DisposingTwiceUnsubscribesOnlyOnce()
        {
            var baseline = SubscriberCount();

            var form = new SETUNA.Main.StyleItems.ToolBoxForm();
            form.Close();
            form.Dispose();

            Assert.AreEqual(baseline, SubscriberCount());
        }

        [TestMethod]
        public void SwitchingLanguageAfterDisposalDoesNotThrow()
        {
            var form = new SETUNA.Main.StyleItems.ToolBoxForm();
            form.Dispose();

            Lang.SetLanguage(AppLanguage.English);
            Lang.SetLanguage(AppLanguage.ChineseSimplified);
        }

        static int SubscriberCount()
        {
            var field = typeof(Lang).GetField("LanguageChanged", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "Lang.LanguageChanged backing field not found; was the event renamed?");

            var handler = (EventHandler)field.GetValue(null);
            return handler == null ? 0 : handler.GetInvocationList().Length;
        }
    }
}
