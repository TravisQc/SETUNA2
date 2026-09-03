using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SETUNA.Main;
using SETUNA.Main.Window;

namespace MenuDpiProbe
{
    /// <summary>
    /// Does .NET 8 size a context menu for the monitor it opens on, or does the application
    /// still have to?
    /// <para>
    /// A menu cannot ride the form pipeline: it is a component, so it never enters
    /// <c>Control.Controls</c> and no control-tree walk reaches it, and its drop-down window
    /// is created by the OS at the moment of opening, after any <c>WM_DPICHANGED</c> would have
    /// arrived. <c>ContextStyleMenuStrip</c> used to scale its own <c>Font</c> and
    /// <c>ImageScalingSize</c> before opening for exactly that reason.
    /// </para>
    /// <para>
    /// Measured answer: the framework does it. Two identically configured menus are opened on
    /// every attached monitor — a plain <see cref="ContextMenuStrip"/> as the control group,
    /// and the <see cref="ContextStyleMenuStrip"/> the application really uses — and both come
    /// out at the metrics of the monitor they appeared on. That is what let task 6.3 delete
    /// the manual pass, and this probe is what keeps the deletion honest: if a future runtime
    /// stops sizing drop-downs per monitor, the control group and the real menu both fail here
    /// rather than silently rendering 1.75x oversized on the low-DPI screen.
    /// </para>
    /// <para>
    /// The verdict needs two monitors at different DPI. With one DPI attached the probe reports
    /// what it measured and says so rather than claiming a result.
    /// </para>
    /// </summary>
    internal static class Program
    {
        [STAThread]
        static int Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

            if (Application.HighDpiMode != HighDpiMode.PerMonitorV2)
            {
                Console.WriteLine("FAIL: the probe process reports " + Application.HighDpiMode
                    + ", so no drop-down will be sized per monitor. Check app.manifest.");
                return 2;
            }

            Console.WriteLine("HighDpiMode: " + Application.HighDpiMode);

            var points = ProbePoints();
            foreach (var point in points)
            {
                Console.WriteLine(point.Describe());
            }

            if (points.Count == 0)
            {
                Console.WriteLine("FAIL: no monitor reported a usable DPI.");
                return 3;
            }

            var framework = new List<Reading>();
            var application = new List<Reading>();

            foreach (var point in points)
            {
                framework.Add(Open(BuildFrameworkMenu(), point));
                application.Add(Open(BuildApplicationMenu(), point));
            }

            Console.WriteLine();
            Console.WriteLine("--- plain ContextMenuStrip (the control group) ---");
            foreach (var reading in framework)
            {
                Console.WriteLine("  " + reading);
            }

            Console.WriteLine();
            Console.WriteLine("--- ContextStyleMenuStrip (the menu the application uses) ---");
            foreach (var reading in application)
            {
                Console.WriteLine("  " + reading);
            }

            return Report(framework, application);
        }

        /// <summary>
        /// The font a menu realises has to follow the monitor it opened on. That is the metric
        /// worth asserting: it decides whether the menu reads as the right size, and it is the
        /// one WinForms derives everything else from.
        /// <para>
        /// Only the font is held to the ratio. Item height mixes the font with padding
        /// (measured 24 → 34 across 96 → 168, a ratio of 1.42), and the icon column is not a
        /// ratio at all: the designer's 20x20 survives on the monitor whose DPI matches the
        /// process and is recomputed from the framework's own 16x16 default elsewhere
        /// (measured 16 at 96, where the designer value would fold to 11). Both are printed so
        /// the numbers are on the record.
        /// </para>
        /// </summary>
        static int Report(List<Reading> framework, List<Reading> application)
        {
            var distinctDpi = application.Select(r => r.Dpi).Distinct().Count();

            Console.WriteLine();
            if (distinctDpi < 2)
            {
                Console.WriteLine("INCONCLUSIVE: every attached monitor reports "
                    + application[0].Dpi + " DPI, so nothing here distinguishes a menu that "
                    + "follows the monitor from one that ignores it. Attach a second monitor "
                    + "at a different scale factor and re-run.");
                return 4;
            }

            var findings = new List<string>();
            findings.AddRange(FontFollowsTheMonitor(application, nameof(ContextStyleMenuStrip)));
            findings.AddRange(FontFollowsTheMonitor(framework, nameof(ContextMenuStrip)));

            if (findings.Count > 0)
            {
                Console.WriteLine("FAIL: " + findings.Count + " findings.");
                foreach (var finding in findings)
                {
                    Console.WriteLine("  " + finding);
                }

                return 1;
            }

            Console.WriteLine("PASS: both menus realise a font that follows the monitor they open"
                + " on, so the application needs no scaling pass of its own.");

            return 0;
        }

        /// <summary>
        /// One pixel for the font height's own rounding, one more because the point size the
        /// framework derives is itself rounded before it is realised.
        /// </summary>
        const int FontSlop = 2;

        static IEnumerable<string> FontFollowsTheMonitor(List<Reading> readings, string what)
        {
            var byDpi = readings
                .GroupBy(r => r.Dpi)
                .OrderBy(g => g.Key)
                .Select(g => g.First())
                .ToArray();

            var reference = byDpi[0];

            for (var i = 1; i < byDpi.Length; i++)
            {
                var expected = (int)Math.Round(
                    reference.FontHeight * (double)byDpi[i].Dpi / reference.Dpi,
                    MidpointRounding.AwayFromZero);

                if (Math.Abs(byDpi[i].FontHeight - expected) > FontSlop)
                {
                    yield return what + " on " + byDpi[i].Monitor + ": a " + byDpi[i].FontHeight
                        + "px font at " + byDpi[i].Dpi + " DPI, but " + reference.FontHeight
                        + "px at " + reference.Dpi + " DPI means about " + expected + "px";
                }
            }
        }

        static ContextMenuStrip BuildFrameworkMenu()
        {
            return Shape(new ContextMenuStrip());
        }

        static ContextMenuStrip BuildApplicationMenu()
        {
            return Shape(new ContextStyleMenuStrip());
        }

        /// <summary>
        /// The shape of the two real menus: an <c>ImageScalingSize</c> assigned by the host
        /// form after construction (<c>Mainform.Designer.cs</c> sets 20x20 on both), one item
        /// carrying a nested drop-down, and one plain item.
        /// </summary>
        static ContextMenuStrip Shape(ContextMenuStrip menu)
        {
            menu.ImageScalingSize = new Size(20, 20);

            var nested = new ToolStripMenuItem("nested");
            nested.DropDownItems.Add(new ToolStripMenuItem("child"));

            menu.Items.Add(nested);
            menu.Items.Add(new ToolStripMenuItem("plain"));

            return menu;
        }

        static Reading Open(ContextMenuStrip menu, ProbePoint point)
        {
            using (menu)
            {
                menu.Show(point.At);
                Application.DoEvents();

                var nested = (ToolStripDropDownItem)menu.Items[0];

                var reading = new Reading
                {
                    Menu = menu.GetType().Name,
                    Monitor = point.DeviceName,
                    Dpi = point.Dpi,
                    FontPoints = menu.Font.SizeInPoints,
                    FontHeight = menu.Font.Height,
                    ImageScalingSize = menu.ImageScalingSize,
                    ItemHeight = menu.Items[0].Height,
                    DropDownSize = menu.Size,
                    NestedFontHeight = nested.DropDown.Font.Height,
                    NestedImageScalingSize = nested.DropDown.ImageScalingSize,
                };

                menu.Close();
                Application.DoEvents();

                return reading;
            }
        }

        /// <summary>
        /// The middle of every attached monitor. A drop-down is placed at a screen point, so
        /// the monitor is selected by that point and not by any window.
        /// </summary>
        static List<ProbePoint> ProbePoints()
        {
            var points = new List<ProbePoint>();

            foreach (var snapshot in WindowsAPI.EnumerateMonitorSnapshots())
            {
                if (!snapshot.IsAvailable)
                {
                    Console.WriteLine("skipping " + snapshot.DeviceName + ": no DPI reported");
                    continue;
                }

                points.Add(new ProbePoint
                {
                    DeviceName = snapshot.DeviceName,
                    Dpi = snapshot.DpiX,
                    Bounds = snapshot.NativeBounds,
                    At = new Point(
                        snapshot.WorkingArea.Left + snapshot.WorkingArea.Width / 2,
                        snapshot.WorkingArea.Top + snapshot.WorkingArea.Height / 2),
                });
            }

            return points;
        }

        sealed class ProbePoint
        {
            public string DeviceName;
            public int Dpi;
            public Rectangle Bounds;
            public Point At;

            public string Describe()
            {
                return DeviceName + " " + Bounds + " @" + Dpi + " DPI, opening at " + At;
            }
        }

        sealed class Reading
        {
            public string Menu;
            public string Monitor;
            public int Dpi;
            public float FontPoints;
            public int FontHeight;
            public Size ImageScalingSize;
            public int ItemHeight;
            public Size DropDownSize;
            public int NestedFontHeight;
            public Size NestedImageScalingSize;

            public override string ToString()
            {
                return Monitor + " @" + Dpi + ": font=" + FontPoints.ToString("F2") + "pt/"
                    + FontHeight + "px icons=" + ImageScalingSize.Width + " item=" + ItemHeight
                    + " size=" + DropDownSize.Width + "x" + DropDownSize.Height
                    + " nested=" + NestedFontHeight + "px/" + NestedImageScalingSize.Width;
            }
        }
    }
}
