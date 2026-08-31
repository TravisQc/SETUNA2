using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SETUNA.Main.Option;
using SETUNA.Main.Tests;

namespace SETUNA.Main.Window.Tests
{
    /// <summary>
    /// Drives the relayout path on real forms by posting a synthetic
    /// <c>WM_DPICHANGED</c>. The handler reads the new DPI and the suggested rectangle out
    /// of the message, so nothing here needs a second monitor, a per-monitor test host, or
    /// a change to the machine's display settings.
    /// <para>
    /// One thing this host cannot reproduce: the window's non-client area actually changing
    /// thickness. The relayout sizes the window by asking the OS what outer rectangle its
    /// computed client area needs <em>at the new DPI</em>, and here the window never leaves
    /// the host's own monitor, so the frame it really gets is the old one and the resulting
    /// client area comes out larger than the relayout asked for. The tests therefore assert
    /// the outer size against the same DPI-parameterised computation production uses, and
    /// leave "the client area equals the native layout" to the dual-monitor checklist —
    /// measured there as 1069x746 ⟷ 586x429 for the options dialog, matching a natively
    /// created 96 DPI form exactly, in both directions.
    /// </para>
    /// </summary>
    [TestClass]
    public class DpiRelayoutFormTests
    {
        /// <summary>
        /// A form shaped like the dialogs that participate: font-driven auto scaling, a
        /// label that owns its font instead of inheriting it (the case a plain form-font
        /// swap misses), a control anchored to the far edges and one inside a docked panel
        /// (both carry layout state derived from their container's size), and both size
        /// bounds set. Fixed-size, like the dialogs whose layout is reproduced from the
        /// startup baseline.
        /// </summary>
        class Participating : BaseForm
        {
            public readonly Label Inheriting = new Label();
            public readonly Label OwnFont = new Label();
            public readonly Button Anchored = new Button();
            public readonly Panel Docked = new Panel();
            public readonly Label InsideDocked = new Label();

            public Participating()
            {
                SuspendLayout();
                AutoScaleDimensions = new SizeF(6F, 12F);
                AutoScaleMode = AutoScaleMode.Font;

                Inheriting.SetBounds(10, 10, 200, 20);
                Inheriting.Name = "inheriting";
                Inheriting.Text = "inheriting";

                OwnFont.SetBounds(10, 40, 200, 20);
                OwnFont.Name = "ownFont";
                OwnFont.Text = "own font";
                OwnFont.Font = new Font(Font, FontStyle.Bold);

                // Anchored to the opposite edges, so its bounds are derived from offsets the
                // layout engine caches when the bounds are written. Writing them while the
                // container is the wrong size corrupts those offsets for good.
                Anchored.SetBounds(280, 120, 100, 30);
                Anchored.Name = "anchored";
                Anchored.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;

                InsideDocked.SetBounds(10, 8, 120, 20);
                InsideDocked.Name = "insideDocked";
                InsideDocked.Text = "docked child";
                InsideDocked.Anchor = AnchorStyles.Top | AnchorStyles.Right;

                Docked.Name = "docked";
                Docked.Dock = DockStyle.Bottom;
                Docked.Height = 40;
                Docked.Controls.Add(InsideDocked);

                Controls.Add(Inheriting);
                Controls.Add(OwnFont);
                Controls.Add(Anchored);
                Controls.Add(Docked);

                FormBorderStyle = FormBorderStyle.FixedDialog;
                ClientSize = new Size(400, 200);
                MinimumSize = new Size(200, 100);
                MaximumSize = new Size(800, 400);
                ResumeLayout(false);
            }

            protected override bool ScalesWithMonitorDpi => true;
        }

        /// <summary>The same form, but one the user can resize — like the main window.</summary>
        sealed class Resizable : Participating
        {
            public Resizable()
            {
                FormBorderStyle = FormBorderStyle.Sizable;
            }
        }

        sealed class NotParticipating : BaseForm
        {
            public NotParticipating()
            {
                AutoScaleDimensions = new SizeF(6F, 12F);
                AutoScaleMode = AutoScaleMode.Font;
                ClientSize = new Size(400, 200);
            }
        }

        [TestMethod]
        public void RaisingTheDpiScalesEveryFontIncludingTheOnesControlsOwn()
        {
            using (var form = new Participating())
            {
                LayoutSnapshot.ShowOffScreen(form);
                Normalise(form);

                var inheritedHeight = form.Inheriting.Font.Height;
                var ownHeight = form.OwnFont.Font.Height;

                SendDpiChanged(form, HighDpi * 2);

                // The whole point of the change: the label that owns its font has to
                // follow. Leaving it behind is what makes the options dialog's navigation
                // labels pile up on each other.
                Assert.IsTrue(
                    form.OwnFont.Font.Height > ownHeight,
                    "The label owning its font kept its old size: " + form.OwnFont.Font.Height + "px.");
                Assert.IsTrue(form.Inheriting.Font.Height > inheritedHeight);
            }
        }

        [TestMethod]
        public void TheOuterSizeFollowsTheClientAreaComputedForTheNewDpi()
        {
            using (var form = new Participating())
            {
                LayoutSnapshot.ShowOffScreen(form);

                // Deliberately no Normalise: the transition has to go from the host's own DPI
                // up to 168, so that the frame thickness the relayout must account for (46px
                // of caption) differs from the one this window really has (29px). Both halves
                // of the computation are then observable — drop either and the outer size
                // comes out 17px short.
                var clientBefore = form.ClientSize;
                var scaleBefore = form.CurrentAutoScaleDimensions;

                SendDpiChanged(form, HighDpi);

                var expectedClient = DpiRelayout.ScaleClientSize(clientBefore, scaleBefore, form.CurrentAutoScaleDimensions);
                var expectedOuter = WindowsAPI.GetOuterSizeForClientSize(form.Handle, expectedClient, HighDpi);

                Assert.IsFalse(expectedClient.IsEmpty, "The form scales with its font, so a factor must be available.");
                Assert.IsFalse(expectedOuter.IsEmpty, "AdjustWindowRectExForDpi is available from Windows 10 1607.");
                Assert.AreEqual(expectedOuter, form.Size);

                // And the two are genuinely different numbers here, so the assertion above is
                // not comparing the framework's own arithmetic with itself.
                Assert.AreNotEqual(
                    WindowsAPI.GetOuterSizeForClientSize(form.Handle, expectedClient, WindowsAPI.GetWindowDpi(form.Handle)),
                    expectedOuter,
                    "This host cannot tell the two computations apart, so the test proves nothing.");
            }
        }

        [TestMethod]
        public void TheLayoutSurvivesADpiRoundTrip()
        {
            using (var form = new Participating())
            {
                LayoutSnapshot.ShowOffScreen(form);
                Normalise(form);

                var ownHeight = form.OwnFont.Font.Height;
                var inheritedHeight = form.Inheriting.Font.Height;
                var scale = form.CurrentAutoScaleDimensions;

                SendDpiChanged(form, LowDpi);
                SendDpiChanged(form, HighDpi);

                Assert.AreEqual(ownHeight, form.OwnFont.Font.Height, "The owned font did not come back.");
                Assert.AreEqual(inheritedHeight, form.Inheriting.Font.Height, "The inherited font did not come back.");

                // The factor the framework reports is what the client area is scaled by, so
                // its returning is what makes the window size return too. The window size
                // itself is checked on the dual-monitor checklist, where the frame thickness
                // really changes.
                Assert.AreEqual(scale, form.CurrentAutoScaleDimensions);
            }
        }

        [TestMethod]
        public void SizeBoundsDoNotBlockTheRelayoutAndFollowTheNewDpi()
        {
            using (var form = new Participating())
            {
                LayoutSnapshot.ShowOffScreen(form);
                Normalise(form);

                // Pin both bounds to the current size, so a relayout in either direction
                // has to break through one of them. Left in place, the minimum clamps the
                // shrinking window and clips the layout that shrank inside it.
                form.MinimumSize = form.Size;
                form.MaximumSize = form.Size;
                var pinned = form.Size;

                SendDpiChanged(form, HighDpi / 2);

                Assert.IsTrue(
                    form.Size.Width < pinned.Width,
                    "The window stopped at the old minimum width " + pinned.Width + ".");
                AssertScaled(pinned.Width / 2, form.MinimumSize.Width, "minimum width");
                AssertScaled(pinned.Width / 2, form.MaximumSize.Width, "maximum width");

                // Now the other direction, against the bounds this relayout just applied:
                // the maximum has to give way too, or the window would be clamped and the
                // grown layout clipped.
                var halved = form.MaximumSize;

                SendDpiChanged(form, HighDpi * 2);

                Assert.IsTrue(
                    form.Size.Width > pinned.Width,
                    "The window stopped at the old maximum width " + pinned.Width + ".");
                AssertScaled(halved.Width * 4, form.MaximumSize.Width, "maximum width");
            }
        }

        [TestMethod]
        public void ADpiChangeToTheSameDpiChangesNothing()
        {
            using (var form = new Participating())
            {
                LayoutSnapshot.ShowOffScreen(form);
                Normalise(form);

                var bounds = form.Bounds;
                var fontHeight = form.Font.Height;

                SendDpiChanged(form, HighDpi);

                Assert.AreEqual(bounds, form.Bounds);
                Assert.AreEqual(fontHeight, form.Font.Height);
            }
        }

        [TestMethod]
        public void AFormThatDoesNotParticipateIsLeftAlone()
        {
            using (var form = new NotParticipating())
            {
                LayoutSnapshot.ShowOffScreen(form);

                var bounds = form.Bounds;
                var fontHeight = form.Font.Height;

                SendDpiChanged(form, HighDpi);
                SendDpiChanged(form, LowDpi);

                // Scrap windows, capture overlays and canvases rely on this: their size is
                // their pixel size, and rescaling them would corrupt the image they show.
                Assert.AreEqual(bounds, form.Bounds);
                Assert.AreEqual(fontHeight, form.Font.Height);
            }
        }

        [TestMethod]
        public void TheWindowMovesToTheSuggestedPosition()
        {
            using (var form = new Participating())
            {
                LayoutSnapshot.ShowOffScreen(form);
                Normalise(form);

                var suggested = new Rectangle(-29000, -29000, 100, 100);
                SendDpiChanged(form, LowDpi, suggested);

                Assert.AreEqual(suggested.Location, form.Location);

                // The size comes from the relayout, not from the suggestion: the OS
                // computes the suggested rectangle by scaling the old frame linearly,
                // which lands a few pixels off the layout the form actually needs.
                Assert.AreNotEqual(suggested.Size, form.Size);
            }
        }

        [TestMethod]
        public void TheOptionsDialogScalesEveryCaptionWithTheDpi()
        {
            using (var form = new OptionForm(SetunaOption.GetDefaultOption()))
            {
                LayoutSnapshot.ShowOffScreen(form);
                Normalise(form);

                var before = Snapshot(form);

                SendDpiChanged(form, LowDpi);

                var after = Snapshot(form);

                // The sharp check on relayout fidelity. A font realised one pixel too
                // large reports the same Font.Height but renders about 10% wider, which is
                // what pushes captions over their neighbours — measured 144px where the
                // native 96 DPI layout gives 131px for the same string.
                AssertCaptionsScaled(before, after, LowDpi, HighDpi);

                SendDpiChanged(form, HighDpi);

                AssertCaptionsScaled(before, Snapshot(form), HighDpi, HighDpi);
            }
        }

        [TestMethod]
        public void TheOptionsDialogComesBackUnchangedFromADpiRoundTrip()
        {
            using (var form = new OptionForm(SetunaOption.GetDefaultOption()))
            {
                LayoutSnapshot.ShowOffScreen(form);
                Normalise(form);

                var before = Snapshot(form);

                SendDpiChanged(form, LowDpi);
                SendDpiChanged(form, HighDpi);

                var back = Snapshot(form);

                // Reversibility is asserted, the intermediate state's overlaps are not:
                // this form's designer baseline is not 96 DPI (AutoScaleDimensions of
                // 15x30, ClientSize of 1449x1000), so its controls sit at slightly
                // different relative positions at every DPI — natively too, before any
                // relayout runs. Normalising that baseline is out of scope for this change.
                var regressions = new List<string>();
                foreach (var grown in back.OverflowGrowth(before))
                {
                    regressions.Add(grown.Path + ": reaches " + grown.After + "px past its container (was " + grown.Before + "px)");
                }

                foreach (var grown in back.OverlapGrowth(before))
                {
                    regressions.Add(grown.Path + ": " + grown.After + "px of overlap (was " + grown.Before + "px)");
                }

                Assert.AreEqual(
                    0,
                    regressions.Count,
                    "A DPI round trip left controls out of their containers or on top of each other:"
                        + Environment.NewLine + string.Join(Environment.NewLine, regressions));
            }
        }

        /// <summary>
        /// Every control has to land on exactly the bounds it had before, not near them.
        /// <para>
        /// Auto scaling multiplies a control's integer bounds by a factor and rounds, and
        /// rounding is not invertible: 168 to 96 turns the OK button's width of 179 into 98,
        /// and 96 back to 168 turns that into 180. Measured on the real dialog before the
        /// relayout was rebased on a stored baseline: btnOK 179→180, pictureBox1
        /// X −120→−119 and width 487→488, label1 X 430→431 — one pixel each, stable
        /// afterwards. Computing every DPI from one stored baseline instead of from the
        /// previous state removes it: the same DPI now always yields the same layout, and
        /// the baseline DPI yields the designer's own numbers.
        /// </para>
        /// </summary>
        [TestMethod]
        public void EveryControlComesBackToItsExactBoundsFromADpiRoundTrip()
        {
            var failures = new List<string>();

            foreach (var form in FormsWhoseLayoutIsReproducible())
            {
                using (form)
                {
                    LayoutSnapshot.ShowOffScreen(form);
                    Normalise(form);

                    var before = BoundsOfEveryControl(form);
                    Assert.AreNotEqual(0, before.Count, form.GetType().Name + " has no controls to measure.");

                    SendDpiChanged(form, LowDpi);
                    SendDpiChanged(form, HighDpi);

                    CollectMovedControls(before, BoundsOfEveryControl(form), form, "one round trip", failures);

                    // And it must not creep either: three more crossings, same numbers.
                    for (var i = 0; i < 3; i++)
                    {
                        SendDpiChanged(form, LowDpi);
                        SendDpiChanged(form, HighDpi);
                    }

                    CollectMovedControls(before, BoundsOfEveryControl(form), form, "four round trips", failures);
                }
            }

            Assert.AreEqual(0, failures.Count, string.Join(Environment.NewLine, failures));
        }

        /// <summary>
        /// The far side has to be reproducible too: arriving at 96 DPI a second time must
        /// give the same layout as the first time, or a dialog would look slightly different
        /// on the same monitor depending on how it got there.
        /// </summary>
        [TestMethod]
        public void ReturningToTheOtherDpiReproducesTheSameLayout()
        {
            var failures = new List<string>();

            foreach (var form in FormsWhoseLayoutIsReproducible())
            {
                using (form)
                {
                    LayoutSnapshot.ShowOffScreen(form);
                    Normalise(form);

                    SendDpiChanged(form, LowDpi);
                    var first = BoundsOfEveryControl(form);

                    SendDpiChanged(form, HighDpi);
                    SendDpiChanged(form, LowDpi);

                    CollectMovedControls(first, BoundsOfEveryControl(form), form, "a second visit to 96 DPI", failures);
                }
            }

            Assert.AreEqual(0, failures.Count, string.Join(Environment.NewLine, failures));
        }

        /// <summary>
        /// A window the user has resized must keep that size across a DPI round trip, not
        /// snap back to the one it started at.
        /// <para>
        /// The stored baseline describes one layout: the baseline DPI at the size the window
        /// had then. The main window is resizable and its size is persisted to the options,
        /// so relayouting a resized window from that baseline would throw the user's size
        /// away. The baseline is therefore dropped as soon as something other than the
        /// relayout changes the client area.
        /// </para>
        /// </summary>
        [TestMethod]
        public void AWindowResizedByTheUserKeepsItsSizeAcrossADpiRoundTrip()
        {
            using (var form = new Resizable())
            {
                LayoutSnapshot.ShowOffScreen(form);
                Normalise(form);

                var baseline = form.ClientSize;
                var added = new Size(96, 48);
                form.ClientSize = baseline + added;
                Assert.AreEqual(baseline + added, form.ClientSize, "The fixture has to accept the resize for this test to mean anything.");

                SendDpiChanged(form, LowDpi);
                SendDpiChanged(form, HighDpi);

                // Not an equality check: this host cannot change the window's frame thickness,
                // so a round trip here lands a few pixels off in either direction (see the class
                // comment). What has to hold is that the room the user added is still there and
                // was not replaced by the stored baseline's size.
                Assert.IsTrue(
                    form.ClientSize.Width > baseline.Width + added.Width / 2
                        && form.ClientSize.Height > baseline.Height + added.Height / 2,
                    "The user's size was rolled back towards the baseline " + baseline
                        + ": resized to " + (baseline + added) + ", came back " + form.ClientSize + ".");
            }
        }

        /// <summary>
        /// The forms whose layout has to be reproducible: the fixed-size participants, plus the
        /// local fixture, which is the only one here carrying an anchored control and a docked
        /// panel — layout state the engine derives from the container's size and caches.
        /// <para>
        /// Resizable participants (the style editor, the login prompt, the main window) are
        /// deliberately absent: their layout is a function of the size the user chose, so it is
        /// scaled from the current state rather than reproduced from the startup baseline.
        /// <see cref="AWindowResizedByTheUserKeepsItsSizeAcrossADpiRoundTrip"/> covers that side.
        /// </para>
        /// </summary>
        static IEnumerable<Form> FormsWhoseLayoutIsReproducible()
        {
            yield return new Participating();
            yield return new OptionForm(SetunaOption.GetDefaultOption());
            yield return new SETUNA.Main.HotkeyMsg();
        }

        static Dictionary<string, Rectangle> BoundsOfEveryControl(Control root)
        {
            var bounds = new Dictionary<string, Rectangle>(StringComparer.Ordinal);
            CollectBounds(root, root.GetType().Name, bounds);

            return bounds;
        }

        static void CollectBounds(Control parent, string path, Dictionary<string, Rectangle> into)
        {
            foreach (Control child in parent.Controls)
            {
                var childPath = path + "/" + child.Name;
                into[childPath] = child.Bounds;
                CollectBounds(child, childPath, into);
            }
        }

        static void CollectMovedControls(
            Dictionary<string, Rectangle> before,
            Dictionary<string, Rectangle> after,
            Form form,
            string what,
            List<string> into)
        {
            foreach (var pair in before)
            {
                Rectangle now;
                if (after.TryGetValue(pair.Key, out now) && now != pair.Value)
                {
                    into.Add(form.GetType().Name + " after " + what + " — "
                        + pair.Key + ": " + pair.Value + " became " + now);
                }
            }
        }

        static LayoutSnapshot Snapshot(Form form)
        {
            var snapshot = new LayoutSnapshot();
            snapshot.Capture(form, form.GetType().Name);

            return snapshot;
        }

        [TestMethod]
        public void EveryParticipatingDialogScalesItsCaptionsWithTheDpi()
        {
            var failures = new List<string>();

            foreach (var form in ParticipatingDialogs())
            {
                using (form)
                {
                    Assert.IsTrue(
                        ParticipatesInRelayout(form),
                        form.GetType().Name + " is in the participating list but does not opt in.");

                    LayoutSnapshot.ShowOffScreen(form);
                    Normalise(form);

                    var before = Snapshot(form);

                    SendDpiChanged(form, LowDpi);
                    CollectCaptionFailures(before, Snapshot(form), LowDpi, HighDpi, form, failures);

                    SendDpiChanged(form, HighDpi);
                    CollectCaptionFailures(before, Snapshot(form), HighDpi, HighDpi, form, failures);
                }
            }

            Assert.AreEqual(0, failures.Count, string.Join(Environment.NewLine, failures));
        }

        [TestMethod]
        public void ControlsOwningLayoutStateFollowTheDpi()
        {
            using (var form = new StyleEditForm(null, new SETUNA.Main.KeyItems.KeyItemBook()))
            {
                LayoutSnapshot.ShowOffScreen(form);
                Normalise(form);

                var lists = FindAll<StyleItemListBox>(form);
                Assert.AreNotEqual(0, lists.Count, "The style editor should carry the item lists this test measures.");

                var before = new List<int>();
                foreach (var list in lists)
                {
                    before.Add(list.HelpFont.Height);
                }

                SendDpiChanged(form, LowDpi);

                for (var i = 0; i < lists.Count; i++)
                {
                    // HelpFont is a property of its own, outside the control tree's font
                    // inheritance, so the form-wide relayout cannot reach it. Left behind, the
                    // description line keeps the old DPI's size and the two lines of each item
                    // collapse into each other — which is what the screenshots showed.
                    var ideal = (double)before[i] * LowDpi / HighDpi;
                    var actual = lists[i].HelpFont.Height;

                    Assert.IsTrue(
                        actual >= Math.Floor(ideal) && actual <= Math.Ceiling(ideal),
                        lists[i].Name + ".HelpFont is a " + actual + "px em, expected "
                            + Math.Floor(ideal) + " to " + Math.Ceiling(ideal) + "px.");
                }

                SendDpiChanged(form, HighDpi);

                for (var i = 0; i < lists.Count; i++)
                {
                    Assert.AreEqual(before[i], lists[i].HelpFont.Height, lists[i].Name + ".HelpFont did not come back.");
                }
            }
        }

        [TestMethod]
        public void DpiInvariantListMetricsAreLeftAlone()
        {
            using (var form = new StyleEditForm(null, new SETUNA.Main.KeyItems.KeyItemBook()))
            {
                LayoutSnapshot.ShowOffScreen(form);
                Normalise(form);

                var lists = FindAll<StyleItemListBox>(form);
                var heights = new List<int>();
                var spaces = new List<int>();
                foreach (var list in lists)
                {
                    heights.Add(list.ItemHeight);
                    spaces.Add(list.LeftSpace);
                }

                SendDpiChanged(form, LowDpi);

                for (var i = 0; i < lists.Count; i++)
                {
                    // These are the same pixel values at every DPI in this codebase — measured
                    // 39 and 34 on both a 96 and a 168 DPI host — and the item icons are drawn
                    // at a fixed size inside them. Scaling them here would make the relayout
                    // diverge from the native layout and push the icons out of the row. That
                    // the row height does not follow the DPI at all is a separate, pre-existing
                    // problem.
                    Assert.AreEqual(heights[i], lists[i].ItemHeight, lists[i].Name + ".ItemHeight changed.");
                    Assert.AreEqual(spaces[i], lists[i].LeftSpace, lists[i].Name + ".LeftSpace changed.");
                }
            }
        }

        static List<T> FindAll<T>(Control root) where T : Control
        {
            var found = new List<T>();
            Collect(root, found);

            return found;
        }

        static void Collect<T>(Control parent, List<T> into) where T : Control
        {
            foreach (Control child in parent.Controls)
            {
                var match = child as T;
                if (match != null)
                {
                    into.Add(match);
                }

                Collect(child, into);
            }
        }

        /// <summary>
        /// The dialogs that opt in, minus the main window: building a <c>Mainform</c> starts
        /// timers, registers hotkeys and claims the singleton, none of which belongs in this
        /// host. Its bounds arithmetic is covered by <c>MainWindowLayoutTests</c> and its
        /// live behaviour by the change's manual checklist.
        /// </summary>
        static IEnumerable<Form> ParticipatingDialogs()
        {
            yield return new OptionForm(SetunaOption.GetDefaultOption());
            yield return new StyleEditForm(null, new SETUNA.Main.KeyItems.KeyItemBook());
            yield return new SETUNA.Main.HotkeyMsg();
            yield return new SETUNA.Main.StyleItems.LoginInput();
        }

        [TestMethod]
        public void FormsWhoseSizeIsNotAFunctionOfTheScaleFactorStayOut()
        {
            // LayerRenameWindow's client height does not follow the auto-scale factor even
            // natively: measured 236x59 at 96 DPI against 433x74 at 168, a ratio of 1.25
            // where the factor is 1.75. Its buttons already reach past the client area at
            // both DPIs — a pre-existing defect — and relayouting it only widens the gap, so
            // it deliberately keeps the inherited default.
            using (var form = new SETUNA.Main.StyleItems.LayerRenameWindow())
            {
                Assert.IsFalse(ParticipatesInRelayout(form));
            }
        }

        static bool ParticipatesInRelayout(Form form)
        {
            var property = typeof(BaseForm).GetProperty(
                "ScalesWithMonitorDpi",
                BindingFlags.Instance | BindingFlags.NonPublic);

            return (bool)property.GetValue(form, null);
        }

        static void CollectCaptionFailures(
            LayoutSnapshot before,
            LayoutSnapshot after,
            int newDpi,
            int oldDpi,
            Form form,
            List<string> into)
        {
            foreach (var wrong in CaptionsNotScaled(before, after, newDpi, oldDpi))
            {
                into.Add(form.GetType().Name + " at " + newDpi + " DPI: " + wrong);
            }
        }

        static void AssertCaptionsScaled(LayoutSnapshot before, LayoutSnapshot after, int newDpi, int oldDpi)
        {
            var wrong = CaptionsNotScaled(before, after, newDpi, oldDpi);

            Assert.AreEqual(
                0,
                wrong.Count,
                "Captions did not scale with the DPI change to " + newDpi + ":" + Environment.NewLine
                    + string.Join(Environment.NewLine, wrong));
        }

        /// <summary>
        /// Every caption has to be re-rendered at the DPI it moved to.
        /// <para>
        /// The precise check is on the caption's height, which is the font's realised pixel
        /// em. A pixel em is an integer, so the only two acceptable values are the ones
        /// bracketing the ideal — and when the ideal is a whole number (168 to 96 on a
        /// 21-pixel em gives exactly 12) that pins it to a single value. This is what
        /// catches a font realised one pixel too large: the em comes out 13 where 12 is due,
        /// and the rendered text is 9% wider as a result.
        /// </para>
        /// <para>
        /// The width check is deliberately coarse. Each glyph advance is an integer too, so
        /// a caption accumulates up to half a pixel of rounding per character: measured
        /// faithful deviations on these dialogs reach 6px on a 106px caption. One pixel per
        /// character, floored at three, admits that without admitting a wholesale error.
        /// </para>
        /// </summary>
        static List<string> CaptionsNotScaled(LayoutSnapshot before, LayoutSnapshot after, int newDpi, int oldDpi)
        {
            var wrong = new List<string>();

            foreach (var pair in before.TextSize)
            {
                Size actual;
                if (!after.TextSize.TryGetValue(pair.Key, out actual))
                {
                    continue;
                }

                var idealHeight = (double)pair.Value.Height * newDpi / oldDpi;
                if (actual.Height < Math.Floor(idealHeight) || actual.Height > Math.Ceiling(idealHeight))
                {
                    wrong.Add(pair.Key + ": caption is " + actual.Height + "px tall, expected "
                        + Math.Floor(idealHeight) + " to " + Math.Ceiling(idealHeight) + "px");
                    continue;
                }

                var idealWidth = (double)pair.Value.Width * newDpi / oldDpi;
                var allowance = Math.Max(3, after.Text[pair.Key].Length);
                if (Math.Abs(actual.Width - idealWidth) > allowance)
                {
                    wrong.Add(pair.Key + ": caption is " + actual.Width + "px wide, expected about "
                        + Math.Round(idealWidth) + "px");
                }
            }

            return wrong;
        }

        static void AssertNoLayoutRegression(LayoutSnapshot before, LayoutSnapshot after, string what)
        {
            var regressions = new List<string>();

            foreach (var grown in after.OverflowGrowth(before))
            {
                regressions.Add(grown.Path + ": reaches " + grown.After + "px past its container (was " + grown.Before + "px)");
            }

            foreach (var grown in after.OverlapGrowth(before))
            {
                regressions.Add(grown.Path + ": " + grown.After + "px of overlap (was " + grown.Before + "px)");
            }

            Assert.AreEqual(
                0,
                regressions.Count,
                "The relayout pushed controls out of their containers or onto each other while " + what
                    + ":" + Environment.NewLine + string.Join(Environment.NewLine, regressions));
        }

        /// <summary>
        /// Size bounds are scaled by the DPI arithmetic directly rather than through the
        /// font, so they land exactly, give or take the rounding of a single division.
        /// </summary>
        static void AssertScaled(int expected, int actual, string what)
        {
            Assert.IsTrue(
                Math.Abs(expected - actual) <= 1,
                "Expected " + what + " near " + expected + " but got " + actual + ".");
        }

        /// <summary>175% and 100%: the two scale factors in the reported bug.</summary>
        const int HighDpi = 168;
        const int LowDpi = 96;

        /// <summary>
        /// Puts the form on a known layout DPI before the test's own transition.
        /// <para>
        /// Without this the transitions would start from whatever the test host's system
        /// DPI happens to be, and the ratio under test would differ per machine — the
        /// 168 to 96 case cannot even be expressed at 96, because 96 * 96 / 168 truncates
        /// to 54.
        /// </para>
        /// </summary>
        static void Normalise(Form form)
        {
            SendDpiChanged(form, HighDpi);
        }

        static void SendDpiChanged(Form form, int newDpi)
        {
            // The suggested rectangle only supplies a position — the size comes from the
            // relayout — so the current frame stands in for what the OS would send.
            SendDpiChanged(form, newDpi, form.Bounds);
        }

        /// <summary>
        /// Posts the message the OS sends when a window's monitor DPI changes: the new DPI
        /// in both halves of <c>wParam</c>, a suggested window rectangle in <c>lParam</c>.
        /// </summary>
        static void SendDpiChanged(Form form, int newDpi, Rectangle suggested)
        {
            var buffer = Marshal.AllocHGlobal(16);
            try
            {
                Marshal.WriteInt32(buffer, 0, suggested.Left);
                Marshal.WriteInt32(buffer, 4, suggested.Top);
                Marshal.WriteInt32(buffer, 8, suggested.Right);
                Marshal.WriteInt32(buffer, 12, suggested.Bottom);

                WindowsAPI.SendMessage(
                    form.Handle,
                    DpiRelayout.WM_DPICHANGED,
                    new IntPtr((newDpi << 16) | newDpi),
                    buffer);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }

            Application.DoEvents();
        }
    }
}
