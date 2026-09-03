using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SETUNA.Main.Localization;
using SETUNA.Main.StyleItems;

namespace SETUNA.Main.Tests
{
    /// <summary>
    /// Builds one of every style-item settings dialog. They are all
    /// <see cref="ToolBoxForm"/> subclasses reached only through their style item, so the
    /// only way to enumerate them is to walk the <c>CStyleItem</c> hierarchy and ask each
    /// item for its panel.
    /// <para>
    /// Shared between the localization sweep, which measures whether the English text
    /// fits, and the DPI sweep, which measures whether the layout follows the monitor —
    /// two questions about the same set of dialogs, so the set is defined once.
    /// </para>
    /// </summary>
    static class StyleItemPanels
    {
        /// <summary>
        /// Every panel the suite can build without application state, ordered by type name
        /// so a failure names the same dialog on every run.
        /// <para>
        /// A style item with no parameterless constructor is skipped — it is not reachable
        /// from here. A panel that throws while building is *not*: it is reported, because a
        /// swallowed build silently shrinks every sweep that runs over this list. That is
        /// how <c>CompactStyleItemPanel</c> dropped out of one run in three while its
        /// preview backdrop still let a failed screen capture escape
        /// (<c>PreviewBackdrop.Capture</c>), and nothing in the output said so.
        /// </para>
        /// </summary>
        public static IEnumerable<ToolBoxForm> All()
        {
            var types = typeof(Lang).Assembly.GetTypes()
                .Where(t => !t.IsAbstract && typeof(CStyleItem).IsAssignableFrom(t))
                .OrderBy(t => t.Name, StringComparer.Ordinal);

            foreach (var type in types)
            {
                if (type.GetConstructor(Type.EmptyTypes) == null)
                {
                    continue;
                }

                yield return Build(type);
            }
        }

        static ToolBoxForm Build(Type styleItemType)
        {
            var item = (CStyleItem)Activator.CreateInstance(styleItemType);
            var method = typeof(CStyleItem).GetMethod(
                "GetToolBoxForm",
                BindingFlags.Instance | BindingFlags.NonPublic);

            try
            {
                return (ToolBoxForm)method.Invoke(item, null);
            }
            catch (TargetInvocationException failed)
            {
                throw new InvalidOperationException(
                    styleItemType.Name + " could not build its settings dialog, so every sweep over "
                        + "these panels would have measured one dialog fewer: "
                        + failed.InnerException,
                    failed.InnerException);
            }
        }
    }
}
