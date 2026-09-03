using System.Drawing;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SETUNA.Main.Window.Tests
{
    /// <summary>
    /// The tray and scrap context menus are components, not controls: they never enter
    /// <c>Control.Controls</c>, so no control-tree walk reaches them, and their drop-down
    /// window is created by the OS at the moment of opening, after the point where a
    /// <c>WM_DPICHANGED</c> would arrive.
    /// <para>
    /// Under the manual pipeline that meant the application had to size them itself, and
    /// measured on the two-monitor machine it showed: the same menu came out 194x106 with 32px
    /// items and a 27px font on both a 168 DPI and a 96 DPI monitor — identical physical
    /// pixels, so 1.75x oversized on the 96 DPI one. On .NET 8 the <c>ToolStrip</c> DPI path
    /// does it, so <c>ContextStyleMenuStrip</c> no longer has a scaling pass and there is no
    /// arithmetic here to drive.
    /// </para>
    /// <para>
    /// What remains in the suite is the capability the menus rest on, which does not need a
    /// manifest to observe: reading the DPI of the monitor under a screen point, the only way
    /// to answer for a popup that has no window yet. That a drop-down then realises the
    /// metrics of that monitor is measured by <c>probes/MenuDpiProbe</c>, which needs both the
    /// manifest and two monitors at different scale factors.
    /// </para>
    /// </summary>
    [TestClass]
    public class MenuDpiTests
    {
        /// <summary>
        /// A menu shaped like the two real ones, kept so the suite still proves the type can
        /// be built and configured the way the host forms configure it.
        /// </summary>
        static ContextStyleMenuStrip BuildMenu()
        {
            var menu = new ContextStyleMenuStrip();

            // Assigned after the constructor, exactly as Mainform.Designer.cs does it.
            menu.ImageScalingSize = new Size(20, 20);

            var parent = new ToolStripMenuItem("nested");
            parent.DropDownItems.Add(new ToolStripMenuItem("child"));

            menu.Items.Add(parent);
            menu.Items.Add(new ToolStripMenuItem("plain"));

            return menu;
        }

        /// <summary>
        /// Reading the DPI of the monitor under a point is the only way to size a popup that
        /// has no window yet. Every screen the host reports has to answer with a usable value,
        /// and a point far outside the desktop has to fall back to the nearest monitor rather
        /// than fail.
        /// </summary>
        [TestMethod]
        public void EveryScreenReportsAUsableDpi()
        {
            foreach (var screen in Screen.AllScreens)
            {
                var middle = new Point(
                    screen.Bounds.Left + screen.Bounds.Width / 2,
                    screen.Bounds.Top + screen.Bounds.Height / 2);

                Assert.IsTrue(
                    DpiContext.IsUsableDpi(WindowsAPI.GetMonitorDpiAt(middle)),
                    screen.DeviceName + " did not report a DPI at " + middle + ".");
            }

            Assert.IsTrue(
                DpiContext.IsUsableDpi(WindowsAPI.GetMonitorDpiAt(new Point(-100000, -100000))),
                "A point off the desktop should fall back to the nearest monitor.");
        }

        /// <summary>
        /// Nothing in the application may set the menu's own DPI metrics any more: the
        /// framework owns them, and a second writer would fight it. Two things are observable
        /// without a manifest — the icon size the host form assigned survives untouched, and
        /// two menus still share one font object, so no scaled font is being manufactured per
        /// instance. Whether the realised metrics then follow the monitor is
        /// <c>probes/MenuDpiProbe</c>'s question.
        /// </summary>
        [TestMethod]
        public void TheMenuKeepsTheMetricsItWasConfiguredWith()
        {
            using (var menu = BuildMenu())
            using (var second = BuildMenu())
            {
                Assert.AreEqual(
                    new Size(20, 20),
                    menu.ImageScalingSize,
                    "Something rewrote the icon size the host form assigned.");

                Assert.AreSame(
                    menu.Font,
                    second.Font,
                    "Two menus no longer share one font object, so the application is creating "
                        + "a font per menu again.");
            }
        }
    }
}
