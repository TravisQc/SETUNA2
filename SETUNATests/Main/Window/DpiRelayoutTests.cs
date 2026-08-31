using System;
using System.Drawing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SETUNA.Main.Window.Tests
{
    /// <summary>
    /// Pins the DPI-change arithmetic. The numbers here are the ones measured on a
    /// 168 DPI primary plus 96 DPI secondary desktop: dragging the options dialog
    /// across the boundary made the OS resize the window from 1069x746 to 611x426
    /// while WinForms left the contents at the old scale. The arithmetic has to
    /// reproduce that 611x426 — that is the frame the application is handed — and
    /// has to get back to 1069x746 when the window returns, or every crossing would
    /// shave a pixel off the window.
    /// <para>
    /// Everything here is pure: no window, no second monitor, no display-settings
    /// change. The message path that consumes it is exercised by
    /// <c>DpiRelayoutFormTests</c>.
    /// </para>
    /// </summary>
    [TestClass]
    public class DpiRelayoutTests
    {
        [TestMethod]
        public void NewDpiComesFromTheLowWordOfWParam()
        {
            Assert.AreEqual(96, DpiRelayout.DpiFromMessage(new IntPtr((96 << 16) | 96)));
            Assert.AreEqual(168, DpiRelayout.DpiFromMessage(new IntPtr((168 << 16) | 168)));
        }

        [TestMethod]
        public void NewDpiSurvivesAWParamWithTheSignBitSet()
        {
            // On x64 the upper half of wParam can carry bits that make
            // IntPtr.ToInt32() throw OverflowException. Masking has to happen on
            // the 64-bit value, not after a narrowing conversion.
            var wParam = new IntPtr(unchecked((long)0xFFFFFFFF00600060));

            Assert.AreEqual(96, DpiRelayout.DpiFromMessage(wParam));
        }

        [TestMethod]
        public void ZeroAndNegativeDpiAreNotUsable()
        {
            // GetDpiForWindow returns 0 for an invalid handle. That means "unknown",
            // not "96" — scaling against it would silently resize windows.
            Assert.IsFalse(DpiRelayout.IsUsableDpi(0));
            Assert.IsFalse(DpiRelayout.IsUsableDpi(-168));
            Assert.IsTrue(DpiRelayout.IsUsableDpi(96));
        }

        [TestMethod]
        public void RelayoutIsRequiredOnlyForTwoUsableAndDifferentDpis()
        {
            Assert.IsTrue(DpiRelayout.RequiresRelayout(96, 168));
            Assert.IsTrue(DpiRelayout.RequiresRelayout(168, 96));

            Assert.IsFalse(DpiRelayout.RequiresRelayout(168, 168), "Same DPI must not trigger a relayout.");
            Assert.IsFalse(DpiRelayout.RequiresRelayout(0, 168), "An unknown new DPI must not trigger a relayout.");
            Assert.IsFalse(DpiRelayout.RequiresRelayout(168, 0), "An unknown old DPI must not trigger a relayout.");
        }

        [TestMethod]
        public void FactorIsOneWhenNoRelayoutIsRequired()
        {
            // Callers therefore do not need their own guard: scaling by the factor
            // of a no-op change leaves every value alone.
            Assert.AreEqual(1f, DpiRelayout.Factor(168, 168));
            Assert.AreEqual(1f, DpiRelayout.Factor(0, 168));
            Assert.AreEqual(1f, DpiRelayout.Factor(168, 0));
        }

        [TestMethod]
        public void FactorIsTheRatioOfTheTwoDpis()
        {
            Assert.AreEqual(96f / 168f, DpiRelayout.Factor(96, 168), 0.0001f);
            Assert.AreEqual(168f / 96f, DpiRelayout.Factor(168, 96), 0.0001f);
        }

        [TestMethod]
        public void PointSizeScalesWithTheDpiRatio()
        {
            Assert.AreEqual(9f * 96f / 168f, DpiRelayout.ScalePointSize(9f, 96, 168), 0.0001f);
            Assert.AreEqual(9f, DpiRelayout.ScalePointSize(9f, 168, 168), 0.0001f);
        }

        [TestMethod]
        public void PointSizeNeverReachesZero()
        {
            // Font's constructor rejects a non-positive size, so an extreme
            // downscale has to be floored rather than allowed to throw.
            Assert.AreEqual(DpiRelayout.MinimumPointSize, DpiRelayout.ScalePointSize(1f, 1, 10000));
            Assert.IsTrue(DpiRelayout.ScalePointSize(0.01f, 96, 168) >= DpiRelayout.MinimumPointSize);
        }

        [TestMethod]
        public void PointSizeStaysOnTheSafeSideOfAWholePixelEm()
        {
            // The knife edge that cost a whole pixel of font. A point size becomes a
            // pixel em by multiplying with the DPI and dividing by 72, and that
            // conversion rounds UP. 宋体 9pt on a 168 DPI system is a 21px em; scaled for
            // 96 DPI it has to land on 12.0px exactly. Computing it as
            // `9f * ((float)96 / 168)` overshoots by one ULP — 12.000002 — which ceils to
            // 13, and the caption renders about 9% wider (measured 144px where 132px was
            // due), shoving neighbouring controls aside. Multiplying before dividing, in
            // double, lands just below instead.
            var scaled = DpiRelayout.ScalePointSize(9f, 96, 168);
            var em = scaled * 168.0 / 72.0;

            Assert.IsTrue(
                em <= 12.0,
                "A 9pt font scaled from 168 to 96 DPI realises as a " + em
                    + "px em, which rounds up to " + Math.Ceiling(em) + "px instead of 12px.");
        }

        [TestMethod]
        public void PointSizeRoundTripsThroughTheCommonScaleFactors()
        {
            // Every scale Windows offers between 100% and 300%, out and back. The font has
            // to come home: an em that grows by a pixel per crossing would inflate the
            // text a little more every time the window changes monitors.
            foreach (var dpi in new[] { 96, 120, 144, 192, 240, 288 })
            {
                var away = DpiRelayout.ScalePointSize(9f, dpi, 168);
                var back = DpiRelayout.ScalePointSize(away, 168, dpi);

                Assert.AreEqual(9f, back, 0.0001f, "9pt did not survive the trip through " + dpi + " DPI.");
            }
        }

        [TestMethod]
        public void SizeScalingReproducesTheFrameTheSystemHandsOver()
        {
            var scaled = DpiRelayout.ScaleSize(new Size(1069, 746), 96, 168);

            Assert.AreEqual(new Size(611, 426), scaled);
        }

        [TestMethod]
        public void SizeScalingReturnsToTheOriginalAfterARoundTrip()
        {
            var original = new Size(1069, 746);

            var away = DpiRelayout.ScaleSize(original, 96, 168);
            var back = DpiRelayout.ScaleSize(away, 168, 96);

            // Truncation would land on 745 here and lose a pixel on every crossing.
            Assert.AreEqual(original, back);
        }

        [TestMethod]
        public void SizeScalingKeepsEveryDimensionAtLeastOnePixel()
        {
            var scaled = DpiRelayout.ScaleSize(new Size(2, 3), 1, 10000);

            Assert.AreEqual(new Size(1, 1), scaled);
        }

        [TestMethod]
        public void SizeScalingLeavesEmptyDimensionsAlone()
        {
            // Zero is the caller's "not set" signal; inventing a pixel would turn it
            // into a real constraint.
            Assert.AreEqual(new Size(0, 0), DpiRelayout.ScaleSize(Size.Empty, 96, 168));
        }

        [TestMethod]
        public void ClientSizeScalesByTheFactorTheFrameworkReports()
        {
            // The measured OptionForm case: auto-scale dimensions of 11x21 at 168 DPI against
            // 6x12 at 96, and a client area of 1063x700. The dimensions are the ambient
            // font's average character size, so their ratio is the very factor the framework
            // used on the child controls — which makes it, and not the DPI ratio, the thing
            // the client area has to follow.
            var scaled = DpiRelayout.ScaleClientSize(new Size(1063, 700), new SizeF(11f, 21f), new SizeF(6f, 12f));

            Assert.AreEqual(new Size(580, 400), scaled);
        }

        [TestMethod]
        public void ClientSizeReturnsToItsOriginalAfterARoundTrip()
        {
            var original = new Size(1063, 700);
            var high = new SizeF(11f, 21f);
            var low = new SizeF(6f, 12f);

            var away = DpiRelayout.ScaleClientSize(original, high, low);
            var back = DpiRelayout.ScaleClientSize(away, low, high);

            // Every crossing is computed from the current state, so a non-reversible step
            // would accumulate: 580 -> 1063 needs the rounding to go the other way.
            Assert.AreEqual(original, back);
        }

        [TestMethod]
        public void ClientSizeIsSkippedWhenAScaleIsUnusable()
        {
            // Size.Empty tells the caller to keep whatever the framework produced. A form
            // with AutoScaleMode.None reports zero dimensions and must not be resized by a
            // factor it never laid out against.
            Assert.IsTrue(DpiRelayout.ScaleClientSize(new Size(400, 300), SizeF.Empty, new SizeF(6f, 12f)).IsEmpty);
            Assert.IsTrue(DpiRelayout.ScaleClientSize(new Size(400, 300), new SizeF(6f, 12f), SizeF.Empty).IsEmpty);
            Assert.IsTrue(DpiRelayout.ScaleClientSize(Size.Empty, new SizeF(6f, 12f), new SizeF(11f, 21f)).IsEmpty);
        }

        [TestMethod]
        public void ComposeTakesThePositionFromTheSuggestionAndTheSizeFromTheRelayout()        {
            var suggested = new Rectangle(3900, 560, 611, 426);
            var current = new Rectangle(60, 60, 1069, 746);

            var composed = DpiRelayout.Compose(suggested, current, new Size(586, 429));

            Assert.AreEqual(new Rectangle(3900, 560, 586, 429), composed);
        }

        [TestMethod]
        public void ComposeKeepsTheCentreWithoutASuggestion()
        {
            // The no-suggestion path is the "we missed the DPI change while hidden"
            // correction. Keeping the top-left would throw a centred dialog off
            // centre by half the size change; keeping the centre leaves it centred.
            var current = new Rectangle(60, 60, 1069, 746);

            var composed = DpiRelayout.Compose(Rectangle.Empty, current, new Size(586, 429));

            Assert.AreEqual(new Size(586, 429), composed.Size);
            AssertCentreHeld(current, composed);
        }

        [TestMethod]
        public void ComposeKeepsTheCentreWhenGrowing()
        {
            var current = new Rectangle(300, 200, 586, 429);

            var composed = DpiRelayout.Compose(Rectangle.Empty, current, new Size(1069, 746));

            Assert.AreEqual(new Size(1069, 746), composed.Size);
            AssertCentreHeld(current, composed);
        }

        /// <summary>
        /// Integer halving drops up to half a pixel per edge, so the centre is held to
        /// within one pixel rather than exactly.
        /// </summary>
        static void AssertCentreHeld(Rectangle before, Rectangle after)
        {
            var dx = Math.Abs((before.X + before.Width / 2) - (after.X + after.Width / 2));
            var dy = Math.Abs((before.Y + before.Height / 2) - (after.Y + after.Height / 2));

            Assert.IsTrue(dx <= 1, "Horizontal centre moved by " + dx + " px.");
            Assert.IsTrue(dy <= 1, "Vertical centre moved by " + dy + " px.");
        }
    }
}
