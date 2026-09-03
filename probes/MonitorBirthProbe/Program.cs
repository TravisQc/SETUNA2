using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using DialogRelayoutProbe;
using SETUNA.Main;

namespace MonitorBirthProbe
{
    /// <summary>
    /// The half the synthetic ladder cannot answer: what a dialog looks like when it is really
    /// **on** another monitor.
    /// <para>
    /// <c>DialogRelayoutProbe</c> posts <c>WM_DPICHANGED</c> to the top-level window, which is
    /// what makes it deterministic and independent of the attached hardware. But a real monitor
    /// change also makes the OS send <c>WM_DPICHANGED_BEFOREPARENT</c> to every child window,
    /// and that is where the framework rescales a child's designer-assigned
    /// <see cref="Control.Font"/> and the outer rectangle of a nested container that scales
    /// itself. A posted message cannot reproduce those (the handler re-reads
    /// <c>GetDpiForWindow</c> on the child, which has not changed), so under synthesis they
    /// stay frozen and the application looks like it must move them itself. It did, twice, and
    /// on real hardware both mechanisms applied the DPI ratio a second time on top of the
    /// framework's own work — nav labels at 15.75pt instead of 9pt, a spin box shrunk to 26x21
    /// and hidden underneath its own check box. This probe is where that cannot hide.
    /// </para>
    /// <para>
    /// It checks relations, not absolutes, so it needs no table of designer coordinates: a
    /// control's font must keep the same proportion to its form's font on every monitor, and its
    /// rectangle divided by the monitor's DPI must come out the same everywhere. Both are
    /// DPI-independent by construction, and a factor-of-1.75 mistake is two orders of magnitude
    /// outside the tolerance either needs.
    /// </para>
    /// </summary>
    internal static class Program
    {
        /// <summary>
        /// How far two DPI-normalised rectangles may disagree, in 96-DPI units.
        /// <para>
        /// Deliberately coarse. Every edge is rounded once per monitor, a container's rounding
        /// shifts its children again, and text metrics do not scale linearly at all — chasing
        /// that here would re-derive the synthetic ladder's whole measurement discipline in a
        /// place that cannot enumerate DPI steps. The defects this probe exists to catch are
        /// whole factors (1.75, 3.06, 0.57), not pixels, so a wide band costs nothing: reverting
        /// the two mechanisms this probe was written for reports 68 findings at this tolerance.
        /// </para>
        /// </summary>
        const int NormalisedSlop = 6;

        /// <summary>Font proportions are exact ratios of floats, so this is real slack.</summary>
        const float RatioSlop = 0.02f;

        [STAThread]
        static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // A probe must never leave a modal dialog on the user's screen: WinForms catches
            // exceptions thrown on the message pump and shows ThreadExceptionDialog, which stops
            // the run behind a window nobody is watching.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);

            if (Application.HighDpiMode != HighDpiMode.PerMonitorV2)
            {
                Console.WriteLine("FAIL: the probe process reports " + Application.HighDpiMode
                    + ", so WinForms will not scale anything. Check app.manifest.");
                return 2;
            }

            Console.WriteLine("HighDpiMode: " + Application.HighDpiMode);
            foreach (var monitor in WindowsAPI.EnumerateMonitorSnapshots())
            {
                Console.WriteLine("  monitor " + monitor.DeviceName + " " + monitor.NativeBounds
                    + " dpi=" + monitor.DpiX + (monitor.IsPrimary ? " PRIMARY" : ""));
            }

            var samples = new List<Sample>();
            var failures = new List<string>();
            var unreachable = 0;

            // Where to write one render per dialog per monitor for a human to look at. Optional:
            // the comparison runs either way. Numbers say a font is x1.75 of the form's; a
            // picture says the nav labels are clipped, which is what the report was about.
            var screenshots = args.Length > 0 ? args[0] : null;
            if (screenshots != null)
            {
                System.IO.Directory.CreateDirectory(screenshots);
                Console.WriteLine("Screenshots: " + screenshots);
            }

            foreach (var screen in Screen.AllScreens)
            {
                foreach (var form in Dialogs.All(failures))
                {
                    using (form)
                    {
                        var name = form.GetType().Name;

                        if (!Place(form, screen))
                        {
                            // A sleeping or disconnected monitor leaves the window on the
                            // primary. Inconclusive, not a failure — and counted, so a run
                            // where nothing landed cannot read as a pass.
                            unreachable++;
                            continue;
                        }

                        samples.Add(Read(form, name + " (born there)"));
                        Capture(form, screenshots, name);

                        // The user's actual gesture. Every other monitor in turn, then back:
                        // the return trip is what showed the old mechanisms multiplying.
                        foreach (var other in Screen.AllScreens)
                        {
                            if (other.DeviceName != screen.DeviceName && Place(form, other))
                            {
                                samples.Add(Read(form, name + " (dragged from " + Short(screen) + ")"));

                                if (Place(form, screen))
                                {
                                    samples.Add(Read(form, name + " (dragged back from " + Short(other) + ")"));
                                }
                            }
                        }

                        form.Hide();
                    }
                }
            }

            Compare(samples, failures);

            Console.WriteLine();
            Console.WriteLine(samples.Count + " dialog readings over "
                + DistinctDpi(samples).Count + " distinct DPI value(s), "
                + unreachable + " placement(s) the desktop could not honour.");

            if (DistinctDpi(samples).Count < 2)
            {
                // The same reason MenuDpiProbe reports inconclusive on a single-DPI desktop: one
                // scale cannot tell a control that follows the monitor from one that ignores it.
                Console.WriteLine("INCONCLUSIVE: a second monitor at a different scale is what "
                    + "makes this check able to fail. Nothing was proven.");
                return 4;
            }

            if (failures.Count > 0)
            {
                Console.WriteLine("FAIL: " + failures.Count + " findings.");
                foreach (var failure in failures)
                {
                    Console.WriteLine("  " + failure);
                }

                return 1;
            }

            Console.WriteLine("PASS: on every monitor, and after every drag between them, each "
                + "control kept its designed proportion to its form's font and its rectangle "
                + "tracked the monitor's DPI.");

            return 0;
        }

        static string Short(Screen screen)
        {
            var name = screen.DeviceName;
            var cut = name.LastIndexOf('\\');
            return cut < 0 ? name : name.Substring(cut + 1);
        }

        /// <summary>One dialog as it stands on one monitor, however it got there.</summary>
        sealed class Sample
        {
            public int Dpi;
            public string Dialog;
            public string Origin;
            public Dictionary<string, Reading> Readings;
        }

        /// <summary>
        /// One control's DPI-independent state: its rectangle (to be divided by the monitor's
        /// DPI before comparing) and its font's proportion to the form's.
        /// </summary>
        sealed class Reading
        {
            public Rectangle Bounds;
            public float FontRatio;

            /// <summary>Its own text decides its size, and text metrics do not scale linearly.</summary>
            public bool SizeFollowsContent;

            /// <summary>Its height comes from the font: spin boxes, combo boxes, single-line edits.</summary>
            public bool HeightFollowsFont;
        }

        static Sample Read(Form form, string origin)
        {
            var readings = new Dictionary<string, Reading>(StringComparer.Ordinal);
            Collect(form, form.GetType().Name, form.Font.Size, readings);

            return new Sample
            {
                // The window's own DPI, not Control.DeviceDpi: one source for every DPI value,
                // see WindowsAPI.GetWindowDpi.
                Dpi = WindowsAPI.GetWindowDpi(form.Handle),
                Dialog = form.GetType().Name,
                Origin = origin,
                Readings = readings,
            };
        }

        static void Collect(Control parent, string path, float formFontSize, Dictionary<string, Reading> into)
        {
            foreach (Control child in parent.Controls)
            {
                // Controls the framework builds inside a composite (a NumericUpDown's editor and
                // spin buttons) have no name and no designer geometry of their own.
                if (string.IsNullOrEmpty(child.Name))
                {
                    continue;
                }

                var childPath = path + "/" + child.Name;
                into[childPath] = new Reading
                {
                    Bounds = child.Bounds,
                    FontRatio = formFontSize > 0f ? child.Font.Size / formFontSize : 0f,
                    SizeFollowsContent = child.AutoSize,
                    HeightFollowsFont = child is UpDownBase || child is ComboBox || child is ListBox
                        || (child is TextBox text && !text.Multiline),
                };

                Collect(child, childPath, formFontSize, into);
            }
        }

        /// <summary>
        /// Puts the window inside <paramref name="screen"/> and reports whether that took. The
        /// window is moved, not recreated, so this is the drag — the OS sends the same
        /// <c>WM_DPICHANGED</c> it sends when a hand does it.
        /// <para>
        /// False rather than a failure when the placement cannot happen: a window larger than
        /// the target monitor would be clamped, and a sleeping or disconnected monitor leaves it
        /// on the primary. Both are answered by asking where it actually landed.
        /// </para>
        /// </summary>
        static bool Place(Form form, Screen screen)
        {
            form.StartPosition = FormStartPosition.Manual;
            form.ShowInTaskbar = false;

            if (!form.Visible)
            {
                form.Location = new Point(screen.Bounds.Left + 20, screen.Bounds.Top + 20);
                form.Show();
                Application.DoEvents();
            }

            form.Location = new Point(
                screen.Bounds.Left + Math.Max(20, (screen.Bounds.Width - form.Width) / 2),
                screen.Bounds.Top + Math.Max(20, (screen.Bounds.Height - form.Height) / 2));

            // Two passes: the first delivers WM_DPICHANGED, the second drains the layout the
            // framework queues from it.
            Application.DoEvents();
            Application.DoEvents();

            if (Screen.FromControl(form).Bounds != screen.Bounds
                || !DpiUsable(WindowsAPI.GetWindowDpi(form.Handle)))
            {
                return false;
            }

            LayOutEveryTabPage(form);

            return true;
        }

        static bool DpiUsable(int dpi)
        {
            return dpi >= 48 && dpi <= 960;
        }

        /// <summary>
        /// The window as the user sees it, off the screen rather than out of
        /// <see cref="Control.DrawToBitmap"/>: the defects this probe is about are in text the
        /// native controls render themselves, and a bitmap the framework paints into is not the
        /// same pixels. In a per-monitor-v2 process <c>Screen.Bounds</c> is already physical
        /// pixels, so the window rectangle needs no conversion (see <c>SurfaceGeometryProbe</c>).
        /// </summary>
        static void Capture(Form form, string directory, string name)
        {
            if (directory == null)
            {
                return;
            }

            form.Activate();
            Application.DoEvents();

            var bounds = form.Bounds;
            using (var bitmap = new Bitmap(bounds.Width, bounds.Height))
            {
                using (var canvas = Graphics.FromImage(bitmap))
                {
                    canvas.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
                }

                bitmap.Save(
                    System.IO.Path.Combine(
                        directory,
                        name + "-" + WindowsAPI.GetWindowDpi(form.Handle).ToString("000") + ".png"),
                    System.Drawing.Imaging.ImageFormat.Png);
            }
        }

        /// <summary>
        /// A tab page that has never been selected has never been laid out, so its controls
        /// report whatever size they were last given — which right after a DPI change is the old
        /// one. Selecting each page in turn forces the layout the user would trigger by clicking
        /// the tab, so the measurement describes what they will actually see. (Measured: on the
        /// 96-DPI monitor <c>hotkeyControl1</c> reads 350x40 until its page is shown and 200x23
        /// afterwards.)
        /// </summary>
        static void LayOutEveryTabPage(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                if (child is TabControl tabs && tabs.TabCount > 0)
                {
                    var selected = tabs.SelectedIndex;
                    for (var i = 0; i < tabs.TabCount; i++)
                    {
                        tabs.SelectedIndex = i;
                        Application.DoEvents();
                    }

                    tabs.SelectedIndex = selected;
                    Application.DoEvents();
                }

                LayOutEveryTabPage(child);
            }
        }

        /// <summary>
        /// Every reading of a dialog against the first one taken of it. One reference rather than
        /// all pairs, so a control that is wrong everywhere is reported once instead of N².
        /// </summary>
        static void Compare(List<Sample> samples, List<string> failures)
        {
            var references = new Dictionary<string, Sample>(StringComparer.Ordinal);

            foreach (var sample in samples)
            {
                Sample reference;
                if (!references.TryGetValue(sample.Dialog, out reference))
                {
                    references[sample.Dialog] = sample;
                    continue;
                }

                foreach (var pair in reference.Readings)
                {
                    Reading now;
                    if (!sample.Readings.TryGetValue(pair.Key, out now))
                    {
                        failures.Add(pair.Key + " disappeared from the control tree "
                            + Where(sample, reference));
                        continue;
                    }

                    var was = pair.Value;
                    if (Math.Abs(now.FontRatio - was.FontRatio) > RatioSlop)
                    {
                        failures.Add(pair.Key + ": its font is x" + now.FontRatio.ToString("F2")
                            + " of the form's " + Where(sample, reference) + ", against x"
                            + was.FontRatio.ToString("F2") + " " + At(reference));
                    }

                    var here = Normalise(now.Bounds, sample.Dpi);
                    var there = Normalise(was.Bounds, reference.Dpi);

                    if (Off(here.Location, there.Location)
                        || (!was.SizeFollowsContent && Off(here.Width, there.Width))
                        || (!was.SizeFollowsContent && !was.HeightFollowsFont
                            && Off(here.Height, there.Height)))
                    {
                        failures.Add(pair.Key + ": " + now.Bounds + " at " + sample.Dpi
                            + " DPI is " + here + " in 96-DPI units " + Where(sample, reference)
                            + ", against " + there + " from " + was.Bounds + " " + At(reference));
                    }
                }
            }
        }

        /// <summary>The rectangle in 96-DPI units, which is the only frame two monitors share.</summary>
        static Rectangle Normalise(Rectangle bounds, int dpi)
        {
            return new Rectangle(
                bounds.X * 96 / dpi, bounds.Y * 96 / dpi,
                bounds.Width * 96 / dpi, bounds.Height * 96 / dpi);
        }

        static bool Off(Point a, Point b)
        {
            return Off(a.X, b.X) || Off(a.Y, b.Y);
        }

        static bool Off(int a, int b)
        {
            return Math.Abs(a - b) > NormalisedSlop;
        }

        static string Where(Sample sample, Sample reference)
        {
            return "at " + sample.Dpi + " DPI " + sample.Origin;
        }

        static string At(Sample reference)
        {
            return "at " + reference.Dpi + " DPI " + reference.Origin;
        }

        static List<int> DistinctDpi(List<Sample> samples)
        {
            var seen = new List<int>();
            foreach (var sample in samples)
            {
                if (!seen.Contains(sample.Dpi))
                {
                    seen.Add(sample.Dpi);
                }
            }

            return seen;
        }
    }
}
