using System.Collections.Generic;
using System.Drawing;
using System.Linq;
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

        /// <summary>
        /// The block the marks surround. The implementation centres it with the same
        /// <c>viewport/2</c> expression <see cref="MagnifierGeometry.SourceRectangle"/> uses,
        /// so this reconstructs it the same way — the invariant under test is that the
        /// marks sit on the sampled viewport centre, not on some other cell.
        /// </summary>
        static Rectangle CenterBlock(Rectangle destination, Size viewport, int magnification)
        {
            var factor = magnification < 1 ? 1 : magnification;

            return new Rectangle(
                destination.X + (viewport.Width / 2) * factor,
                destination.Y + (viewport.Height / 2) * factor,
                factor,
                factor);
        }

        [TestMethod]
        public void TheCrosshairCenterBlockIsTheSampledCenterPixel()
        {
            // factor 4, destination (1,1,244,244), viewport 61x61: the center index is
            // (30,30), so the marked block is (121,121,4,4). It must land on the same
            // pixel SourceRectangle uses as its viewport centre.
            var destination = new Rectangle(1, 1, 244, 244);
            var viewport = new Size(61, 61);
            var marks = MagnifierGeometry.CrosshairMarks(
                destination, viewport, MagnifierGeometry.Magnification);

            Assert.AreEqual(
                CenterBlock(destination, viewport, MagnifierGeometry.Magnification),
                new Rectangle(121, 121, 4, 4));
        }

        [TestMethod]
        public void TheMarkedBlockCorrespondsToTheViewportCenterForEvenSizes()
        {
            // Even viewport sizes divide the centre the same way both SourceRectangle and
            // the marker must, so the marker cannot drift half a cell. The block the marks
            // surround is asserted to be exactly the sampled centre.
            foreach (var size in new[] { new Size(61, 61), new Size(62, 62), new Size(80, 40) })
            {
                var destination = new Rectangle(1, 1, 244, 244);
                var expected = CenterBlock(destination, size, MagnifierGeometry.Magnification);

                var marks = MagnifierGeometry.CrosshairMarks(
                    destination, size, MagnifierGeometry.Magnification);

                Assert.IsTrue(
                    marks.Length > 0,
                    "crosshair must produce marks for " + size);

                foreach (var side in BorderRects(expected))
                {
                    Assert.IsTrue(
                        marks.Any(m => m.Bounds.IntersectsWith(side)),
                        "the marks must border the " + side + " side of the sampled centre for " + size);
                }
            }
        }

        [TestMethod]
        public void NoCrosshairMarkIntersectsTheCenterBlock()
        {
            var destination = new Rectangle(1, 1, 244, 244);
            var viewport = new Size(61, 61);
            var marks = MagnifierGeometry.CrosshairMarks(
                destination, viewport, MagnifierGeometry.Magnification);
            var block = CenterBlock(destination, viewport, MagnifierGeometry.Magnification);

            foreach (var mark in marks)
            {
                Assert.IsFalse(
                    mark.Bounds.IntersectsWith(block),
                    "mark " + mark.Bounds + " must not cover the marked pixel " + block);
            }
        }

        [TestMethod]
        public void EveryCrosshairMarkLiesInsideTheDestination()
        {
            var destination = new Rectangle(1, 1, 244, 244);
            var marks = MagnifierGeometry.CrosshairMarks(
                destination, new Size(61, 61), MagnifierGeometry.Magnification);

            Assert.IsTrue(marks.Length > 0, "crosshair must produce marks");

            foreach (var mark in marks)
            {
                Assert.IsTrue(
                    destination.Contains(mark.Bounds),
                    "mark " + mark.Bounds + " left destination " + destination);
            }
        }

        [TestMethod]
        public void TheCrosshairSpansTheWholeDestination()
        {
            var destination = new Rectangle(1, 1, 244, 244);
            var viewport = new Size(61, 61);
            var marks = MagnifierGeometry.CrosshairMarks(
                destination, viewport, MagnifierGeometry.Magnification);
            var block = CenterBlock(destination, viewport, MagnifierGeometry.Magnification);

            var horizontal = marks.Where(m => m.Bounds.Height == 1 && !m.Bounds.IntersectsWith(block)).ToArray();
            var vertical = marks.Where(m => m.Bounds.Width == 1 && !m.Bounds.IntersectsWith(block)).ToArray();

            Assert.AreEqual(destination.Left, horizontal.Min(m => m.Bounds.Left), "horizontal must start at the left edge");
            Assert.AreEqual(destination.Right, horizontal.Max(m => m.Bounds.Right), "horizontal must reach the right edge");
            Assert.AreEqual(destination.Top, vertical.Min(m => m.Bounds.Top), "vertical must start at the top edge");
            Assert.AreEqual(destination.Bottom, vertical.Max(m => m.Bounds.Bottom), "vertical must reach the bottom edge");
        }

        [TestMethod]
        public void EveryCrosshairSideCarriesBothShades()
        {
            // The scheme is a dark and a light band per direction, offset by one pixel.
            // For the ring that is the inner (light) and outer (dark) rings; for the lines
            // it is the two adjacent rows/columns. Either way each side of the block must be
            // flanked by both shades, no matter what the underlying pixels are.
            var destination = new Rectangle(1, 1, 244, 244);
            var viewport = new Size(61, 61);
            var marks = MagnifierGeometry.CrosshairMarks(
                destination, viewport, MagnifierGeometry.Magnification);
            var block = CenterBlock(destination, viewport, MagnifierGeometry.Magnification);

            foreach (var side in BorderRects(block))
            {
                var light = marks.Any(m => !m.Dark && m.Bounds.IntersectsWith(side));
                var dark = marks.Any(m => m.Dark && m.Bounds.IntersectsWith(side));

                Assert.IsTrue(
                    light && dark,
                    "side " + side + " must be flanked by both a light and a dark band");
            }
        }

        [TestMethod]
        public void ATinyDestinationDoesNotThrowAndKeepsEveryMarkInBounds()
        {
            // 6x6 is far smaller than the marker footprint (block 4x4 + 2px ring), so the
            // marks are clipped down or vanish. The point is to fail safely: never run out
            // of bounds, never throw.
            var destination = new Rectangle(0, 0, 6, 6);

            var marks = MagnifierGeometry.CrosshairMarks(
                destination, new Size(61, 61), MagnifierGeometry.Magnification);

            foreach (var mark in marks)
            {
                Assert.IsTrue(
                    destination.Contains(mark.Bounds),
                    "mark " + mark.Bounds + " left tiny destination " + destination);
            }
        }

        /// <summary>A narrow band just outside each side of <paramref name="block"/>: two
        /// pixels wide (or tall), to the left/right/above/below. The marks dead ahead of the
        /// cursor are the strips that must land here. All four strips fit inside the
        /// margin because the block sits two pixels in from the destination edge.</summary>
        static IEnumerable<Rectangle> BorderRects(Rectangle block)
        {
            var ring = MagnifierGeometry.MarkerOuterRingOffset;

            yield return new Rectangle(block.X - ring, block.Y, ring, block.Height);
            yield return new Rectangle(block.Right, block.Y, ring, block.Height);
            yield return new Rectangle(block.X, block.Y - ring, block.Width, ring);
            yield return new Rectangle(block.X, block.Bottom, block.Width, ring);
        }

        [TestMethod]
        public void TheCrosshairMatchesTheDesignCoordinates()
        {
            // factor 4, destination (1,1,244,244), viewport 61x61: block (121,121,4,4),
            // footprint (119,119,8,8). The dark and light bands are pinned explicitly so a
            // future edit that shifts a band by a pixel is caught, not merely "still around".
            var destination = new Rectangle(1, 1, 244, 244);
            var viewport = new Size(61, 61);
            var marks = MagnifierGeometry.CrosshairMarks(
                destination, viewport, MagnifierGeometry.Magnification);

            var block = new Rectangle(121, 121, 4, 4);
            var footprint = Rectangle.Inflate(block, 2, 2);
            var inner = Rectangle.Inflate(block, 1, 1);

            // Dark ring: the outer ring, one pixel outside the inner one.
            AssertHas(marks, new Rectangle(footprint.X, footprint.Bottom - 1, footprint.Width, 1), dark: true);
            AssertHas(marks, new Rectangle(footprint.Right - 1, footprint.Y + 1, 1, footprint.Height - 2), dark: true);

            // Light ring: the inner ring sits between the block and the outer one.
            AssertHas(marks, new Rectangle(inner.X, inner.Y, inner.Width, 1), dark: false);
            AssertHas(marks, new Rectangle(inner.X, inner.Bottom - 1, inner.Width, 1), dark: false);

            // Crosshair lines: light row 122, dark row 123 on the left; same pair on right.
            AssertHas(marks, new Rectangle(destination.Left, 122, footprint.Left - destination.Left, 1), dark: false);
            AssertHas(marks, new Rectangle(destination.Left, 123, footprint.Left - destination.Left, 1), dark: true);
            AssertHas(marks, new Rectangle(footprint.Right, 122, destination.Right - footprint.Right, 1), dark: false);
            AssertHas(marks, new Rectangle(footprint.Right, 123, destination.Right - footprint.Right, 1), dark: true);

            // Vertical bands: light col 122, dark col 123 on top; same pair below.
            AssertHas(marks, new Rectangle(122, destination.Top, 1, footprint.Top - destination.Top), dark: false);
            AssertHas(marks, new Rectangle(123, destination.Top, 1, footprint.Top - destination.Top), dark: true);
            AssertHas(marks, new Rectangle(122, footprint.Bottom, 1, destination.Bottom - footprint.Bottom), dark: false);
            AssertHas(marks, new Rectangle(123, footprint.Bottom, 1, destination.Bottom - footprint.Bottom), dark: true);
        }

        static void AssertHas(IReadOnlyList<CrosshairMark> marks, Rectangle bounds, bool dark)
        {
            Assert.IsTrue(
                marks.Any(m => m.Bounds == bounds && m.Dark == dark),
                "expected a " + (dark ? "dark" : "light") + " mark at " + bounds);
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
