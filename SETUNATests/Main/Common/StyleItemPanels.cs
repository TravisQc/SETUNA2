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
        /// Items needing more context than this host can supply are skipped rather than
        /// reported: <c>LocalizationSweepTests</c> enumerates the same hierarchy from the
        /// type system alone, so a panel that stops being constructible cannot go
        /// unnoticed by dropping out here.
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

                var panel = TryBuild(type);
                if (panel != null)
                {
                    yield return panel;
                }
            }
        }

        static ToolBoxForm TryBuild(Type styleItemType)
        {
            try
            {
                var item = (CStyleItem)Activator.CreateInstance(styleItemType);
                var method = typeof(CStyleItem).GetMethod(
                    "GetToolBoxForm",
                    BindingFlags.Instance | BindingFlags.NonPublic);

                return (ToolBoxForm)method.Invoke(item, null);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
