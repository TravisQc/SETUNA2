using System.Drawing;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SETUNA.Main;
using SETUNA.Main.Option;
using SETUNA.Main.Tests;

namespace SETUNATests.Main.Window
{
    [TestClass]
    public class SetunaListBoxLayoutTests
    {
        [DataTestMethod]
        [DataRow("pageStyle")]
        [DataRow("pageScrapMenu")]
        public void OptionPagesOpenWithReadableLists(string pageName)
        {
            StaThread.Run(() =>
            {
                using (var form = new OptionForm(SetunaOption.GetDefaultOption()))
                {
                    form.StartPosition = FormStartPosition.Manual;
                    form.Location = new Point(-30000, -30000);
                    form.ShowInTaskbar = false;
                    try
                    {
                        form.Show();
                        var tabs = (TabControl)form.Controls.Find("tabControl1", true)[0];
                        tabs.SelectedTab = (TabPage)form.Controls.Find(pageName, true)[0];
                        Application.DoEvents();

                        var navigationFont = form.Controls.Find("lblMenuAll", true)[0].Font;
                        var names = pageName == "pageStyle"
                            ? new[] { "listStyles" }
                            : new[] { "listScrapMenuStyles", "listScrapMenuItems", "listScrapMenuList" };

                        foreach (var name in names)
                        {
                            var list = (ListBox)form.Controls.Find(name, true)[0];
                            Assert.IsTrue(list.Items.Count > 0, name + " must contain its default actions.");
                            // Both use 9pt at the design baseline. Comparing their
                            // proportion also works when WinForms scales the fonts.
                            Assert.IsTrue(list.Font.SizeInPoints >= navigationFont.SizeInPoints * 0.9f,
                                name + " text must be as readable as the navigation labels.");
                            Assert.IsTrue(list.ClientSize.Height >= list.ItemHeight * 4,
                                name + " must show several complete rows, rather than a collapsed border.");
                        }
                    }
                    finally
                    {
                        form.Close();
                    }
                }
            });
        }

        [DataTestMethod]
        [DataRow(120)]
        [DataRow(144)]
        [DataRow(168)]
        [DataRow(192)]
        public void DpiRoundTripsPreserveTheListViewport(int dpi)
        {
            StaThread.Run(() =>
            {
                // These include the short "Other" list that used to lose a row on
                // each trip until only its border remained.
                foreach (var height in new[] { 102, 202, 302, 345 })
                {
                    using (var list = new SetunaListBox { Size = new Size(265, height) })
                    {
                        list.Items.AddRange(new object[] { "Copy", "Save", "Close" });
                        list.SelectedIndex = 1;
                        _ = list.Handle;

                        Assert.AreEqual(height, list.Height, "Creating the native list must preserve its layout height.");
                        var baseline = list.Size;

                        for (var trip = 0; trip < 5; trip++)
                        {
                            list.Scale(new SizeF(dpi / 96f, dpi / 96f));
                            list.Scale(new SizeF(96f / dpi, 96f / dpi));

                            Assert.AreEqual(baseline.Width, list.Width, 1, $"Width after trip {trip + 1} through {dpi} DPI.");
                            Assert.AreEqual(baseline.Height, list.Height, 1, $"Height after trip {trip + 1} through {dpi} DPI.");
                            Assert.AreEqual(1, list.SelectedIndex);
                            Assert.AreEqual(3, list.Items.Count);
                        }
                    }
                }
            });
        }
    }
}
