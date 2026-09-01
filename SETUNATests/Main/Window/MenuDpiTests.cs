using System;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SETUNA.Main.Window.Tests
{
    /// <summary>
    /// The tray and scrap context menus are components, not controls: they never enter
    /// <c>Control.Controls</c>, so the form-wide relayout cannot see them, and their drop-down
    /// window is created by the OS after the point where a <c>WM_DPICHANGED</c> would arrive.
    /// They therefore size themselves from the monitor they are about to appear on, and that
    /// arithmetic is what these tests drive.
    /// <para>
    /// Measured on the two-monitor machine before the change: the same menu came out
    /// 194x106 with 32px items and a 27px font on both a 168 DPI and a 96 DPI monitor —
    /// identical physical pixels, so 1.75x oversized on the 96 DPI one.
    /// </para>
    /// <para>
    /// The baseline here is the process's system DPI, which in this host is 96 (the test
    /// runner has no per-monitor manifest, so <c>GetDpiForSystem</c> reports 96). The tests
    /// are written against that value rather than a literal, and drive the transition
    /// upwards, so they mean the same thing whatever the host reports.
    /// </para>
    /// </summary>
    [TestClass]
    public class MenuDpiTests
    {
        /// <summary>
        /// A menu shaped like the two real ones: an explicit <c>ImageScalingSize</c> assigned
        /// by the host form after construction, and one item carrying a nested drop-down.
        /// </summary>
        static ContextStyleMenuStrip BuildMenu()
        {
            var menu = new ContextStyleMenuStrip();

            // Assigned after the constructor, exactly as Mainform.Designer.cs does it — the
            // reason the baseline cannot be captured in the constructor.
            menu.ImageScalingSize = new Size(20, 20);

            var parent = new ToolStripMenuItem("nested");
            parent.DropDownItems.Add(new ToolStripMenuItem("child"));

            menu.Items.Add(parent);
            menu.Items.Add(new ToolStripMenuItem("plain"));

            return menu;
        }

        static int BaselineDpi
        {
            get { return WindowsAPI.GetSystemDpi(); }
        }

        [TestMethod]
        public void TheMenuMetricsFollowTheTargetMonitorDpi()
        {
            using (var menu = BuildMenu())
            {
                var configuredPoints = menu.Font.SizeInPoints;
                var configuredScaling = menu.ImageScalingSize;
                var target = BaselineDpi * 2;

                menu.ApplyMonitorDpi(target);

                Assert.AreEqual(
                    configuredPoints * 2,
                    menu.Font.SizeInPoints,
                    0.01,
                    "The font did not follow the target monitor's DPI.");

                Assert.AreEqual(
                    new Size(configuredScaling.Width * 2, configuredScaling.Height * 2),
                    menu.ImageScalingSize,
                    "The icon size did not follow the target monitor's DPI.");
            }
        }

        /// <summary>
        /// Nested drop-downs must not need their own pass. Measured: assigning the root's
        /// <c>Font</c> and <c>ImageScalingSize</c> moved the submenu's font from 27px to 16px,
        /// its icons from 20 to 11 and its item height from 32 to 22, and a recursive
        /// assignment afterwards changed nothing — the drop-down reads through to its owner.
        /// <para>
        /// That is what lets the lazily built entries work: the scrap list fills its
        /// drop-down on hover, long after the menu opened, and those items inherit too.
        /// </para>
        /// </summary>
        [TestMethod]
        public void NestedDropDownsInheritTheScaledMetrics()
        {
            using (var menu = BuildMenu())
            {
                var nested = (ToolStripDropDownItem)menu.Items[0];

                menu.ApplyMonitorDpi(BaselineDpi * 2);

                Assert.AreEqual(
                    menu.Font.SizeInPoints,
                    nested.DropDown.Font.SizeInPoints,
                    0.01,
                    "The submenu kept its own font, so it would render at the wrong size.");

                Assert.AreEqual(
                    menu.ImageScalingSize,
                    nested.DropDown.ImageScalingSize,
                    "The submenu kept its own icon size.");

                // An item added after the menu was scaled — the scrap list's case.
                var late = new ToolStripMenuItem("late");
                nested.DropDownItems.Add(late);

                Assert.AreEqual(
                    menu.Font.SizeInPoints,
                    late.Font.SizeInPoints,
                    0.01,
                    "An item built after the menu opened did not inherit the scaled font.");
            }
        }

        /// <summary>
        /// The menu is reopened on alternating monitors any number of times, so the metrics
        /// have to be a function of the target DPI alone. Scaling the previous result instead
        /// of a stored baseline would compound the rounding of every crossing — the same
        /// reason <c>BaseForm</c> keeps a layout baseline.
        /// </summary>
        [TestMethod]
        public void CrossingBetweenTwoDpisRepeatedlyDoesNotDriftTheMetrics()
        {
            using (var menu = BuildMenu())
            {
                var configuredPoints = menu.Font.SizeInPoints;
                var configuredScaling = menu.ImageScalingSize;

                var low = BaselineDpi;
                var high = BaselineDpi * 175 / 100;

                menu.ApplyMonitorDpi(high);
                var atHighPoints = menu.Font.SizeInPoints;
                var atHighScaling = menu.ImageScalingSize;

                for (var i = 0; i < 4; i++)
                {
                    menu.ApplyMonitorDpi(low);

                    Assert.AreEqual(configuredPoints, menu.Font.SizeInPoints, 0.0001,
                        "Round trip " + i + " did not return the font to the configured size.");
                    Assert.AreEqual(configuredScaling, menu.ImageScalingSize,
                        "Round trip " + i + " did not return the icon size.");

                    menu.ApplyMonitorDpi(high);

                    Assert.AreEqual(atHighPoints, menu.Font.SizeInPoints, 0.0001,
                        "Visit " + i + " to the other DPI gave a different font size.");
                    Assert.AreEqual(atHighScaling, menu.ImageScalingSize,
                        "Visit " + i + " to the other DPI gave a different icon size.");
                }
            }
        }

        /// <summary>
        /// 0 means "could not read the DPI", not 96. A menu about to appear on a monitor the
        /// API would not report must be left exactly as configured rather than scaled by a
        /// guess.
        /// </summary>
        [TestMethod]
        public void AnUnreadableDpiLeavesTheMenuAsConfigured()
        {
            using (var menu = BuildMenu())
            {
                var configuredFont = menu.Font;
                var configuredScaling = menu.ImageScalingSize;

                menu.ApplyMonitorDpi(0);

                Assert.AreSame(configuredFont, menu.Font, "The font was replaced without a usable DPI.");
                Assert.AreEqual(configuredScaling, menu.ImageScalingSize, "The icon size changed without a usable DPI.");
            }
        }

        /// <summary>
        /// The scaled fonts are GDI handles the menu creates on every open. Closing the menu
        /// has to hand them back, and the one belonging to <c>ToolStripManager</c> must never
        /// be released — the check is that the menu survives being scaled many times and then
        /// disposed, and that the manager's font is still usable afterwards.
        /// </summary>
        [TestMethod]
        public void ScalingManyTimesDoesNotLeakOrReleaseTheSharedFont()
        {
            Font configured;

            using (var menu = BuildMenu())
            {
                configured = menu.Font;

                for (var i = 1; i <= 50; i++)
                {
                    menu.ApplyMonitorDpi(BaselineDpi + i);
                }
            }

            // Touching a disposed Font throws; the shared one has to have been left alone.
            Assert.IsTrue(configured.Height > 0, "The shared menu font was disposed with the menu.");
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
                    DpiRelayout.IsUsableDpi(WindowsAPI.GetMonitorDpiAt(middle)),
                    screen.DeviceName + " did not report a DPI at " + middle + ".");
            }

            Assert.IsTrue(
                DpiRelayout.IsUsableDpi(WindowsAPI.GetMonitorDpiAt(new Point(-100000, -100000))),
                "A point off the desktop should fall back to the nearest monitor.");
        }
    }
}
