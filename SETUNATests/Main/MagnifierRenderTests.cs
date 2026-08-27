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

                Assert.AreEqual(
                    Color.Red.ToArgb(),
                    image.GetPixel(region.Destination.X + 2, region.Destination.Y + 2).ToArgb(),
                    "the in-bounds part must show snapshot pixels");
                Assert.AreEqual(
                    magnifier.BackColor.ToArgb(),
                    image.GetPixel(region.Destination.X - 2, region.Destination.Y - 2).ToArgb(),
                    "the out-of-bounds part must be padded with the background");
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
