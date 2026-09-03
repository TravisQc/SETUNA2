using System.Collections.Generic;
using System.Drawing;
using SETUNA.Main.StyleItems;

namespace SETUNA.Main.Tests
{
    /// <summary>
    /// Every top-level window the suite can build without application state.
    /// <para>
    /// Shared by the translation fit measurements and the DPI baseline check: both ask
    /// one question about the same set of logical dialogs, so the set is defined once.
    /// <see cref="StyleItemPanels"/> supplies the settings panels, which are reached
    /// only through their style item and so have to be enumerated differently.
    /// </para>
    /// <para>
    /// The three paint palettes take their tool object with a null canvas. Their
    /// constructors only store it, and building a palette touches nothing that reaches
    /// the canvas — <c>ClearCommand</c> allocates a command and stops there.
    /// </para>
    /// </summary>
    static class ApplicationForms
    {
        /// <summary>
        /// The options dialog comes first because it holds most of the app's text.
        /// </summary>
        public static IEnumerable<BaseForm> All()
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

            foreach (var panel in StyleItemPanels.All())
            {
                yield return panel;
            }
        }
    }
}
