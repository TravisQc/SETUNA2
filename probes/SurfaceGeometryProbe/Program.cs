using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SETUNA.Main;
using SETUNA.Main.Window;

namespace SurfaceGeometryProbe
{
    /// <summary>
    /// Are the physical surfaces really working in physical pixels?
    /// <para>
    /// The capture path expresses every rectangle in <c>Screen.Bounds</c> coordinates and hands
    /// them straight to <c>BitBlt</c>. That is only correct if <c>Screen.Bounds</c> is device
    /// pixels — which it is *not* in a DPI-unaware process, where Windows virtualises it — so
    /// the question cannot be answered in the test host. This probe borrows SETUNA's manifest
    /// and compares what WinForms reports against the monitor snapshots the application's own
    /// boundary layer builds from <c>GetMonitorInfo</c> + <c>GetDpiForMonitor</c>.
    /// </para>
    /// <para>
    /// It also checks the two things that follow from it: a capture of a monitor-sized
    /// rectangle produces a bitmap of exactly that many pixels, and selecting a monitor for a
    /// rectangle that straddles two of them picks the one it overlaps most rather than an
    /// arbitrary one.
    /// </para>
    /// </summary>
    internal static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

            if (Application.HighDpiMode != HighDpiMode.PerMonitorV2)
            {
                Console.WriteLine("FAIL: the probe process reports " + Application.HighDpiMode
                    + ", so screen bounds are virtualised. Check app.manifest.");
                return 2;
            }

            Console.WriteLine("HighDpiMode: " + Application.HighDpiMode);

            // Where to write the renders of each surface either side of a transition, for the
            // manual matrix to look at. Optional: the byte comparison runs either way.
            screenshotDirectory = args.Length > 0 ? args[0] : null;
            if (screenshotDirectory != null)
            {
                Console.WriteLine("Screenshots: " + screenshotDirectory);
            }

            var findings = new List<string>();

            findings.AddRange(ScreenBoundsMatchTheSnapshots());
            findings.AddRange(ACaptureIsAsManyPixelsAsItAsksFor());
            findings.AddRange(ARectanglePicksTheMonitorItOverlapsMost());
            findings.AddRange(ADpiChangeLeavesAPhysicalSurfaceAlone());
            findings.AddRange(ADpiChangeLeavesACapturedRectangleAlone());

            Console.WriteLine();
            if (findings.Count > 0)
            {
                Console.WriteLine("FAIL: " + findings.Count + " findings.");
                foreach (var finding in findings)
                {
                    Console.WriteLine("  " + finding);
                }

                return 1;
            }

            Console.WriteLine("PASS: WinForms screen bounds are the monitors' physical pixels, a"
                + " capture is exactly as many pixels as it asks for, a straddling rectangle"
                + " picks the monitor it overlaps most, and a DPI change alters neither a physical"
                + " surface's pixels nor a captured rectangle's.");

            return 0;
        }

        static string screenshotDirectory;

        /// <summary>
        /// <c>Screen.Bounds</c> against <c>MonitorSnapshot.NativeBounds</c>. Everything in the
        /// capture and scrap code is written in the former; the boundary layer answers in the
        /// latter. If they disagree, one of the two is not device pixels and every
        /// <c>BitBlt</c> source rectangle in the project is wrong on some monitor.
        /// </summary>
        static IEnumerable<string> ScreenBoundsMatchTheSnapshots()
        {
            Console.WriteLine();
            Console.WriteLine("--- monitor geometry ---");

            var snapshots = new Dictionary<string, MonitorSnapshot>(StringComparer.Ordinal);
            foreach (var snapshot in WindowsAPI.EnumerateMonitorSnapshots())
            {
                if (snapshot.IsAvailable)
                {
                    snapshots[snapshot.DeviceName] = snapshot;
                }
            }

            foreach (var screen in Screen.AllScreens)
            {
                MonitorSnapshot snapshot;
                if (!snapshots.TryGetValue(screen.DeviceName, out snapshot))
                {
                    Console.WriteLine("  " + screen.DeviceName + " " + screen.Bounds + " (no snapshot)");
                    yield return screen.DeviceName + " has no available monitor snapshot, so no"
                        + " capture rectangle on it can be converted";
                    continue;
                }

                Console.WriteLine("  " + screen.DeviceName + " @" + snapshot.DpiX
                    + ": Screen.Bounds=" + screen.Bounds + " NativeBounds=" + snapshot.NativeBounds
                    + " Screen.WorkingArea=" + screen.WorkingArea + " snapshot.WorkingArea=" + snapshot.WorkingArea
                    + " primary=" + screen.Primary + "/" + snapshot.IsPrimary);

                if (screen.Bounds != snapshot.NativeBounds)
                {
                    yield return screen.DeviceName + ": Screen.Bounds " + screen.Bounds
                        + " is not the monitor's physical " + snapshot.NativeBounds;
                }

                if (screen.WorkingArea != snapshot.WorkingArea)
                {
                    yield return screen.DeviceName + ": Screen.WorkingArea " + screen.WorkingArea
                        + " is not the monitor's physical " + snapshot.WorkingArea;
                }

                if (screen.Primary != snapshot.IsPrimary)
                {
                    yield return screen.DeviceName + ": primary flag disagrees";
                }
            }
        }

        /// <summary>
        /// The capture path allocates a bitmap of <c>Screen.Bounds</c> size and blits from the
        /// screen origin of that monitor. The bitmap must come back with exactly that many
        /// pixels on every monitor, whatever its scale factor — that is the whole meaning of
        /// "the capture API receives exactly the physical rectangle".
        /// <para>
        /// Only the dimensions are checked, not the content: a monitor that is asleep answers
        /// a successful <c>BitBlt</c> with black, which is indistinguishable from a black
        /// desktop and is not this probe's question.
        /// </para>
        /// </summary>
        static IEnumerable<string> ACaptureIsAsManyPixelsAsItAsksFor()
        {
            Console.WriteLine();
            Console.WriteLine("--- capture dimensions ---");

            foreach (var screen in Screen.AllScreens)
            {
                var bounds = screen.Bounds;
                using (var captured = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb))
                {
                    var ok = CaptureForm.CopyFromScreen(captured, bounds.Location);

                    Console.WriteLine("  " + screen.DeviceName + ": asked " + bounds.Width + "x" + bounds.Height
                        + " at " + bounds.Location + ", got " + captured.Width + "x" + captured.Height
                        + " (copy " + (ok ? "ok" : "failed") + ")");

                    if (!ok)
                    {
                        yield return screen.DeviceName + ": the screen copy failed outright";
                        continue;
                    }

                    if (captured.Width != bounds.Width || captured.Height != bounds.Height)
                    {
                        yield return screen.DeviceName + ": captured " + captured.Width + "x" + captured.Height
                            + " for a " + bounds.Width + "x" + bounds.Height + " physical rectangle";
                    }
                }
            }
        }

        /// <summary>
        /// A rectangle that straddles two monitors belongs to the one it overlaps most. Picking
        /// the nearest instead (what <c>MONITOR_DEFAULTTONEAREST</c> does, and what taking the
        /// first intersecting screen amounts to) gives an answer that depends on enumeration
        /// order, which is how a capture ends up sized for the wrong scale factor.
        /// <para>
        /// Needs two monitors; with one attached there is nothing to straddle and the check is
        /// skipped rather than claimed.
        /// </para>
        /// </summary>
        static IEnumerable<string> ARectanglePicksTheMonitorItOverlapsMost()
        {
            Console.WriteLine();
            Console.WriteLine("--- straddling rectangle ---");

            var monitors = new List<MonitorSnapshot>();
            foreach (var snapshot in WindowsAPI.EnumerateMonitorSnapshots())
            {
                if (snapshot.IsAvailable)
                {
                    monitors.Add(snapshot);
                }
            }

            if (monitors.Count < 2)
            {
                Console.WriteLine("  skipped: only one monitor attached");
                yield break;
            }

            foreach (var mostly in monitors)
            {
                var other = monitors[0].Handle == mostly.Handle ? monitors[1] : monitors[0];

                // Nine tenths on `mostly`, a sliver reaching towards `other`.
                var width = Math.Max(2, mostly.NativeBounds.Width / 10);
                var straddling = new Rectangle(
                    mostly.NativeBounds.Left < other.NativeBounds.Left
                        ? mostly.NativeBounds.Right - width * 9 / 10
                        : mostly.NativeBounds.Left - width / 10,
                    mostly.NativeBounds.Top + mostly.NativeBounds.Height / 2,
                    width,
                    2);

                var picked = WindowsAPI.GetMonitorSnapshotFor(straddling);

                Console.WriteLine("  " + straddling + " → " + picked.DeviceName
                    + " (mostly on " + mostly.DeviceName + ")");

                if (picked.DeviceName != mostly.DeviceName)
                {
                    yield return straddling + " was assigned to " + picked.DeviceName
                        + " but lies mostly on " + mostly.DeviceName;
                }
            }
        }
        /// <summary>
        /// A physical surface must come out of a DPI change exactly as it went in.
        /// <para>
        /// <c>AutoScaleMode.None</c> keeps WinForms from scaling the control tree, but it does
        /// not by itself keep the *window* out of the transition: <c>WM_DPICHANGED</c> carries a
        /// suggested rectangle, and adopting it would resize a scrap by the scale factor —
        /// stretching the image the user captured. So the check is on the window and its
        /// content together: outer bounds, client area, and the bitmap's own pixel dimensions.
        /// </para>
        /// <para>
        /// A logical dialog runs through the same synthetic message as a control group. Without
        /// it "nothing moved" cannot be told apart from "the message did nothing", and every
        /// assertion here would pass on a broken pipeline.
        /// </para>
        /// </summary>
        static IEnumerable<string> ADpiChangeLeavesAPhysicalSurfaceAlone()
        {
            Console.WriteLine();
            Console.WriteLine("--- a DPI change across a physical surface ---");

            var control = Transition(new SETUNA.Main.StyleItems.ToolBoxForm(), "ToolBoxForm (control group)");
            if (control.Before == control.After)
            {
                yield return "the control group ToolBoxForm did not move at all (" + control.Before
                    + "), so the synthetic WM_DPICHANGED is not reaching the pipeline and nothing"
                    + " below this line proves anything";
            }

            if (control.PixelsMatch)
            {
                yield return "the control group ToolBoxForm rendered identical pixels either side of"
                    + " the transition, so the pixel comparison below cannot distinguish a surface"
                    + " that held still from one that was never asked to move";
            }

            foreach (var surface in PhysicalSurfaces())
            {
                var measured = Transition(surface.Value, surface.Key);

                if (measured.Before != measured.After)
                {
                    yield return surface.Key + " changed from " + measured.Before + " to "
                        + measured.After + " across a DPI change";
                }

                // The measurements above are sizes; this is the rendered surface itself. A
                // window can keep its rectangle and still resample the bitmap into it —
                // ScrapBase paints through HighQualityBicubic — so the two fail independently.
                if (!measured.PixelsMatch)
                {
                    yield return surface.Key + ": " + measured.PixelDifference;
                }
            }
        }

        /// <summary>
        /// A capture of a fixed physical rectangle must be unaffected by a DPI transition
        /// somewhere else in the process.
        /// <para>
        /// The capture path allocates a <c>Screen.Bounds</c>-sized bitmap and blits from the
        /// monitor's origin, and neither of those is supposed to move when a window's DPI
        /// changes. Comparing real captured bytes is the only way to see it end to end — but
        /// the desktop is a live image, so the check first captures the same rectangle twice
        /// and reports itself inconclusive if those already differ, rather than blaming a
        /// clock tick on the DPI change.
        /// </para>
        /// </summary>
        static IEnumerable<string> ADpiChangeLeavesACapturedRectangleAlone()
        {
            Console.WriteLine();
            Console.WriteLine("--- a captured rectangle across a DPI change ---");

            var screen = Screen.PrimaryScreen;
            var region = new Rectangle(
                screen.Bounds.Left + screen.Bounds.Width / 4,
                screen.Bounds.Top + screen.Bounds.Height / 4,
                Math.Min(320, screen.Bounds.Width / 2),
                Math.Min(240, screen.Bounds.Height / 2));

            var first = CaptureRegion(region);
            var control = CaptureRegion(region);

            if (first == null || control == null)
            {
                Console.WriteLine("  inconclusive: the screen copy failed (a sleeping or locked"
                    + " monitor answers a successful BitBlt with black)");
                yield break;
            }

            if (!Same(first, control))
            {
                Console.WriteLine("  inconclusive: " + region + " changed between two consecutive"
                    + " captures, so the desktop is not static enough to attribute a difference to"
                    + " the DPI change");
                yield break;
            }

            using (var surface = new SETUNA.Main.Magnifier())
            {
                surface.StartPosition = FormStartPosition.Manual;
                surface.Location = new Point(-30000, -30000);
                surface.ShowInTaskbar = false;
                surface.Show();
                Application.DoEvents();
                surface.Hide();

                var born = surface.CurrentDpiContext.DpiX;
                var target = born == 96 ? 168 : 96;

                SendDpiChanged(surface, target, born);
                SendDpiChanged(surface, born, target);
            }

            var after = CaptureRegion(region);
            if (after == null)
            {
                Console.WriteLine("  inconclusive: the screen copy failed after the transition");
                yield break;
            }

            Console.WriteLine("  " + region + ": " + first.Length + " bytes, "
                + (Same(first, after) ? "identical after the transition" : "CHANGED"));

            if (!Same(first, after))
            {
                yield return region + " captured different bytes after a DPI transition, so the"
                    + " capture path's geometry followed the window's DPI";
            }
        }

        static byte[] CaptureRegion(Rectangle region)
        {
            using (var captured = new Bitmap(region.Width, region.Height, PixelFormat.Format24bppRgb))
            {
                if (!CaptureForm.CopyFromScreen(captured, region.Location))
                {
                    return null;
                }

                return Pixels(captured);
            }
        }

        static bool Same(byte[] left, byte[] right)
        {
            if (left.Length != right.Length)
            {
                return false;
            }

            for (var i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                {
                    return false;
                }
            }

            return true;
        }

        static IEnumerable<KeyValuePair<string, BaseForm>> PhysicalSurfaces()
        {
            var scrap = new SETUNA.Main.ScrapBase();
            using (var image = new Bitmap(137, 89, PixelFormat.Format24bppRgb))
            {
                scrap.Image = image;
            }

            yield return new KeyValuePair<string, BaseForm>("ScrapBase", scrap);
            yield return new KeyValuePair<string, BaseForm>("Magnifier", new SETUNA.Main.Magnifier());
            yield return new KeyValuePair<string, BaseForm>("CaptureInfo", new SETUNA.Main.CaptureInfo());
            yield return new KeyValuePair<string, BaseForm>("CaptureSelLine", new SETUNA.Main.CaptureSelLine());
        }

        /// <summary>
        /// Shows <paramref name="form"/> off screen, sends it the message the OS sends when a
        /// window's DPI changes, and reports its measurable state either side.
        /// </summary>
        static Measured Transition(BaseForm form, string name)
        {
            using (form)
            {
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-30000, -30000);
                form.ShowInTaskbar = false;
                form.Show();
                Application.DoEvents();
                form.Hide();

                var born = form.CurrentDpiContext.DpiX;
                var target = born == 96 ? 168 : 96;
                var before = Describe(form);
                var beforePixels = Render(form, name, born);

                SendDpiChanged(form, target, born);

                var after = Describe(form);
                var afterPixels = Render(form, name, target);

                Console.WriteLine("  " + name + " " + born + "→" + target + " DPI: " + before
                    + (before == after ? " (unchanged)" : " became " + after));

                return new Measured
                {
                    Before = before,
                    After = after,
                    PixelsMatch = beforePixels != null && afterPixels != null && Same(beforePixels, afterPixels),
                    PixelDifference = DescribeDifference(beforePixels, afterPixels)
                };
            }
        }

        /// <summary>
        /// The message the OS sends when a window's DPI changes. The suggested rectangle is the
        /// scaled client area plus the frame the window <em>currently</em> has; see
        /// probes/DialogRelayoutProbe for why the frame must be the current one and not the
        /// target DPI's.
        /// </summary>
        static void SendDpiChanged(Form form, int target, int born)
        {
            const int WM_DPICHANGED = 0x02E0;

            var ratio = (double)target / Math.Max(1, born);
            var frame = form.Size - form.ClientSize;
            var suggested = new Rectangle(
                form.Left,
                form.Top,
                DpiContext.Scale(form.ClientSize.Width, ratio) + frame.Width,
                DpiContext.Scale(form.ClientSize.Height, ratio) + frame.Height);

            var buffer = Marshal.AllocHGlobal(16);
            try
            {
                Marshal.WriteInt32(buffer, 0, suggested.Left);
                Marshal.WriteInt32(buffer, 4, suggested.Top);
                Marshal.WriteInt32(buffer, 8, suggested.Right);
                Marshal.WriteInt32(buffer, 12, suggested.Bottom);

                WindowsAPI.SendMessage(
                    form.Handle,
                    WM_DPICHANGED,
                    new IntPtr((target << 16) | target),
                    buffer);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }

            Application.DoEvents();
        }

        /// <summary>
        /// The surface as it is actually drawn, optionally saved for the manual matrix.
        /// Returns null for an empty client area, which the size comparison already reports.
        /// </summary>
        static byte[] Render(BaseForm form, string name, int dpi)
        {
            var size = form.ClientSize;
            if (size.Width <= 0 || size.Height <= 0)
            {
                return null;
            }

            using (var rendered = new Bitmap(size.Width, size.Height, PixelFormat.Format24bppRgb))
            {
                form.DrawToBitmap(rendered, new Rectangle(Point.Empty, size));

                if (!string.IsNullOrEmpty(screenshotDirectory))
                {
                    System.IO.Directory.CreateDirectory(screenshotDirectory);
                    rendered.Save(
                        System.IO.Path.Combine(
                            screenshotDirectory,
                            name.Replace(' ', '-').Replace("(", string.Empty).Replace(")", string.Empty)
                                + "-" + dpi.ToString("000") + ".png"),
                        System.Drawing.Imaging.ImageFormat.Png);
                }

                return Pixels(rendered);
            }
        }

        /// <summary>The raw bytes, dimensions first so a size change reads as a difference.</summary>
        static byte[] Pixels(Bitmap bitmap)
        {
            var data = bitmap.LockBits(
                new Rectangle(Point.Empty, bitmap.Size), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            try
            {
                var stride = Math.Abs(data.Stride);
                var bytes = new byte[8 + stride * data.Height];
                BitConverter.GetBytes(bitmap.Width).CopyTo(bytes, 0);
                BitConverter.GetBytes(bitmap.Height).CopyTo(bytes, 4);

                for (var row = 0; row < data.Height; row++)
                {
                    Marshal.Copy(data.Scan0 + row * data.Stride, bytes, 8 + row * stride, stride);
                }

                return bytes;
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        static string DescribeDifference(byte[] before, byte[] after)
        {
            if (before == null || after == null)
            {
                return "the client area was empty, so nothing could be rendered";
            }

            if (before.Length != after.Length)
            {
                return "the rendered surface went from " + Dimensions(before) + " to " + Dimensions(after);
            }

            var differing = 0;
            for (var i = 8; i < before.Length; i++)
            {
                if (before[i] != after[i])
                {
                    differing++;
                }
            }

            return differing == 0
                ? "identical"
                : differing + " of " + (before.Length - 8) + " bytes of the " + Dimensions(before)
                    + " render changed across the DPI transition";
        }

        static string Dimensions(byte[] pixels)
        {
            return BitConverter.ToInt32(pixels, 0) + "x" + BitConverter.ToInt32(pixels, 4);
        }

        /// <summary>
        /// Everything a DPI change must not touch on a physical surface, as one string so a
        /// difference reads as a difference rather than as five comparisons.
        /// </summary>
        static string Describe(BaseForm form)
        {
            var scrap = form as SETUNA.Main.ScrapBase;
            var image = scrap != null && scrap.Image != null
                ? " image=" + scrap.Image.Width + "x" + scrap.Image.Height + " scale=" + scrap.Scale
                : string.Empty;

            return "size=" + form.Size.Width + "x" + form.Size.Height
                + " client=" + form.ClientSize.Width + "x" + form.ClientSize.Height
                + " padding=" + form.Padding.All
                + image;
        }

        struct Measured
        {
            public string Before;
            public string After;
            public bool PixelsMatch;
            public string PixelDifference;
        }
    }
}
