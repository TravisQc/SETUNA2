using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SETUNA.Main.Window;

namespace DialogRelayoutProbe
{
    /// <summary>
    /// Does a logical dialog really follow the monitor DPI under the framework pipeline?
    /// <para>
    /// The unit suite cannot answer this. WinForms only scales a control tree on
    /// <c>WM_DPICHANGED</c> when the process is per-monitor-v2 aware, and the test host has
    /// no application manifest — there, fonts change and control bounds do not, which reads
    /// as a relayout bug in every dialog at once. This probe links SETUNA's manifest, so the
    /// gate is open and the numbers are the ones SETUNA gets.
    /// </para>
    /// <para>
    /// Transitions are synthetic <c>WM_DPICHANGED</c> messages, which is what makes the run
    /// deterministic and independent of how many monitors are attached. Dragging a real
    /// window between real monitors stays with the manual matrix (task 10.2).
    /// </para>
    /// </summary>
    internal static class Program
    {
        const int WM_DPICHANGED = 0x02E0;

        /// <summary>The scale steps the support matrix lists, minus whatever the probe was born on.</summary>
        static readonly int[] Targets = { 96, 120, 144, 168, 192 };

        /// <summary>
        /// Bounds are scaled per control and rounded independently, so a round trip through a
        /// non-integer ratio can land a pixel off. More than that is drift that accumulates.
        /// </summary>
        const int RoundTripSlop = 1;

        /// <summary>
        /// The same allowance for a control whose position a flow or table panel computes.
        /// <see cref="RoundTripSlop"/>'s single rounding is the right budget for a control the
        /// framework scales directly; a flow panel child's position is instead a running total
        /// of every preceding sibling's rounded size and margin, so it rounds several times per
        /// hop. Measured on <c>OptionForm</c>'s <c>flowLayoutPanel1</c>: <c>panel4</c> sits at
        /// Y=150 at 96 DPI and comes back from 120 DPI at Y=152, while a round trip through
        /// 96/144/192 returns exactly. This only became visible when the designer geometry was
        /// rebased to the 96-DPI baseline — at twice the coordinates the same accumulation had
        /// twice the headroom before it crossed a pixel.
        /// </summary>
        const int LayoutOwnedRoundTripSlop = 3;

        /// <summary>
        /// How far a scaled bound may sit from the exact ratio. Each control is rounded once
        /// on the way out, and a container's rounding shifts its children.
        /// </summary>
        const int ScaleSlop = 2;

        /// <summary>
        /// The same, for a comparison made across the whole 96→192 ladder rather than one hop.
        /// <para>
        /// WinForms scales each transition from the size the control currently has, not from
        /// the designer's, so a five-step walk rounds every edge five times and the product
        /// drifts from the direct ratio. Measured on <c>OptionForm</c>: <c>detailPanel</c>
        /// reaches 192 DPI at 2242x1782 where a single hop from 96 would give 2244x1788, and a
        /// container's own drift shifts its children again. That is a property of the
        /// framework's per-hop scaling, not a defect the application can fix, so the ladder
        /// allows for it — and checks instead the things that must not drift at all: overflow,
        /// overlap, text fit, font size and the suggested placement. Per-hop bound fidelity is
        /// <see cref="Check"/>'s job, at <see cref="ScaleSlop"/>.
        /// </para>
        /// </summary>
        const int LadderSlop = 3;

        /// <summary>Control path suffix to trace in detail, from DIALOG_PROBE_TRACE_CONTROL.</summary>
        static readonly string TraceControl = Environment.GetEnvironmentVariable("DIALOG_PROBE_TRACE_CONTROL");

        [STAThread]
        static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

            if (Application.HighDpiMode != HighDpiMode.PerMonitorV2)
            {
                Console.WriteLine("FAIL: the probe process reports " + Application.HighDpiMode
                    + ", so WinForms will not scale anything. Check app.manifest.");
                return 2;
            }

            Console.WriteLine("HighDpiMode: " + Application.HighDpiMode);

            // Where to write the per-DPI renders for a human to look at. Optional: the ink
            // comparison runs either way, only the PNGs are skipped.
            var screenshots = args.Length > 0 ? args[0] : null;
            if (screenshots != null)
            {
                Console.WriteLine("Screenshots: " + screenshots);
            }

            var failures = new List<string>();
            var buildFailures = new List<string>();
            var transitions = 0;
            var dialogs = 0;

            OwnerDrawText.ReportOnEveryMonitor(() =>
                new SETUNA.Main.Option.StyleEditForm(null, new SETUNA.Main.KeyItems.KeyItemBook()));

            OwnerDrawText.CheckHelpFontProportion(
                () => new SETUNA.Main.Option.StyleEditForm(null, new SETUNA.Main.KeyItems.KeyItemBook()),
                failures);

            OwnerDrawText.ReportExplicitFonts(() => new SETUNA.Main.HotkeyMsg());

            // 语言 × DPI 一起扫：文字长度和缩放倍率各自都能把控件撑破，但真正会漏掉的是
            // 两者叠加的那一格（英文译文 + 100% 缩放），单独扫任何一维都看不到。
            foreach (var language in new[] { "zh-CN", "en" })
            {
                SetLanguage(language);
                Console.WriteLine();
                Console.WriteLine("=== " + language + " ===");

                foreach (var form in Dialogs.All(buildFailures))
                {
                    using (form)
                    {
                        var name = form.GetType().Name;
                        ShowOffScreen(form);
                        var born = form.CurrentDpiContext.DpiX;
                        dialogs++;

                        Console.WriteLine(name + ": born at " + born + " DPI, client " + form.ClientSize);

                        // A dialog with no client area in one direction has nothing left to
                        // measure, so every comparison below it passes vacuously. Three
                        // dialogs sat at height 0 for exactly that reason: their designer
                        // assigned ControlBox before Text, so the size was recorded against a
                        // caption-less frame and the scale factor multiplied the error
                        // (BaseForm.HideControlBoxAfterInitialize).
                        if (form.ClientSize.Width <= 0 || form.ClientSize.Height <= 0)
                        {
                            failures.Add(language + " " + name + ": client area " + form.ClientSize
                                + " has no extent, so nothing about its layout can be measured");
                            continue;
                        }

                        foreach (var target in Targets)
                        {
                            if (target == born || !FitsOnThisDesktop(form, target, born))
                            {
                                continue;
                            }

                            transitions++;
                            Check(form, language + " " + name, born, target, failures);
                        }

                        CheckLadderAgainstLogicalBaseline(
                            form, language + " " + name, born, failures, screenshots);
                    }
                }
            }

            SetLanguage("zh-CN");

            Console.WriteLine();
            Console.WriteLine(dialogs + " dialogs, " + transitions + " transitions.");
            Console.WriteLine(Screenshot.Rendered + " renders, ink coverage "
                + Screenshot.DescribeCoverageRange() + ".");

            failures.AddRange(buildFailures);

            if (transitions < 20)
            {
                Console.WriteLine("FAIL: only " + transitions + " transitions ran; the sweep proves nothing.");
                return 3;
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

            Console.WriteLine("PASS: in every language, each dialog scaled with the monitor, came back"
                + " unchanged, and clipped no caption.");

            return 0;
        }

        /// <summary>
        /// <c>Lang</c> and <c>AppLanguage</c> are internal to the application assembly, and a
        /// probe is not worth widening that for. The string overload takes the value the
        /// config file stores, so no enum type is needed either.
        /// </summary>
        static void SetLanguage(string configValue)
        {
            var lang = typeof(BaseForm).Assembly.GetType("SETUNA.Main.Localization.Lang", true);
            lang.GetMethod("SetLanguage", new[] { typeof(string) }).Invoke(null, new object[] { configValue });
            Application.DoEvents();
        }

        /// <summary>
        /// One transition and its return trip: the layout must scale on the way out, gain no
        /// overflow or overlap there, and land back exactly where it started.
        /// </summary>
        static void Check(BaseForm form, string name, int born, int target, List<string> failures)
        {
            var baseline = Measure(form);
            var baselineLayout = new Layout();
            baselineLayout.Capture(form, name);
            var baselineText = new TextFit();
            baselineText.Capture(form, name, born);
            var baselineClient = form.ClientSize;

            OwnerDrawText.Report(form, name + " @" + born + " (born)");

            SendDpiChanged(form, target, born);

            var scaled = Measure(form);
            OwnerDrawText.Report(form, name + " @" + target);
            var ratio = (double)target / born;
            CheckClientArea(form, name, baselineClient, ratio, target, ScaleSlop, failures);
            CompareScaled(baseline, scaled, ratio, "@" + target, failures);

            var scaledLayout = new Layout();
            scaledLayout.Capture(form, name);
            foreach (var regression in scaledLayout.RegressionsAgainst(baselineLayout, ratio))
            {
                failures.Add(name + " @" + target + ": " + regression);
            }

            var scaledText = new TextFit();
            scaledText.Capture(form, name, target);
            foreach (var clipped in scaledText.NewlyClipped(baselineText))
            {
                failures.Add(name + " @" + target + ": " + clipped);
            }

            SendDpiChanged(form, born, target);

            CompareReturned(
                baseline,
                Measure(form),
                scaled,
                "the " + target + " DPI round trip",
                born,
                target,
                RoundTripSlop,
                failures);
        }

        /// <summary>
        /// The client area must not come out of a transition smaller than the ratio asks for;
        /// a window clamped by the desktop or by its own maximum size would silently invalidate
        /// every control comparison below it.
        /// </summary>
        static void CheckClientArea(
            Form form, string name, Size baselineClient, double ratio, int target, int slop, List<string> failures)
        {
            if (DpiContext.Scale(baselineClient.Width, ratio) - form.ClientSize.Width > slop
                || DpiContext.Scale(baselineClient.Height, ratio) - form.ClientSize.Height > slop)
            {
                failures.Add(name + " @" + target + ": client area " + form.ClientSize
                    + " is smaller than " + baselineClient + " scaled by " + ratio.ToString("F2"));
            }
        }

        /// <summary>
        /// Every control is still there, and its font is the reference font times
        /// <paramref name="ratio"/>.
        /// <para>
        /// Separated from the bounds comparison because a font does not accumulate error the
        /// way a rectangle does: WinForms scales the point size, a float, so a chain of
        /// transitions multiplies exactly, while every rectangle is rounded to whole pixels at
        /// each step.
        /// </para>
        /// </summary>
        static void ComparePresenceAndFonts(
            Dictionary<string, Reading> reference,
            Dictionary<string, Reading> actual,
            double ratio,
            string at,
            List<string> failures)
        {
            foreach (var pair in reference)
            {
                Reading after;
                if (!actual.TryGetValue(pair.Key, out after))
                {
                    failures.Add(pair.Key + " " + at + " disappeared from the control tree");
                    continue;
                }

                var expectedFont = DpiContext.Scale(pair.Value.FontHeight, ratio);
                if (Math.Abs(after.FontHeight - expectedFont) > 1)
                {
                    failures.Add(pair.Key + " " + at + ": font is " + after.FontHeight
                        + "px, expected about " + expectedFont + "px");
                }
            }
        }

        /// <summary>
        /// Every control's font and designer-owned bounds against the reference multiplied by
        /// <paramref name="ratio"/>. Only valid for a single transition; see
        /// <see cref="CheckLadderAgainstLogicalBaseline"/> for why a chain of them is not.
        /// </summary>
        static void CompareScaled(
            Dictionary<string, Reading> reference,
            Dictionary<string, Reading> actual,
            double ratio,
            string at,
            List<string> failures)
        {
            ComparePresenceAndFonts(reference, actual, ratio, at, failures);

            foreach (var pair in reference)
            {
                Reading after;
                if (!actual.TryGetValue(pair.Key, out after))
                {
                    continue;
                }

                // An AutoSize control's box follows its text, not the ratio, so only the
                // controls the designer sized are checked against the ratio itself.
                if (!pair.Value.AutoSize
                    && !ScaledWithin(pair.Value.Bounds, after.Bounds, ratio, pair.Value.HeightFollowsFont))
                {
                    failures.Add(pair.Key + " " + at + ": " + pair.Value.Bounds + " became "
                        + after.Bounds + ", not " + ratio.ToString("F2") + "x");
                }

                foreach (var metric in MetricChanges(pair.Value, after, ratio, pair.Key, at))
                {
                    failures.Add(metric);
                }
            }
        }

        /// <summary>
        /// Every control back at the DPI the reference was taken on. Deliberately stricter than
        /// <see cref="CompareScaled"/> — the ratio is 1, so anything but the original numbers is
        /// drift.
        /// </summary>
        static void CompareReturned(
            Dictionary<string, Reading> reference,
            Dictionary<string, Reading> returned,
            Dictionary<string, Reading> mid,
            string via,
            int born,
            int target,
            int slop,
            List<string> failures)
        {
            foreach (var pair in reference)
            {
                Reading back;
                if (!returned.TryGetValue(pair.Key, out back))
                {
                    failures.Add(pair.Key + " disappeared after " + via);
                    continue;
                }

                if (pair.Value.Describe != null)
                {
                    Console.WriteLine("  [trace] " + pair.Key + " @" + born + "→" + target + "→" + born);
                    Console.WriteLine("  [trace]   was      " + pair.Value.Describe);
                    Console.WriteLine("  [trace]   scaled   "
                        + (mid != null && mid.TryGetValue(pair.Key, out var seen) ? seen.Describe : "(gone)"));
                    Console.WriteLine("  [trace]   returned " + back.Describe);
                }

                // An AutoSize control does not owe the designer its size — only its position.
                // Measured on OptionForm: chkShowMainWindow is born 250 wide at 168 DPI while
                // its text needs 143, because construction scales the designer bounds without
                // ever re-running the AutoSize measurement; a DPI round trip is what finally
                // snaps it to 143. Comparing its size to the baseline would report that
                // convergence as a regression. A font-driven height is excluded for the same
                // reason: an IntegralHeight list box lands on a whole number of rows, so
                // listKeyItems comes back 368 tall instead of 372.
                bool boundsMatch;
                if (pair.Value.AutoSize)
                {
                    boundsMatch = WithinSlop(
                        pair.Value.Bounds.Location,
                        back.Bounds.Location,
                        pair.Value.LayoutOwned ? LayoutOwnedRoundTripSlop : slop);
                }
                else if (pair.Value.HeightFollowsFont)
                {
                    boundsMatch = WithinSlop(pair.Value.Bounds.Location, back.Bounds.Location, slop)
                        && Math.Abs(pair.Value.Bounds.Width - back.Bounds.Width) <= slop;
                }
                else
                {
                    boundsMatch = WithinSlop(pair.Value.Bounds, back.Bounds, slop);
                }

                if (!boundsMatch)
                {
                    failures.Add(pair.Key + " came back as " + back.Bounds + " instead of "
                        + pair.Value.Bounds + " (via " + via + ")");
                }

                if (back.FontHeight != pair.Value.FontHeight)
                {
                    failures.Add(pair.Key + " came back with a " + back.FontHeight + "px font instead of "
                        + pair.Value.FontHeight + "px (via " + via + ")");
                }

                foreach (var metric in MetricChanges(pair.Value, back, 1d, pair.Key, "after " + via))
                {
                    failures.Add(metric);
                }
            }
        }

        /// <summary>
        /// The whole scale ladder against one fixed 96-DPI reference — the baseline every
        /// designer declares — rather than against the DPI the dialog happened to be born on.
        /// <para>
        /// Three things this catches that a single hop from the born DPI does not. Error
        /// accumulates: 96→120→144→168→192 is four roundings deep by the top, and a per-hop
        /// check accepts each of them while the total drifts. The reference is the designer's
        /// own numbers, so a failure names the coordinate a developer can read in the
        /// designer. And the ladder is the only place the *suggested placement* can be
        /// checked, because it is the framework's own answer at each step: for a logical
        /// dialog the window must end up exactly at the rectangle Windows suggested, and a
        /// window that quietly kept its old size would still satisfy a ratio comparison of
        /// its controls against each other.
        /// </para>
        /// <para>
        /// Screenshots come from the same pass. They are written for a human to look at, and
        /// the one property comparable across DPI — how much of the client area carries ink —
        /// is asserted; see <see cref="Screenshot"/>.
        /// </para>
        /// </summary>
        static void CheckLadderAgainstLogicalBaseline(
            BaseForm form, string name, int born, List<string> failures, string screenshots)
        {
            const int BaselineDpi = 96;

            var current = born;
            if (current != BaselineDpi)
            {
                SendDpiChanged(form, BaselineDpi, current);
                current = BaselineDpi;
            }

            var reference = Measure(form);
            var referenceLayout = new Layout();
            referenceLayout.Capture(form, name);
            var referenceText = new TextFit();
            referenceText.Capture(form, name, BaselineDpi);
            var referenceClient = form.ClientSize;
            var referenceShot = Capture(form, name, BaselineDpi, screenshots);

            foreach (var finding in referenceShot.RegressionsAgainst(referenceShot, BaselineDpi))
            {
                failures.Add(name + ": " + finding);
            }

            var reached = new List<int>();

            foreach (var target in Targets)
            {
                if (target == BaselineDpi || !FitsOnThisDesktop(form, target, current))
                {
                    continue;
                }

                var suggested = SendDpiChanged(form, target, current);
                current = target;
                reached.Add(target);

                if (form.Bounds != suggested)
                {
                    failures.Add(name + " @" + target + ": the window is " + form.Bounds
                        + " but Windows suggested " + suggested
                        + ", so a logical dialog did not take the suggested placement");
                }

                var ratio = (double)target / BaselineDpi;
                CheckClientArea(form, name + " (from 96)", referenceClient, ratio, target, LadderSlop, failures);
                ComparePresenceAndFonts(
                    reference, Measure(form), ratio, "@" + target + " from the 96 DPI baseline", failures);

                var ladderLayout = new Layout();
                ladderLayout.Capture(form, name);
                foreach (var regression in ladderLayout.RegressionsAgainst(referenceLayout, ratio))
                {
                    failures.Add(name + " @" + target + " from the 96 DPI baseline: " + regression);
                }

                var ladderText = new TextFit();
                ladderText.Capture(form, name, target);
                foreach (var clipped in ladderText.NewlyClipped(referenceText))
                {
                    failures.Add(name + " @" + target + " from the 96 DPI baseline: " + clipped);
                }

                foreach (var finding in Capture(form, name, target, screenshots).RegressionsAgainst(referenceShot, target))
                {
                    failures.Add(name + ": " + finding);
                }
            }

            if (reached.Count == 0)
            {
                failures.Add(name + ": no scale step above 96 DPI fits on this desktop, so the"
                    + " ladder proved nothing");
                return;
            }

            SendDpiChanged(form, BaselineDpi, current);
            CompareReturned(
                reference,
                Measure(form),
                null,
                "the 96→" + string.Join("→", reached) + "→96 ladder",
                BaselineDpi,
                reached[reached.Count - 1],
                LadderSlop,
                failures);
        }

        static Screenshot Capture(Form form, string name, int dpi, string screenshots)
        {
            return Screenshot.Capture(form, screenshots, name.Replace(' ', '-') + "-" + dpi.ToString("000"));
        }

        static void DumpListMetrics(Control parent, string label)
        {
            foreach (Control child in parent.Controls)
            {
                if (child is ListBox list)
                {
                    var leftSpace = child.GetType().GetProperty("LeftSpace");
                    Console.WriteLine("[dbg] " + label + " " + child.Name + ": itemHeight=" + list.ItemHeight
                        + " leftSpace=" + (leftSpace == null ? "n/a" : leftSpace.GetValue(child).ToString())
                        + " font=" + list.Font.SizeInPoints.ToString("F2") + "pt/" + list.Font.Height + "px"
                        + " bounds=" + list.Bounds
                        + " preferredItem=" + list.PreferredHeight);
                }

                DumpListMetrics(child, label);
            }
        }

        /// <summary>
        /// Application-owned pixel metrics have to follow the same ratio the framework applied
        /// to the control's rectangle, and have to come back where they started.
        /// <para>
        /// They are checked apart from the bounds because nothing in the framework touches
        /// them: an owner-draw-fixed list box decides its own row height, so a row that does
        /// not grow with the monitor clips text inside a control whose rectangle scaled
        /// perfectly — which is exactly what every bounds comparison here would miss.
        /// </para>
        /// </summary>
        static IEnumerable<string> MetricChanges(Reading before, Reading after, double ratio, string path, string trip)
        {
            if (before.Metrics == null || after.Metrics == null)
            {
                yield break;
            }

            foreach (var metric in before.Metrics)
            {
                int now;
                if (!after.Metrics.TryGetValue(metric.Key, out now))
                {
                    continue;
                }

                var expected = DpiContext.Scale(metric.Value, ratio);
                if (Math.Abs(now - expected) > RoundTripSlop)
                {
                    yield return path + "." + metric.Key + " " + trip + ": " + now
                        + " instead of " + expected;
                }
            }
        }

        static bool ScaledWithin(Rectangle before, Rectangle after, double ratio, bool heightFollowsFont)
        {
            if (Math.Abs(DpiContext.Scale(before.Width, ratio) - after.Width) > ScaleSlop)
            {
                return false;
            }

            return heightFollowsFont
                || Math.Abs(DpiContext.Scale(before.Height, ratio) - after.Height) <= ScaleSlop;
        }

        static bool WithinSlop(Rectangle expected, Rectangle actual, int slop)
        {
            return WithinSlop(expected.Location, actual.Location, slop)
                && Math.Abs(expected.Width - actual.Width) <= slop
                && Math.Abs(expected.Height - actual.Height) <= slop;
        }

        static bool WithinSlop(Point expected, Point actual, int slop)
        {
            return Math.Abs(expected.X - actual.X) <= slop
                && Math.Abs(expected.Y - actual.Y) <= slop;
        }

        /// <summary>
        /// A window scaled past the desktop's maximum track size is clamped, and a clamped
        /// window neither scales nor comes back. Skipping keeps the verdict the same on a
        /// 1920x1080 machine and a 4K one.
        /// </summary>
        static bool FitsOnThisDesktop(Form form, int target, int born)
        {
            var scale = (double)target / born;
            var limit = SystemInformation.MaxWindowTrackSize;

            return form.Width * scale <= limit.Width && form.Height * scale <= limit.Height;
        }

        static Dictionary<string, Reading> Measure(BaseForm form)
        {
            LayOutEveryTabPage(form);

            var readings = new Dictionary<string, Reading>(StringComparer.Ordinal);
            Measure(form, form.GetType().Name, readings);

            return readings;
        }

        /// <summary>
        /// A tab page that has never been selected has never been laid out, so its controls
        /// report whatever size they were last given — which after a DPI transition is the
        /// old one. Selecting each page in turn forces the layout the user would trigger by
        /// clicking the tab, so the measurement describes what they will actually see.
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

        static void Measure(Control container, string path, Dictionary<string, Reading> into)
        {
            foreach (Control child in container.Controls)
            {
                // Controls the framework builds inside a composite (a NumericUpDown's editor
                // and spin button) have no name and no designer bounds, and they follow the
                // font rather than the DPI ratio. They are not this project's layout.
                if (string.IsNullOrEmpty(child.Name))
                {
                    continue;
                }

                var childPath = path + "/" + child.Name;
                into[childPath] = new Reading
                {
                    Bounds = child.Bounds,
                    FontHeight = child.Font.Height,
                    AutoSize = child.AutoSize || LayoutOwnedByParent(child),
                    LayoutOwned = LayoutOwnedByParent(child),
                    HeightFollowsFont = FollowsFontHeight(child),
                    Metrics = OwnedMetrics(child),
                    Describe = TraceControl != null && childPath.EndsWith(TraceControl, StringComparison.Ordinal)
                        ? child.Bounds + " preferred=" + child.PreferredSize
                            + " font=" + child.Font.SizeInPoints.ToString("F2") + "pt/" + child.Font.Height + "px"
                            + " autoSize=" + child.AutoSize + " text=\"" + child.Text + "\""
                        : null,
                };

                Measure(child, childPath, into);
            }
        }

        /// <summary>
        /// Pixel metrics this project owns rather than the framework. A list box that draws its
        /// own rows of a fixed height decides that height, so nothing scales it unless the
        /// control does; a <c>DrawMode.Normal</c> list box derives it from the font instead and
        /// is the framework's business (measured: <c>listKeyItems</c> is 17/20/24/28 px tall at
        /// 96/120/144/168 DPI without anyone asking).
        /// <para>
        /// <c>LeftSpace</c> needs reflection because <c>SetunaListBox</c> is internal to the
        /// application assembly. Its row padding and icon size are protected and out of reach,
        /// but they are scaled in the same <c>ScaleControl</c> pass by the same factor, so the
        /// two metrics visible here cover the mechanism.
        /// </para>
        /// </summary>
        static Dictionary<string, int> OwnedMetrics(Control control)
        {
            if (!(control is ListBox list) || list.DrawMode != DrawMode.OwnerDrawFixed)
            {
                return null;
            }

            var metrics = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["ItemHeight"] = list.ItemHeight,
            };

            var leftSpace = control.GetType().GetProperty("LeftSpace");
            if (leftSpace != null && leftSpace.PropertyType == typeof(int))
            {
                metrics["LeftSpace"] = (int)leftSpace.GetValue(control);
            }

            return metrics;
        }

        /// <summary>
        /// Controls whose rectangle a layout engine computes, not the designer: anything
        /// docked, a <c>TabPage</c> (the <c>TabControl</c> subtracts a font-driven tab strip
        /// from its own client area), and the children of a table or flow panel (space is
        /// distributed by percentage and rounded per cell). Holding these to the designer's
        /// ratio measures the layout engine's arithmetic, not this project's coordinates.
        /// </summary>
        static bool LayoutOwnedByParent(Control control)
        {
            return control.Dock != DockStyle.None
                || control is TabPage
                || control.Parent is TableLayoutPanel
                || control.Parent is FlowLayoutPanel;
        }

        /// <summary>
        /// Single-line editors, spin boxes and combo boxes size their own height from the
        /// font and ignore what the layout asks for, and a list box with
        /// <c>IntegralHeight</c> snaps its height to whole rows. Their height cannot be held
        /// to the DPI ratio; their width still can.
        /// </summary>
        static bool FollowsFontHeight(Control control)
        {
            return control is UpDownBase
                || control is ComboBox
                || (control is ListBox list && list.IntegralHeight)
                || (control is TextBoxBase box && !box.Multiline);
        }

        /// <summary>
        /// The message the OS sends when a window's DPI changes: both words of wParam carry
        /// the new DPI, lParam points at the outer rectangle Windows suggests for the new
        /// scale. WinForms adopts that rectangle, so it has to be the scaled *client* area
        /// plus a frame — and the frame has to be the one the window actually has. A
        /// synthetic message does not make the OS re-thicken the non-client area, so asking
        /// for the target DPI's frame (via <c>AdjustWindowRectExForDpi</c>) leaves the client
        /// area short by the difference and shifts every bottom-anchored control. Reusing the
        /// current frame keeps the transition internally consistent; the frame thickness
        /// itself is what only a real monitor change can show (task 10.2).
        /// </summary>
        static Rectangle SendDpiChanged(Form form, int newDpi, int oldDpi)
        {
            var ratio = (double)newDpi / Math.Max(1, oldDpi);
            var frame = form.Size - form.ClientSize;
            var client = new Size(
                DpiContext.Scale(form.ClientSize.Width, ratio),
                DpiContext.Scale(form.ClientSize.Height, ratio));
            var suggested = new Rectangle(form.Left, form.Top, client.Width + frame.Width, client.Height + frame.Height);

            var buffer = Marshal.AllocHGlobal(16);
            try
            {
                Marshal.WriteInt32(buffer, 0, suggested.Left);
                Marshal.WriteInt32(buffer, 4, suggested.Top);
                Marshal.WriteInt32(buffer, 8, suggested.Right);
                Marshal.WriteInt32(buffer, 12, suggested.Bottom);

                SETUNA.Main.WindowsAPI.SendMessage(
                    form.Handle,
                    WM_DPICHANGED,
                    new IntPtr((newDpi << 16) | newDpi),
                    buffer);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }

            Application.DoEvents();

            return suggested;
        }

        /// <summary>
        /// Far enough for OnLoad and the first layout pass to have run, off the side of every
        /// monitor so nothing flashes on screen. Deliberately not minimized: a minimized form
        /// reports an empty client area and its docked panels collapse.
        /// </summary>
        static void ShowOffScreen(Form form)
        {
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-30000, -30000);
            form.ShowInTaskbar = false;
            form.Show();
            Application.DoEvents();
            form.Hide();
        }
    }
}
