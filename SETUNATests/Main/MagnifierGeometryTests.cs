using System.Collections.Generic;
using System.Drawing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SETUNA.Main;

namespace SETUNA.Main.Tests
{
    /// <summary>
    /// Pins the magnifier's placement and viewport geometry. The window used to be
    /// nailed to the target screen's top-left or bottom-right corner and only ever
    /// flipped between those two, and the viewport origin was
    /// <c>Cursor.Position - halfViewport</c> with no clamping, so sampling near a
    /// screen edge read from negative coordinates.
    /// </summary>
    [TestClass]
    public class MagnifierGeometryTests
    {
        static readonly Size Window = new Size(250, 265);
        static readonly Rectangle PrimaryScreen = new Rectangle(0, 0, 1920, 1080);

        /// <summary>A secondary screen sitting left of and above the primary one.</summary>
        static readonly Rectangle NegativeOriginScreen = new Rectangle(-1920, -200, 1920, 1080);

        const int Gap = MagnifierGeometry.DefaultGap;

        [TestMethod]
        public void ACursorInTheMiddleGetsTheBottomRightQuadrant()
        {
            var location = MagnifierGeometry.WindowLocation(new Point(500, 400), Window, PrimaryScreen, Gap);

            Assert.AreEqual(new Point(500 + Gap, 400 + Gap), location);
        }

        [TestMethod]
        public void ACursorNearTheRightEdgeFlipsTheWindowLeft()
        {
            var cursor = new Point(1900, 400);

            var location = MagnifierGeometry.WindowLocation(cursor, Window, PrimaryScreen, Gap);

            Assert.AreEqual(cursor.X - Gap - Window.Width, location.X, "the window must move to the cursor's left");
            Assert.AreEqual(cursor.Y + Gap, location.Y, "the vertical axis had room and must not flip");
            Assert.IsTrue(location.X + Window.Width <= PrimaryScreen.Right);
        }

        [TestMethod]
        public void ACursorNearTheBottomEdgeFlipsTheWindowUp()
        {
            var cursor = new Point(500, 1070);

            var location = MagnifierGeometry.WindowLocation(cursor, Window, PrimaryScreen, Gap);

            Assert.AreEqual(cursor.X + Gap, location.X, "the horizontal axis had room and must not flip");
            Assert.AreEqual(cursor.Y - Gap - Window.Height, location.Y, "the window must move above the cursor");
            Assert.IsTrue(location.Y + Window.Height <= PrimaryScreen.Bottom);
        }

        [TestMethod]
        public void EveryCornerKeepsTheWindowOnScreenAndOffTheCursor()
        {
            foreach (var screen in new[] { PrimaryScreen, NegativeOriginScreen })
            {
                foreach (var cursor in Corners(screen))
                {
                    var window = new Rectangle(
                        MagnifierGeometry.WindowLocation(cursor, Window, screen, Gap), Window);

                    Assert.IsTrue(screen.Contains(window), "window " + window + " left screen " + screen);
                    Assert.IsFalse(window.Contains(cursor), "window " + window + " covered cursor " + cursor);
                }
            }
        }

        [TestMethod]
        public void NoCursorPositionEverPutsTheWindowOffScreen()
        {
            foreach (var screen in new[] { PrimaryScreen, NegativeOriginScreen })
            {
                foreach (var cursor in Sweep(screen))
                {
                    var window = new Rectangle(
                        MagnifierGeometry.WindowLocation(cursor, Window, screen, Gap), Window);

                    Assert.IsTrue(screen.Contains(window), "window " + window + " left screen " + screen);
                }
            }
        }

        [TestMethod]
        public void NoCursorPositionEverEndsUpUnderTheWindow()
        {
            foreach (var screen in new[] { PrimaryScreen, NegativeOriginScreen })
            {
                foreach (var cursor in Sweep(screen))
                {
                    var window = new Rectangle(
                        MagnifierGeometry.WindowLocation(cursor, Window, screen, Gap), Window);

                    Assert.IsFalse(window.Contains(cursor), "window " + window + " covered cursor " + cursor);
                }
            }
        }

        [TestMethod]
        public void AScreenTooNarrowForTheGapStillKeepsTheWindowFullyVisible()
        {
            // 260 fits the 250-wide window but not the window plus a 24 gap on either
            // side. Staying fully visible wins; the cursor may end up covered.
            var screen = new Rectangle(0, 0, 260, 1080);

            var window = new Rectangle(
                MagnifierGeometry.WindowLocation(new Point(130, 400), Window, screen, Gap), Window);

            Assert.IsTrue(screen.Contains(window), "window " + window + " left screen " + screen);
        }

        [TestMethod]
        public void TheDestinationIsAnExactIntegerMultipleOfTheViewport()
        {
            // 246 / 4 is 61.5, so the old "sample 61x61, stretch to 246" path magnified
            // by 4.03 and produced uneven pixel blocks. The destination is now sized to
            // an exact multiple and the remainder becomes a hairline border.
            foreach (var destination in new[] { new Size(246, 246), new Size(250, 200), new Size(99, 33) })
            {
                var viewport = MagnifierGeometry.ViewportSize(destination, MagnifierGeometry.Magnification);
                var rect = MagnifierGeometry.DestinationRectangle(
                    destination, viewport, MagnifierGeometry.Magnification);

                Assert.AreEqual(viewport.Width * MagnifierGeometry.Magnification, rect.Width);
                Assert.AreEqual(viewport.Height * MagnifierGeometry.Magnification, rect.Height);
                Assert.IsTrue(
                    new Rectangle(Point.Empty, destination).Contains(rect),
                    "destination " + rect + " overflowed " + destination);
            }
        }

        [TestMethod]
        public void ADegenerateMagnificationDoesNotDivideByZero()
        {
            Assert.AreEqual(new Size(246, 246), MagnifierGeometry.ViewportSize(new Size(246, 246), 0));
        }

        [TestMethod]
        public void TheViewportIsNeverSmallerThanOnePixel()
        {
            Assert.AreEqual(new Size(1, 1), MagnifierGeometry.ViewportSize(new Size(2, 2), 4));
        }

        [TestMethod]
        public void TheSourceRectangleIsCenteredOnTheCursor()
        {
            Assert.AreEqual(
                new Rectangle(70, 170, 61, 61),
                MagnifierGeometry.SourceRectangle(new Point(100, 200), new Size(61, 61)));
        }

        [TestMethod]
        public void AFullyInBoundsViewportFillsTheWholeDestination()
        {
            var destination = new Rectangle(1, 1, 244, 244);
            var source = MagnifierGeometry.SourceRectangle(new Point(500, 400), new Size(61, 61));

            var region = MagnifierGeometry.Clip(source, new Size(1920, 1080), destination, 4);

            Assert.IsFalse(region.IsEmpty);
            Assert.AreEqual(source, region.Source);
            Assert.AreEqual(destination, region.Destination);
        }

        [TestMethod]
        public void ATopLeftOverflowKeepsTheInBoundsPartAndShiftsTheDestination()
        {
            var destination = new Rectangle(1, 1, 244, 244);
            var source = MagnifierGeometry.SourceRectangle(new Point(10, 10), new Size(61, 61));

            var region = MagnifierGeometry.Clip(source, new Size(1920, 1080), destination, 4);

            Assert.AreEqual(new Rectangle(0, 0, 41, 41), region.Source);
            Assert.AreEqual(new Rectangle(81, 81, 164, 164), region.Destination);
            Assert.AreEqual(
                destination.Right, region.Destination.Right, "the in-bounds part must still reach the far edge");
        }

        [TestMethod]
        public void ABottomRightOverflowKeepsTheInBoundsPartAtTheDestinationOrigin()
        {
            var destination = new Rectangle(1, 1, 244, 244);
            var source = MagnifierGeometry.SourceRectangle(new Point(95, 95), new Size(61, 61));

            var region = MagnifierGeometry.Clip(source, new Size(100, 100), destination, 4);

            Assert.AreEqual(new Rectangle(65, 65, 35, 35), region.Source);
            Assert.AreEqual(new Rectangle(1, 1, 140, 140), region.Destination);
        }

        [TestMethod]
        public void AViewportFullyOutsideTheSnapshotIsEmpty()
        {
            var source = MagnifierGeometry.SourceRectangle(new Point(500, 500), new Size(61, 61));

            var region = MagnifierGeometry.Clip(source, new Size(100, 100), new Rectangle(1, 1, 244, 244), 4);

            Assert.IsTrue(region.IsEmpty);
        }

        static IEnumerable<Point> Corners(Rectangle screen)
        {
            yield return new Point(screen.Left, screen.Top);
            yield return new Point(screen.Right - 1, screen.Top);
            yield return new Point(screen.Left, screen.Bottom - 1);
            yield return new Point(screen.Right - 1, screen.Bottom - 1);
        }

        static IEnumerable<Point> Sweep(Rectangle screen)
        {
            foreach (var x in Axis(screen.Left, screen.Right))
            {
                foreach (var y in Axis(screen.Top, screen.Bottom))
                {
                    yield return new Point(x, y);
                }
            }
        }

        /// <summary>A 37-pixel stride so the sweep does not land only on multiples of
        /// the window size or the gap.</summary>
        static IEnumerable<int> Axis(int start, int end)
        {
            for (var value = start; value < end; value += 37)
            {
                yield return value;
            }

            yield return end - 1;
        }
    }
}
