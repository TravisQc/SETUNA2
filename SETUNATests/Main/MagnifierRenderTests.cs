using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SETUNA.Main;

namespace SETUNA.Main.Tests
{
    /// <summary>
    /// Covers the magnifier's refresh loop. The old <c>RefreshImage</c> allocated a
    /// fresh source bitmap, an undisposed <c>Graphics</c>, and a second scaled bitmap
    /// on every 100 ms tick, and never disposed the <c>PictureBox.Image</c> it
    /// replaced — three leaks per frame for the whole capture session.
    /// <para>
    /// The deterministic assertion about that is
    /// <c>TheControlImageIsNeverReplacedAcrossFrames</c>: all three leaked objects are
    /// finalizable, so a GDI handle count taken after a forced GC would have passed on
    /// the old code too. The handle count below is only a backstop for a
    /// non-finalizable handle leak.
    /// </para>
    /// <para>
    /// <c>RenderFrom</c> takes the snapshot explicitly so the loop can be driven here
    /// without a real screen. The form needs no handle for this: the picture box gets
    /// its size from the designer and <c>Invalidate</c> on an unrealized control is a
    /// no-op.
    /// </para>
    /// </summary>
    [TestClass]
    public class MagnifierRenderTests
    {
        const uint GR_GDIOBJECTS = 0;
        const int GWL_EXSTYLE = -20;
        const int WS_EX_TRANSPARENT = 0x00000020;

        [DllImport("user32.dll")]
        static extern uint GetGuiResources(IntPtr hProcess, uint uiFlags);

        [DllImport("user32.dll")]
        static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        static uint GdiObjectCount()
        {
            return GetGuiResources(Process.GetCurrentProcess().Handle, GR_GDIOBJECTS);
        }

        [TestMethod]
        public void RepeatedRendersDoNotAccumulateGdiObjectsUnbounded()
        {
            const int Iterations = 400;

            using (var magnifier = new Magnifier())
            using (var snapshot = new Bitmap(400, 400, PixelFormat.Format24bppRgb))
            {
                // Let the shared buffer and its Graphics come into existence before the
                // baseline, so what is measured is strictly per-frame cost.
                for (var i = 0; i < 20; i++)
                {
                    magnifier.RenderFrom(snapshot, Point.Empty, new Point(200, 200));
                }

                Settle();
                var before = GdiObjectCount();
                Assert.IsTrue(
                    before > 0, "GetGuiResources must report a usable count for this assertion to mean anything.");

                for (var i = 0; i < Iterations; i++)
                {
                    magnifier.RenderFrom(snapshot, Point.Empty, new Point(i % 400, i * 7 % 400));
                }

                Settle();
                var after = GdiObjectCount();

                Assert.IsTrue(
                    (long)after - before < Iterations / 10,
                    $"GDI objects grew by {(long)after - before} across {Iterations} renders ({before} -> {after}).");
            }
        }

        [TestMethod]
        public void ANullSnapshotIsSkippedInsteadOfThrowing()
        {
            using (var magnifier = new Magnifier())
            {
                magnifier.RenderFrom(null, Point.Empty, new Point(10, 10));
            }
        }

        [TestMethod]
        public void TheControlImageIsNeverReplacedAcrossFrames()
        {
            // The buffer is shared for the whole session, so there is no outgoing image
            // to leak. Replacing it per frame is what used to lose one bitmap a tick.
            using (var magnifier = new Magnifier())
            using (var snapshot = new Bitmap(400, 400, PixelFormat.Format24bppRgb))
            {
                magnifier.RenderFrom(snapshot, Point.Empty, new Point(100, 100));
                var first = RenderedImageOf(magnifier);

                for (var i = 0; i < 10; i++)
                {
                    magnifier.RenderFrom(snapshot, Point.Empty, new Point(100 + i, 100 + i));
                }

                Assert.IsTrue(ReferenceEquals(first, RenderedImageOf(magnifier)));
            }
        }

        [TestMethod]
        public void ACursorFullyOutsideTheSnapshotLeavesOnlyBackground()
        {
            using (var magnifier = new Magnifier())
            using (var snapshot = new Bitmap(64, 64, PixelFormat.Format24bppRgb))
            {
                FillWith(snapshot, Color.Red);

                magnifier.RenderFrom(snapshot, Point.Empty, new Point(5000, 5000));

                var image = RenderedImageOf(magnifier);
                var background = magnifier.BackColor.ToArgb();

                Assert.AreEqual(background, image.GetPixel(0, 0).ToArgb());
                Assert.AreEqual(background, image.GetPixel(image.Width / 2, image.Height / 2).ToArgb());
                Assert.AreEqual(background, image.GetPixel(image.Width - 1, image.Height - 1).ToArgb());
            }
        }

        [TestMethod]
        public void AnEdgeCursorKeepsTheInBoundsPixelsAndPadsTheRest()
        {
            using (var magnifier = new Magnifier())
            using (var snapshot = new Bitmap(64, 64, PixelFormat.Format24bppRgb))
            {
                FillWith(snapshot, Color.Red);

                // A cursor at the snapshot's origin puts three quarters of the viewport
                // outside it. The old path handed those negative coordinates straight to
                // CopyFromScreen.
                magnifier.RenderFrom(snapshot, Point.Empty, Point.Empty);

                var image = RenderedImageOf(magnifier);
                var viewport = MagnifierGeometry.ViewportSize(image.Size, MagnifierGeometry.Magnification);
                var region = MagnifierGeometry.Clip(
                    MagnifierGeometry.SourceRectangle(Point.Empty, viewport),
                    snapshot.Size,
                    MagnifierGeometry.DestinationRectangle(
                        image.Size, viewport, MagnifierGeometry.Magnification),
                    MagnifierGeometry.Magnification);

                Assert.IsFalse(region.IsEmpty, "part of the viewport must still be inside the snapshot");
                Assert.IsTrue(region.Destination.X > 2, "the overflow must have pushed the drawn part inward");

                // (dest.X+2, dest.Y+2) is right at the crosshair's inner ring corner (120,120)
                // when the cursor is at the snapshot origin, so sample a little deeper into
                // the drawn part to stay clear of the marker. The assertion's intent —
                // in-bounds pixels survive — is unchanged.
                Assert.AreEqual(
                    Color.Red.ToArgb(),
                    image.GetPixel(region.Destination.X + 12, region.Destination.Y + 12).ToArgb(),
                    "the in-bounds part must show snapshot pixels");

                // (dest.X-6, dest.Y-6) is in the background padding outside the drawn content
                // (content starts at the destination itself, but the sample must also clear
                // the crosshair's ring, which sits 2px in from the content's top-left edge).
                Assert.AreEqual(
                    magnifier.BackColor.ToArgb(),
                    image.GetPixel(region.Destination.X - 6, region.Destination.Y - 6).ToArgb(),
                    "the out-of-bounds part must be padded with the background");
            }
        }

        [TestMethod]
        public void TheCenterBlockStaysSnapshotColored()
        {
            // The marked source pixel and its 4x4 magnified block must never be painted
            // over by the crosshair — that is the whole point of leaving the centre open.
            using (var magnifier = new Magnifier())
            using (var snapshot = new Bitmap(64, 64, PixelFormat.Format24bppRgb))
            {
                FillWith(snapshot, Color.Red);

                magnifier.RenderFrom(snapshot, Point.Empty, new Point(32, 32));

                var image = RenderedImageOf(magnifier);
                var block = CenterBlockFor(image);

                for (var y = block.Top; y < block.Bottom; y++)
                {
                    for (var x = block.Left; x < block.Right; x++)
                    {
                        Assert.AreEqual(
                            Color.Red.ToArgb(),
                            image.GetPixel(x, y).ToArgb(),
                            "the marked block at (" + x + "," + y + ") must stay the snapshot color");
                    }
                }
            }
        }

        [TestMethod]
        public void TheCrosshairIsVisibleOnAWhiteBackground()
        {
            using (var magnifier = new Magnifier())
            using (var snapshot = new Bitmap(64, 64, PixelFormat.Format24bppRgb))
            {
                FillWith(snapshot, Color.White);

                magnifier.RenderFrom(snapshot, Point.Empty, new Point(32, 32));

                var image = RenderedImageOf(magnifier);
                var block = CenterBlockFor(image);

                Assert.IsTrue(
                    ContainsBleakPixel(image, Around(block)),
                    "the crosshair must show a dark pixel on a white image");
            }
        }

        [TestMethod]
        public void TheCrosshairIsVisibleOnABlackBackground()
        {
            using (var magnifier = new Magnifier())
            using (var snapshot = new Bitmap(64, 64, PixelFormat.Format24bppRgb))
            {
                FillWith(snapshot, Color.Black);

                magnifier.RenderFrom(snapshot, Point.Empty, new Point(32, 32));

                var image = RenderedImageOf(magnifier);
                var block = CenterBlockFor(image);

                Assert.IsTrue(
                    ContainsBrightPixel(image, Around(block)),
                    "the crosshair must show a bright pixel on a black image");
            }
        }

        [TestMethod]
        public void TheHorizontalLineIsOpenAtTheCentre()
        {
            // Row 122 is the light band, which runs across the whole destination except
            // where the 4x4 block sits. So the line covers the snapshot on both sides of
            // the centre, and the centre stays untouched.
            using (var magnifier = new Magnifier())
            using (var snapshot = new Bitmap(64, 64, PixelFormat.Format24bppRgb))
            {
                FillWith(snapshot, Color.Red);

                magnifier.RenderFrom(snapshot, Point.Empty, new Point(32, 32));

                var image = RenderedImageOf(magnifier);
                var destination = DestinationFor(image);
                var block = CenterBlockFor(image);
                var row = block.Top + 1; // 122: the light band

                Assert.AreEqual(
                    Color.White.ToArgb(),
                    image.GetPixel(destination.Left + 1, row).ToArgb(),
                    "the left part of the line must be marked");
                Assert.AreEqual(
                    Color.White.ToArgb(),
                    image.GetPixel(destination.Right - 2, row).ToArgb(),
                    "the right part of the line must be marked");

                Assert.AreEqual(
                    Color.Red.ToArgb(),
                    image.GetPixel(block.Left + 1, row).ToArgb(),
                    "the centre must stay open (not painted over)");
            }
        }

        [TestMethod]
        public void NoCrosshairPixelEscapesTheMagnifiedRegion()
        {
            using (var magnifier = new Magnifier())
            using (var snapshot = new Bitmap(64, 64, PixelFormat.Format24bppRgb))
            {
                FillWith(snapshot, Color.Red);

                magnifier.RenderFrom(snapshot, Point.Empty, new Point(32, 32));

                var image = RenderedImageOf(magnifier);
                var destination = DestinationFor(image);

                // Only the horizontal line (row 122/123) touches the destination's left and
                // right edges. On that row, the pixel just outside either edge must be the
                // background, and the pixel just inside must carry a mark.
                var markedRow = CenterBlockFor(image).Top + 1; // 122: the light band

                Assert.AreEqual(
                    magnifier.BackColor.ToArgb(),
                    image.GetPixel(destination.Left - 1, markedRow).ToArgb(),
                    "a pixel left of the destination must stay background");
                Assert.AreEqual(
                    magnifier.BackColor.ToArgb(),
                    image.GetPixel(destination.Right, markedRow).ToArgb(),
                    "a pixel right of the destination must stay background");

                Assert.AreNotEqual(
                    Color.Red.ToArgb(),
                    image.GetPixel(0, 0).ToArgb(),
                    "the origin margin must be background");
                Assert.AreNotEqual(
                    Color.Red.ToArgb(),
                    image.GetPixel(image.Width - 1, 0).ToArgb(),
                    "the far-right margin must be background");

                // The vertical band (the outer ring's left column) sits two pixels left of
                // the block edge. Sampling well inside the content confirms it is painted
                // inside the region, not out.
                var block = CenterBlockFor(image);
                Assert.AreNotEqual(
                    Color.Red.ToArgb(),
                    image.GetPixel(block.Left - 2, block.Top + 2).ToArgb(),
                    "the ring must be drawn inside the region");

                // Every pixel outside the destination rectangle is background; the crosshair
                // must never paint there. Scan the whole image so this can not drift.
                for (var y = 0; y < image.Height; y++)
                {
                    for (var x = 0; x < image.Width; x++)
                    {
                        var outside = x < destination.Left || x >= destination.Right
                            || y < destination.Top || y >= destination.Bottom;

                        if (outside)
                        {
                            Assert.IsFalse(
                                HasShadow(image, x, y),
                                "shadow pixel (" + x + "," + y + ") painted outside the magnified region");
                        }
                    }
                }
            }
        }

        [TestMethod]
        public void TheCrosshairIsDrawnEvenWhenTheSnapshotIsUnavailable()
        {
            // A cursor far outside the snapshot leaves only background, but the marker is a
            // function of the geometry, not of what was sampled — it must still be there.
            using (var magnifier = new Magnifier())
            using (var snapshot = new Bitmap(64, 64, PixelFormat.Format24bppRgb))
            {
                FillWith(snapshot, Color.Red);

                magnifier.RenderFrom(snapshot, Point.Empty, new Point(5000, 5000));

                var image = RenderedImageOf(magnifier);
                var block = CenterBlockFor(image);

                Assert.AreEqual(magnifier.BackColor.ToArgb(), image.GetPixel(block.Left, block.Top).ToArgb());

                // The inner ring is drawn white and the outer ring black around the block,
                // so at least one pixel near the centre differs from the background.
                Assert.IsTrue(
                    Scan(image, Around(block), c => c.ToArgb() != magnifier.BackColor.ToArgb()),
                    "the crosshair must be drawn even when the magnified content is empty");
            }
        }

        [TestMethod]
        public void TheCrosshairKeepsTheGdiBudgetFlat()
        {
            // The crosshair is drawn with System.Drawing shared brushes (Brushes.Black/White);
            // it must not allocate brushes, pens or bitmaps per frame. Reuse the same
            // GDI-handle harness as the general refresh test, with a cursor that keeps the
            // marker under it for the whole run.
            const int Iterations = 400;

            using (var magnifier = new Magnifier())
            using (var snapshot = new Bitmap(400, 400, PixelFormat.Format24bppRgb))
            {
                for (var i = 0; i < 20; i++)
                {
                    magnifier.RenderFrom(snapshot, Point.Empty, new Point(200, 200));
                }

                Settle();
                var before = GdiObjectCount();

                for (var i = 0; i < Iterations; i++)
                {
                    magnifier.RenderFrom(snapshot, Point.Empty, new Point(200 + (i % 3), 200 + (i % 5)));
                }

                Settle();
                var after = GdiObjectCount();

                Assert.IsTrue(
                    (long)after - before < Iterations / 10,
                    $"GDI objects grew by {(long)after - before} across {Iterations} renders ({before} -> {after}).");
            }
        }

        [TestMethod]
        public void TheWindowLetsMouseMessagesThrough()
        {
            // Following the cursor parks the window right next to it, so without
            // WS_EX_TRANSPARENT it would swallow the capture form's drag messages.
            using (var magnifier = new Magnifier())
            {
                var exStyle = GetWindowLong(magnifier.Handle, GWL_EXSTYLE);

                Assert.AreNotEqual(0, exStyle, "GetWindowLong must succeed for this assertion to mean anything.");
                Assert.AreEqual(
                    WS_EX_TRANSPARENT,
                    exStyle & WS_EX_TRANSPARENT,
                    "the magnifier must let mouse messages through to the capture form beneath it");
            }
        }

        [TestMethod]
        public void DisposingTwiceDoesNotThrow()
        {
            // Component.Dispose() has no reentrancy guard, and Close() followed by
            // Dispose() walks the cleanup path twice.
            var magnifier = new Magnifier();

            using (var snapshot = new Bitmap(64, 64, PixelFormat.Format24bppRgb))
            {
                magnifier.RenderFrom(snapshot, Point.Empty, new Point(32, 32));
            }

            magnifier.Dispose();
            magnifier.Dispose();
        }

        static Bitmap RenderedImageOf(Magnifier magnifier)
        {
            var found = magnifier.Controls.Find("pictureBox1", true);

            Assert.AreEqual(1, found.Length, "the magnifier must still hold exactly one picture box");

            return (Bitmap)((PictureBox)found[0]).Image;
        }

        /// <summary>The 4x4 magnified source pixel the crosshair centres on, in image
        /// coordinates. It is the viewport centre, so (61x61 viewport @ factor 4) puts the
        /// top-left corner at destination.X + 30*4 = 121.</summary>
        static Rectangle CenterBlockFor(Bitmap image)
        {
            var destination = DestinationFor(image);
            var viewport = MagnifierGeometry.ViewportSize(image.Size, MagnifierGeometry.Magnification);

            return new Rectangle(
                destination.X + (viewport.Width / 2) * MagnifierGeometry.Magnification,
                destination.Y + (viewport.Height / 2) * MagnifierGeometry.Magnification,
                MagnifierGeometry.Magnification,
                MagnifierGeometry.Magnification);
        }

        static Rectangle DestinationFor(Bitmap image)
        {
            var viewport = MagnifierGeometry.ViewportSize(image.Size, MagnifierGeometry.Magnification);

            return MagnifierGeometry.DestinationRectangle(
                image.Size, viewport, MagnifierGeometry.Magnification);
        }

        static Rectangle Around(Rectangle rectangle)
        {
            return Rectangle.Inflate(rectangle, MagnifierGeometry.MarkerOuterRingOffset, MagnifierGeometry.MarkerOuterRingOffset);
        }

        static bool ContainsBleakPixel(Bitmap bitmap, Rectangle region)
        {
            return Scan(bitmap, region, c => c.R + c.G + c.B < 96);
        }

        static bool ContainsBrightPixel(Bitmap bitmap, Rectangle region)
        {
            return Scan(bitmap, region, c => c.R + c.G + c.B > 720);
        }

        /// <summary>A red pixel or the control-colour background both sit in the middle range
        /// (red=255, background≈666). The mark bands are pure black (0) or pure white (765),
        /// so anything far outside that middle band is a mark — which must never appear
        /// outside the destination rectangle.</summary>
        static bool HasShadow(Bitmap bitmap, int x, int y)
        {
            var color = bitmap.GetPixel(x, y);

            return color.R + color.G + color.B < 96 || color.R + color.G + color.B > 720;
        }

        static bool Scan(Bitmap bitmap, Rectangle region, Func<Color, bool> test)
        {
            for (var y = region.Top; y < region.Bottom; y++)
            {
                for (var x = region.Left; x < region.Right; x++)
                {
                    if (test(bitmap.GetPixel(x, y)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        static void FillWith(Bitmap bitmap, Color color)
        {
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(color);
            }
        }

        static void Settle()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }
}
