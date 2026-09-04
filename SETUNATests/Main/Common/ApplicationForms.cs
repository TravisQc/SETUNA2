using System.Collections.Generic;
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
            yield return new LoginInput();
            yield return new SETUNA.Main.HotkeyMsg();

            foreach (var panel in StyleItemPanels.All())
            {
                yield return panel;
            }
        }
    }
}
