using System;
using System.Collections.Generic;
using System.Drawing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SETUNA.Main.Window.Tests
{
    /// <summary>
    /// A rectangle that straddles two monitors belongs to the one it overlaps most.
    /// <para>
    /// Selecting the nearest instead — what <c>MONITOR_DEFAULTTONEAREST</c> does for a point,
    /// and what "the first screen that intersects" amounts to — gives an answer that depends on
    /// enumeration order, and the answer decides which scale factor a capture is sized for.
    /// <c>MonitorFromWindow</c> already picks by largest intersection, so this rule exists for
    /// the rectangles that have no window yet.
    /// </para>
    /// <para>
    /// Pure arithmetic on physical bounds, so it runs in this host: the DPI-unaware test
    /// process cannot enumerate a mixed-DPI desktop, but it does not need to — the snapshots
    /// here are synthetic and describe the layouts that matter, including the negative origins
    /// a monitor left of the primary produces. The live desktop is covered by
    /// <c>probes/SurfaceGeometryProbe</c>.
    /// </para>
    /// </summary>
    [TestClass]
    public class MonitorSelectionTests
    {
        /// <summary>The layout on the development machine: 4K at 168 DPI, 1080p at 96 to its right.</summary>
        static MonitorSnapshot Primary4K()
        {
            return new MonitorSnapshot(
                new IntPtr(1), @"\\.\DISPLAY2",
                new Rectangle(0, 0, 3840, 2160),
                new Rectangle(0, 0, 3840, 2076),
                168, 168, true);
        }

        static MonitorSnapshot SecondaryToTheRight()
        {
            return new MonitorSnapshot(
                new IntPtr(2), @"\\.\DISPLAY1",
                new Rectangle(3840, 548, 1920, 1080),
                new Rectangle(3840, 548, 1920, 1032),
                96, 96, false);
        }

        static MonitorSnapshot SecondaryToTheLeft()
        {
            return new MonitorSnapshot(
                new IntPtr(3), @"\\.\DISPLAY3",
                new Rectangle(-1920, -200, 1920, 1080),
                new Rectangle(-1920, -200, 1920, 1032),
                96, 96, false);
        }

        static List<MonitorSnapshot> Layout(params MonitorSnapshot[] monitors)
        {
            return new List<MonitorSnapshot>(monitors);
        }

        [TestMethod]
        public void AStraddlingRectangleGoesToTheMonitorItOverlapsMost()
        {
            var monitors = Layout(Primary4K(), SecondaryToTheRight());

            // 300 columns on the primary, 100 on the secondary.
            var mostlyPrimary = new Rectangle(3540, 600, 400, 100);
            Assert.AreEqual(
                @"\\.\DISPLAY2",
                MonitorSnapshot.SelectFor(mostlyPrimary, monitors).DeviceName);

            // 100 columns on the primary, 300 on the secondary.
            var mostlySecondary = new Rectangle(3740, 600, 400, 100);
            Assert.AreEqual(
                @"\\.\DISPLAY1",
                MonitorSnapshot.SelectFor(mostlySecondary, monitors).DeviceName);
        }

        /// <summary>
        /// Enumeration order must not decide the answer, which is the whole point of measuring
        /// the overlap instead of taking the first hit.
        /// </summary>
        [TestMethod]
        public void TheAnswerDoesNotDependOnEnumerationOrder()
        {
            var straddling = new Rectangle(3740, 600, 400, 100);

            Assert.AreEqual(
                MonitorSnapshot.SelectFor(straddling, Layout(Primary4K(), SecondaryToTheRight())).DeviceName,
                MonitorSnapshot.SelectFor(straddling, Layout(SecondaryToTheRight(), Primary4K())).DeviceName);
        }

        /// <summary>
        /// An exact half-and-half split has to resolve the same way every time. Primary wins,
        /// which is also the correction to ScreenToGif's implementation: its
        /// <c>ThenBy(IsPrimary)</c> sorts <c>false</c> first, so a tie there lands on the
        /// secondary monitor.
        /// </summary>
        [TestMethod]
        public void AnExactTieGoesToThePrimaryMonitorInEitherOrder()
        {
            // 200 columns each side of the 3840 boundary, inside both monitors' vertical span.
            var tied = new Rectangle(3640, 600, 400, 100);

            Assert.AreEqual(
                @"\\.\DISPLAY2",
                MonitorSnapshot.SelectFor(tied, Layout(Primary4K(), SecondaryToTheRight())).DeviceName);

            Assert.AreEqual(
                @"\\.\DISPLAY2",
                MonitorSnapshot.SelectFor(tied, Layout(SecondaryToTheRight(), Primary4K())).DeviceName);
        }

        /// <summary>
        /// A monitor left of or above the primary has a negative origin, and the overlap
        /// arithmetic must not lose the sign — the requirement spells this out because a
        /// conversion that clamps at zero silently moves the region onto the primary.
        /// </summary>
        [TestMethod]
        public void ARectangleOnAMonitorWithANegativeOriginIsFound()
        {
            var monitors = Layout(Primary4K(), SecondaryToTheLeft());
            var onTheLeftMonitor = new Rectangle(-1500, -100, 400, 300);

            var picked = MonitorSnapshot.SelectFor(onTheLeftMonitor, monitors);

            Assert.AreEqual(@"\\.\DISPLAY3", picked.DeviceName);
            Assert.AreEqual(96, picked.DpiX);
        }

        /// <summary>
        /// Touching an edge is not overlapping it: a rectangle that ends exactly where a
        /// monitor begins shares no pixel with it.
        /// </summary>
        [TestMethod]
        public void AnAdjacentRectangleDoesNotCountAsOverlapping()
        {
            var monitors = Layout(Primary4K(), SecondaryToTheRight());
            var flushAgainstTheSeam = new Rectangle(3640, 600, 200, 100);

            Assert.AreEqual(
                @"\\.\DISPLAY2",
                MonitorSnapshot.SelectFor(flushAgainstTheSeam, monitors).DeviceName);
        }

        /// <summary>
        /// A rectangle in the gap between two monitors, or on no monitor at all, must report
        /// unavailable rather than a guess. <c>WindowsAPI.GetMonitorSnapshotFor</c> is what
        /// then falls back to the monitor nearest its centre; the selection itself does not
        /// invent one.
        /// </summary>
        [TestMethod]
        public void ARectangleOnNoMonitorIsUnavailable()
        {
            var monitors = Layout(Primary4K(), SecondaryToTheRight());

            // Below the primary, left of the secondary: inside the desktop's bounding box,
            // outside every monitor.
            Assert.IsFalse(MonitorSnapshot.SelectFor(new Rectangle(100, 2200, 400, 100), monitors).IsAvailable);
            Assert.IsFalse(MonitorSnapshot.SelectFor(new Rectangle(-9000, -9000, 10, 10), monitors).IsAvailable);
        }

        /// <summary>
        /// An unavailable candidate has no DPI, so converting against it would be a fabricated
        /// 96. It is skipped even when it is the only one whose bounds contain the rectangle —
        /// the bounds of an unavailable snapshot are empty anyway, so this pins that a caller
        /// can pass the raw enumeration through without filtering it first.
        /// </summary>
        [TestMethod]
        public void UnavailableAndNullCandidatesAreSkipped()
        {
            var monitors = Layout(MonitorSnapshot.Unavailable, null, SecondaryToTheRight());
            var onTheSecondary = new Rectangle(4000, 600, 100, 100);

            Assert.AreEqual(@"\\.\DISPLAY1", MonitorSnapshot.SelectFor(onTheSecondary, monitors).DeviceName);

            Assert.IsFalse(MonitorSnapshot.SelectFor(onTheSecondary, null).IsAvailable);
            Assert.IsFalse(
                MonitorSnapshot.SelectFor(onTheSecondary, Layout(MonitorSnapshot.Unavailable)).IsAvailable);
        }

        /// <summary>
        /// A degenerate rectangle has no area to compare, so it overlaps nothing. The caller
        /// that hands one over gets an explicit unavailable instead of an arbitrary monitor.
        /// </summary>
        [TestMethod]
        public void AnEmptyRectangleOverlapsNothing()
        {
            var monitors = Layout(Primary4K(), SecondaryToTheRight());

            Assert.IsFalse(MonitorSnapshot.SelectFor(Rectangle.Empty, monitors).IsAvailable);
            Assert.IsFalse(MonitorSnapshot.SelectFor(new Rectangle(100, 100, 0, 500), monitors).IsAvailable);
            Assert.IsFalse(MonitorSnapshot.SelectFor(new Rectangle(100, 100, 500, -5), monitors).IsAvailable);
        }
    }
}
