using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace DialogRelayoutProbe
{
    /// <summary>
    /// A measurement, not a check: how large owner-drawn row text actually comes out at each
    /// DPI. Gated on <c>DIALOG_PROBE_MEASURE_OWNERDRAW</c> so the gate's verdict never depends
    /// on it.
    /// <para>
    /// The question it answers. <c>SetunaListBox.OnDrawItem</c> paints its rows with
    /// <c>Graphics.DrawString(text, Font, ...)</c> on the <c>DrawItemEventArgs.Graphics</c>,
    /// and GDI+ converts a point-unit font to pixels using *that Graphics'* DPI. The framework
    /// has meanwhile already multiplied the font's point size by the DPI ratio (measured on an
    /// inherited font: 9.00pt at 168 DPI becomes 5.14pt at 96). If the Graphics reports the
    /// monitor's DPI, those two multiplications compound and the row text renders at the ratio
    /// *squared*; if it reports the process DPI, they do not.
    /// </para>
    /// <para>
    /// <see cref="Graphics.MeasureString(string, Font)"/> performs the same conversion
    /// <c>DrawString</c> does, so it is the realised size without having to find ink in a
    /// bitmap. <c>HelpFont</c> is read by reflection: <c>StyleItemListBox</c> is internal to
    /// the application assembly, and its doc comment claims the opposite of what
    /// <c>StyleEditForm</c> does to it.
    /// </para>
    /// </summary>
    static class OwnerDrawText
    {
        const string Sample = "MMMMMMMMMM";

        public static readonly bool Enabled =
            Environment.GetEnvironmentVariable("DIALOG_PROBE_MEASURE_OWNERDRAW") == "1";

        public static void Report(Control root, string label)
        {
            if (!Enabled)
            {
                return;
            }

            foreach (var list in OwnerDrawn(root))
            {
                using (var g = list.CreateGraphics())
                {
                    Console.WriteLine("  [ownerdraw] " + label + " " + list.Name
                        + " g.Dpi=" + g.DpiX.ToString("F0") + "x" + g.DpiY.ToString("F0")
                        + " itemHeight=" + list.ItemHeight);
                    Console.WriteLine("  [ownerdraw]   Font    " + Describe(g, list.Font));

                    var help = HelpFont(list);
                    if (help != null)
                    {
                        Console.WriteLine("  [ownerdraw]   HelpFont " + Describe(g, help));
                    }
                }
            }
        }

        static string Describe(Graphics g, Font font)
        {
            var measured = g.MeasureString(Sample, font);

            return font.Size.ToString("F2") + font.Unit
                + " Font.Height=" + font.Height
                + " GetHeight(g)=" + font.GetHeight(g).ToString("F1")
                + " MeasureString=" + measured.Width.ToString("F1") + "x" + measured.Height.ToString("F1");
        }

        static Font HelpFont(Control control)
        {
            var property = control.GetType().GetProperty(
                "HelpFont", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            return property == null ? null : property.GetValue(control) as Font;
        }

        static IEnumerable<ListBox> OwnerDrawn(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                if (child is ListBox list && list.DrawMode == DrawMode.OwnerDrawFixed)
                {
                    yield return list;
                }

                foreach (var nested in OwnerDrawn(child))
                {
                    yield return nested;
                }
            }
        }

        /// <summary>
        /// The half a synthetic <c>WM_DPICHANGED</c> cannot answer: what DPI a control's own
        /// <see cref="Graphics"/> reports when the window is *really* on a monitor whose scale
        /// differs from the process's. The synthetic message never moves the window, so its
        /// device context legitimately keeps the process DPI and the reading proves nothing
        /// either way. This places the form inside each attached monitor instead.
        /// <para>
        /// Inconclusive rather than wrong when the placement does not take: a sleeping or
        /// disconnected secondary monitor leaves the window on the primary, and
        /// <c>DeviceDpi</c> is what shows that — so it is printed next to the monitor the
        /// placement asked for.
        /// </para>
        /// </summary>
        public static void ReportOnEveryMonitor(Func<Form> build)
        {
            if (!Enabled)
            {
                return;
            }

            Console.WriteLine();
            Console.WriteLine("=== owner-draw text on real monitors ===");

            foreach (var screen in Screen.AllScreens)
            {
                var form = build();
                using (form)
                {
                    Place(form, screen);

                    Console.WriteLine("  [monitor] " + screen.DeviceName
                        + " bounds=" + screen.Bounds
                        + " primary=" + screen.Primary
                        + " -> form.DeviceDpi=" + form.DeviceDpi
                        + " at " + form.Location);

                    Report(form, screen.DeviceName);
                    form.Hide();
                }
            }
        }

        /// <summary>
        /// The permanent check the measurement above turned into: on every attached monitor the
        /// help font must keep its designed proportion to the row's main font.
        /// <para>
        /// Measured 2026-09-03 on a real 96-DPI secondary monitor, which is the only place this
        /// is visible: <c>Font</c> came out 5.71pt (the framework had scaled it) while
        /// <c>HelpFont</c> sat at the designer's 8pt, so the *description* line rendered larger
        /// than the *name* line — 21px against 15px, 36px of text in a 39px row. A synthetic
        /// <c>WM_DPICHANGED</c> cannot see it: the old code scaled <c>HelpFont</c> from the
        /// form's DPI-change hook, which a synthetic transition does run, and which a form born
        /// on the other monitor never runs at all.
        /// </para>
        /// <para>
        /// Ratios, not absolute sizes: the point size that realises a given physical size
        /// depends on the process DPI, so only the two fonts' relation is comparable across
        /// machines. A monitor the placement could not reach is skipped rather than failed —
        /// a sleeping or disconnected secondary leaves the window on the primary, and
        /// <c>DeviceDpi</c> is what says so.
        /// </para>
        /// </summary>
        public static void CheckHelpFontProportion(Func<Form> build, List<string> failures)
        {
            var seen = 0;
            var dpis = new List<int>();

            foreach (var screen in Screen.AllScreens)
            {
                var form = build();
                using (form)
                {
                    Place(form, screen);

                    // Whether the placement took, asked without needing the monitor's DPI: a
                    // sleeping or disconnected secondary leaves the window on the primary.
                    if (Screen.FromControl(form).Bounds != screen.Bounds)
                    {
                        Console.WriteLine("  [ownerdraw] " + screen.DeviceName
                            + " unreachable (the window stayed on "
                            + Screen.FromControl(form).DeviceName + "); skipped");
                        form.Hide();
                        continue;
                    }

                    if (!dpis.Contains(form.DeviceDpi))
                    {
                        dpis.Add(form.DeviceDpi);
                    }

                    foreach (var list in OwnerDrawn(form))
                    {
                        var help = HelpFont(list);
                        if (help == null || list.Font == null || list.Font.Size <= 0f)
                        {
                            continue;
                        }

                        seen++;
                        var ratio = help.Size / list.Font.Size;
                        if (Math.Abs(ratio - DesignedHelpFontRatio) > 0.02f)
                        {
                            failures.Add(screen.DeviceName + " (" + form.DeviceDpi + " DPI) "
                                + list.Name + ": HelpFont is " + help.Size.ToString("F2") + "pt against a "
                                + list.Font.Size.ToString("F2") + "pt row font, a ratio of "
                                + ratio.ToString("F2") + " instead of the designed "
                                + DesignedHelpFontRatio.ToString("F2"));
                        }
                    }

                    form.Hide();
                }
            }

            if (seen == 0)
            {
                failures.Add("no owner-drawn list with a HelpFont was reachable on any monitor, "
                    + "so the help-font proportion was never checked");
                return;
            }

            Console.WriteLine("HelpFont proportion: " + seen + " list(s) over "
                + dpis.Count + " distinct DPI value(s).");

            // One DPI cannot tell a font that follows the monitor from one that ignores it —
            // the same reason MenuDpiProbe reports inconclusive on a single-DPI desktop.
            if (dpis.Count < 2)
            {
                Console.WriteLine("  (inconclusive: a second monitor at a different scale is "
                    + "what makes this check able to fail)");
            }
        }

        /// <summary>The designer's 8pt help font against the row's 10pt name font.</summary>
        const float DesignedHelpFontRatio = 0.8f;

        /// <summary>
        /// The adjacent question, measured the same way: <c>BaseForm.RescaleOwnedFonts</c> also
        /// runs only from the DPI-change hook, so every control with a designer-assigned font is
        /// open to the same failure a form born on the other monitor showed for
        /// <c>HelpFont</c>. Prints each named control's point size against its form's, on every
        /// monitor, so the two can be compared without guessing which mechanism moved them.
        /// </summary>
        public static void ReportExplicitFonts(Func<Form> build)
        {
            if (!Enabled)
            {
                return;
            }

            Console.WriteLine();
            Console.WriteLine("=== designer-assigned Control.Font on real monitors ===");

            foreach (var screen in Screen.AllScreens)
            {
                var form = build();
                using (form)
                {
                    Place(form, screen);

                    Console.WriteLine("  [explicit] " + screen.DeviceName
                        + " landed=" + Screen.FromControl(form).DeviceName
                        + " DeviceDpi=" + form.DeviceDpi
                        + " form.Font=" + form.Font.Size.ToString("F2") + form.Font.Unit);

                    foreach (var child in Descendants(form))
                    {
                        if (child.Parent != null && ReferenceEquals(child.Font, child.Parent.Font))
                        {
                            continue;
                        }

                        Console.WriteLine("  [explicit]   " + child.Name
                            + " " + child.Font.Size.ToString("F2") + child.Font.Unit
                            + " (x" + (child.Font.Size / form.Font.Size).ToString("F2") + " of the form's)");
                    }

                    form.Hide();
                }
            }
        }

        static IEnumerable<Control> Descendants(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                if (!string.IsNullOrEmpty(child.Name))
                {
                    yield return child;
                }

                foreach (var nested in Descendants(child))
                {
                    yield return nested;
                }
            }
        }

        static void Place(Form form, Screen screen)
        {
            form.StartPosition = FormStartPosition.Manual;
            form.ShowInTaskbar = false;

            // Well inside the monitor, so no part of the window can straddle a neighbour and
            // leave the assignment ambiguous.
            form.Location = new Point(
                screen.Bounds.Left + screen.Bounds.Width / 4,
                screen.Bounds.Top + screen.Bounds.Height / 4);
            form.Show();
            Application.DoEvents();
        }
    }
}
