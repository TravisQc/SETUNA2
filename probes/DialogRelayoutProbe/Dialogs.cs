using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using SETUNA.Main.StyleItems;

namespace DialogRelayoutProbe
{
    /// <summary>
    /// Every logical dialog the probe can build without application state. Kept in step with
    /// the suite's <c>ApplicationForms</c> by hand — the two lists answer the same question
    /// in processes with deliberately different DPI awareness, so neither can reference the
    /// other.
    /// </summary>
    static class Dialogs
    {
        /// <summary>
        /// <paramref name="buildFailures"/> collects the panels that threw while building.
        /// They are reported rather than skipped: a swallowed build silently shrinks the
        /// sweep, which is how <c>CompactStyleItemPanel</c> dropped out of one run in three
        /// while a failed screen capture could still escape its preview backdrop.
        /// </summary>
        public static IEnumerable<BaseForm> All(List<string> buildFailures)
        {
            yield return new SETUNA.Main.Option.OptionForm(SETUNA.Main.Option.SetunaOption.GetDefaultOption());
            yield return new SETUNA.Main.Option.StyleEditForm(null, new SETUNA.Main.KeyItems.KeyItemBook());
            yield return new ToolBoxForm();
            yield return new LayerRenameWindow();
            yield return new LoginInput();
            yield return new SETUNA.Main.HotkeyMsg();
            yield return new ScrapPaintToolBar(null);
            yield return new ScrapPaintPenTool(new PenTool(Color.Black, null));
            yield return new ScrapPaintTextTool(new TextTool(null));

            foreach (var panel in StyleItemPanels(buildFailures))
            {
                yield return panel;
            }
        }

        /// <summary>
        /// The settings panels are reachable only through their style item, so the hierarchy
        /// has to be walked and each item asked for its panel.
        /// </summary>
        static IEnumerable<ToolBoxForm> StyleItemPanels(List<string> buildFailures)
        {
            var getToolBoxForm = typeof(CStyleItem).GetMethod(
                "GetToolBoxForm", BindingFlags.Instance | BindingFlags.NonPublic);

            var types = typeof(CStyleItem).Assembly.GetTypes()
                .Where(t => !t.IsAbstract && typeof(CStyleItem).IsAssignableFrom(t))
                .Where(t => t.GetConstructor(Type.EmptyTypes) != null)
                .OrderBy(t => t.Name, StringComparer.Ordinal);

            foreach (var type in types)
            {
                ToolBoxForm panel = null;
                try
                {
                    panel = (ToolBoxForm)getToolBoxForm.Invoke(Activator.CreateInstance(type), null);
                }
                catch (Exception failed)
                {
                    buildFailures.Add(type.Name + " could not build its settings dialog, so the sweep "
                        + "covered one dialog fewer: " + (failed.InnerException ?? failed).Message);
                }

                if (panel != null)
                {
                    yield return panel;
                }
            }
        }
    }
}
