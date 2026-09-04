using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SETUNA.Main.StyleItems;

namespace SETUNA.Main.Localization.Tests
{
    /// <summary>
    /// Walks every form the suite can construct, in both languages, and inspects the
    /// text that actually ends up on the controls.
    /// <para>
    /// This is the automated form of "click through the UI and look for leftovers".
    /// Reading the rendered control text catches things a resource-file comparison
    /// cannot: a key that never gets applied because the control sits somewhere the
    /// walker does not reach, a stale English string, a bare resource key leaking
    /// through. The setting panels are obtained by asking each style item for its own
    /// panel — the same call the application makes — so the panels are wired exactly
    /// as they are in production instead of by a test fixture.
    /// </para>
    /// </summary>
    [TestClass]
    public class LocalizationSweepTests
    {
        static readonly Regex Cjk = new Regex(@"[\u4e00-\u9fff\u3040-\u30ff]", RegexOptions.Compiled);

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
        public void EnglishLeavesNoChineseTextOnAnyReachableForm()
        {
            Lang.SetLanguage(AppLanguage.English);

            var leftovers = new List<string>();
            ForEachConstructibleForm((label, form) =>
            {
                LocalizationApplier.Apply(form);

                foreach (var entry in TextOf(form))
                {
                    if (Cjk.IsMatch(entry.Value))
                    {
                        leftovers.Add(label + " / " + entry.Key + " = " + Quote(entry.Value));
                    }
                }
            });

            Assert.AreEqual(
                0,
                leftovers.Count,
                "These controls still show Chinese with English selected:" + Environment.NewLine
                    + string.Join(Environment.NewLine, leftovers));
        }

        [TestMethod]
        public void NoFormShowsABareResourceKeyOrAnEmptyLabel()
        {
            foreach (var language in new[] { AppLanguage.ChineseSimplified, AppLanguage.English })
            {
                Lang.SetLanguage(language);

                var problems = new List<string>();
                ForEachConstructibleForm((label, form) =>
                {
                    LocalizationApplier.Apply(form);

                    foreach (var entry in TextOf(form))
                    {
                        // "!Some.Key!" is what Lang.T returns in release builds when a
                        // key is missing; it must never reach a control.
                        if (entry.Value.StartsWith("!", StringComparison.Ordinal)
                            && entry.Value.EndsWith("!", StringComparison.Ordinal)
                            && entry.Value.Contains("."))
                        {
                            problems.Add(language + " " + label + " / " + entry.Key + " = " + Quote(entry.Value));
                        }
                    }
                });

                Assert.AreEqual(
                    0,
                    problems.Count,
                    "Missing resource keys reached the interface:" + Environment.NewLine
                        + string.Join(Environment.NewLine, problems));
            }
        }

        [TestMethod]
        public void EveryReachableFormChangesAtLeastSomeTextBetweenLanguages()
        {
            // A form whose text is identical in both languages is either fully
            // untranslated or not reached by the walker at all. Either way it is worth
            // knowing about, and it is exactly the failure a hand walkthrough misses.
            var unchanged = new List<string>();

            var chinese = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            Lang.SetLanguage(AppLanguage.ChineseSimplified);
            ForEachConstructibleForm((label, form) =>
            {
                LocalizationApplier.Apply(form);
                chinese[label] = TextOf(form);
            });

            Lang.SetLanguage(AppLanguage.English);
            ForEachConstructibleForm((label, form) =>
            {
                LocalizationApplier.Apply(form);
                var english = TextOf(form);

                if (!chinese.TryGetValue(label, out var before) || before.Count == 0)
                {
                    return;
                }

                var differing = before.Count(pair =>
                    english.TryGetValue(pair.Key, out var after) && after != pair.Value);

                if (differing == 0)
                {
                    unchanged.Add(label + " (" + before.Count + " text-bearing controls, none changed)");
                }
            });

            Assert.AreEqual(
                0,
                unchanged.Count,
                "These forms look untranslated or unreachable:" + Environment.NewLine
                    + string.Join(Environment.NewLine, unchanged));
        }

        [TestMethod]
        public void StyleItemNamesAndDescriptionsAreTranslated()
        {
            // Style items are listed by GetDisplayName()/GetDescription(), which the
            // control walker never sees: the list holds the objects, not strings.
            var untranslated = new List<string>();

            Lang.SetLanguage(AppLanguage.English);
            foreach (var item in StyleItems())
            {
                foreach (var pair in new[]
                {
                    new KeyValuePair<string, string>("GetDisplayName", item.GetDisplayName()),
                    new KeyValuePair<string, string>("GetDescription", item.GetDescription()),
                    new KeyValuePair<string, string>("StateText", item.StateText),
                })
                {
                    if (pair.Value != null && Cjk.IsMatch(pair.Value))
                    {
                        untranslated.Add(item.GetType().Name + "." + pair.Key + " = " + Quote(pair.Value));
                    }
                }
            }

            Assert.AreEqual(
                0,
                untranslated.Count,
                "These style-item strings are still Chinese under English:" + Environment.NewLine
                    + string.Join(Environment.NewLine, untranslated));
        }

        [TestMethod]
        public void PresetStyleNamesAreTranslated()
        {
            // The tray and scrap menus are built from these.
            var untranslated = new List<string>();

            Lang.SetLanguage(AppLanguage.English);
            foreach (var type in ConcreteSubclassesOf(typeof(SETUNA.Main.Style.CPreStyle)))
            {
                if (type.GetConstructor(Type.EmptyTypes) == null)
                {
                    continue;
                }

                var style = (SETUNA.Main.Style.CStyle)Activator.CreateInstance(type);
                if (style.StyleName != null && Cjk.IsMatch(style.StyleName))
                {
                    untranslated.Add(type.Name + " = " + Quote(style.StyleName));
                }
            }

            Assert.AreNotEqual(0, ConcreteSubclassesOf(typeof(SETUNA.Main.Style.CPreStyle)).Count(),
                "The preset styles should be discoverable.");
            Assert.AreEqual(
                0,
                untranslated.Count,
                "These preset style names are still Chinese under English:" + Environment.NewLine
                    + string.Join(Environment.NewLine, untranslated));
        }

        /// <summary>
        /// Runs <paramref name="inspect"/> over every form this suite can build without
        /// inventing fixtures, disposing each one afterwards.
        /// </summary>
        static void ForEachConstructibleForm(Action<string, Form> inspect)
        {
            var visited = 0;

            // Forms with a parameterless constructor.
            foreach (var type in new[]
            {
                typeof(ToolBoxForm),
                typeof(LoginInput),
                typeof(SETUNA.Main.HotkeyMsg),
            })
            {
                using (var form = (Form)Activator.CreateInstance(type))
                {
                    inspect(type.Name, form);
                    visited++;
                }
            }

            // Setting panels, obtained the way the application does: each style item
            // hands back its own panel.
            foreach (var item in StyleItems())
            {
                var method = typeof(CStyleItem).GetMethod(
                    "GetToolBoxForm", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(method, "CStyleItem.GetToolBoxForm not found; was it renamed?");

                ToolBoxForm panel;
                try
                {
                    panel = (ToolBoxForm)method.Invoke(item, null);
                }
                catch (TargetInvocationException)
                {
                    // A panel that needs more than its style item to come up (network
                    // state, a live scrap). Out of reach here; the manual walkthrough
                    // covers those.
                    continue;
                }

                if (panel == null)
                {
                    continue;
                }

                using (panel)
                {
                    inspect(item.GetType().Name + " -> " + panel.GetType().Name, panel);
                    visited++;
                }
            }

            Assert.IsTrue(visited >= 15, "Expected to reach most forms, only reached " + visited + ".");
        }

        static IEnumerable<CStyleItem> StyleItems()
        {
            foreach (var type in ConcreteSubclassesOf(typeof(CStyleItem)))
            {
                if (type.GetConstructor(Type.EmptyTypes) == null)
                {
                    continue;
                }

                CStyleItem item = null;
                try
                {
                    item = (CStyleItem)Activator.CreateInstance(type);
                }
                catch (Exception)
                {
                    continue;
                }

                yield return item;
            }
        }

        static IEnumerable<Type> ConcreteSubclassesOf(Type baseType)
        {
            return typeof(Lang).Assembly.GetTypes()
                .Where(t => !t.IsAbstract && baseType.IsAssignableFrom(t) && t != baseType)
                .OrderBy(t => t.Name, StringComparer.Ordinal);
        }

        /// <summary>Every non-empty piece of text on the control tree, keyed by path.</summary>
        static Dictionary<string, string> TextOf(Control root)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!string.IsNullOrEmpty(root.Text))
            {
                result["$this"] = root.Text;
            }

            Collect(root, "", result);
            return result;
        }

        static void Collect(Control control, string path, Dictionary<string, string> into)
        {
            foreach (Control child in control.Controls)
            {
                var childPath = path.Length == 0 ? child.Name : path + "/" + child.Name;

                if (!string.IsNullOrEmpty(child.Text))
                {
                    into[childPath] = child.Text;
                }

                // Designer-preset dropdown entries are text too, and are the easiest
                // thing to forget: they are neither controls nor a Text property.
                if (child is ComboBox combo)
                {
                    for (var i = 0; i < combo.Items.Count; i++)
                    {
                        if (combo.Items[i] is string text && text.Length > 0)
                        {
                            into[childPath + ".Items." + i.ToString(CultureInfo.InvariantCulture)] = text;
                        }
                    }
                }
                else if (child is ListBox list)
                {
                    for (var i = 0; i < list.Items.Count; i++)
                    {
                        if (list.Items[i] is string text && text.Length > 0)
                        {
                            into[childPath + ".Items." + i.ToString(CultureInfo.InvariantCulture)] = text;
                        }
                    }
                }

                Collect(child, childPath, into);
            }
        }

        static string Quote(string value)
        {
            return "\"" + value.Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
        }
    }
}
