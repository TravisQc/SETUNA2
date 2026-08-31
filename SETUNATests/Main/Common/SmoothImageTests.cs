using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SETUNA.Main;
using SETUNA.Main.Common;
using SETUNA.Main.Option;
using SETUNA.Main.Tests;

namespace SETUNATests.Main.Common
{
    /// <summary>
    /// Pins the smooth-image drawing used for the options dialog's logo.
    /// <para>
    /// Two things have to hold. First, the destination rectangle must match
    /// <see cref="PictureBoxSizeMode.Zoom"/> exactly, so replacing the PictureBox's own
    /// drawing changes resampling quality and nothing about layout. Second, the resample
    /// must actually be sharper than the GDI+ default — that default is what made the
    /// logo look out of focus on the 100% monitor, where the control is 266x360 for a
    /// 170x370 bitmap and the zoom factor lands at 0.973.
    /// </para>
    /// </summary>
    [TestClass]
    public class SmoothImageTests
    {
        [TestMethod]
        public void FitMatchesTheZoomRectangleOfARealPictureBox()
        {
            // The two sizes the options dialog actually produces: 266x360 at 96 DPI and
            // 488x630 at 168 DPI, both holding the 170x370 OptionBG bitmap.
            foreach (var bounds in new[] { new Size(266, 360), new Size(488, 630) })
            {
                using (var bitmap = new Bitmap(170, 370))
                using (var box = new PictureBox())
                {
                    box.SizeMode = PictureBoxSizeMode.Zoom;
                    box.Image = bitmap;
                    box.ClientSize = bounds;

                    Assert.AreEqual(
                        ZoomRectangleOf(box),
                        SmoothImage.Fit(bitmap.Size, box.ClientSize),
                        "zoom rectangle for " + bounds);
                }
            }
        }

        [TestMethod]
        public void FitCentresTheImageAndKeepsItInside()
        {
            var target = SmoothImage.Fit(new Size(170, 370), new Size(266, 360));

            // 360/370 is the binding ratio, so height fills and width is letterboxed.
            Assert.AreEqual(360, target.Height);
            Assert.AreEqual(165, target.Width);
            Assert.AreEqual((266 - 165) / 2, target.X);
            Assert.AreEqual(0, target.Y);
        }

        [TestMethod]
        public void FitReturnsEmptyForUnusableSizes()
        {
            Assert.AreEqual(Rectangle.Empty, SmoothImage.Fit(new Size(0, 370), new Size(266, 360)));
            Assert.AreEqual(Rectangle.Empty, SmoothImage.Fit(new Size(170, 370), new Size(266, 0)));
            Assert.AreEqual(Rectangle.Empty, SmoothImage.Fit(new Size(170, -1), new Size(266, 360)));
        }

        [TestMethod]
        public void DrawFittedResamplesTheLogoMoreSharplyThanTheGdiPlusDefault()
        {
            // The real asset at the real size: OptionBG is 170x370 and the control is 266x360
            // on a 100% monitor, so the zoom factor is 0.973 — near enough to 1 that the GDI+
            // default blends every pixel with its neighbour and the whole logo goes soft.
            var bounds = new Size(266, 360);

            using (var source = (Image)SETUNA.Properties.Resources.OptionBG)
            {
                var target = SmoothImage.Fit(source.Size, bounds);

                var byDefault = Sharpness(Render(bounds, g => g.DrawImage(source, target)), target);
                var bySmooth = Sharpness(Render(bounds, g => SmoothImage.DrawFitted(g, source, bounds)), target);

                // Measured: 2.22 by default, 2.85 smoothed. The margin is well inside that gap
                // so the test is about the mode change, not about GDI+ minutiae.
                Assert.IsTrue(
                    bySmooth > byDefault * 1.1,
                    "expected the smoothed resample to be sharper, got " + bySmooth + " vs " + byDefault);
            }
        }

        static Bitmap Render(Size size, Action<Graphics> draw)
        {
            var bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);

            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.White);
                draw(g);
            }

            return bitmap;
        }

        /// <summary>
        /// Mean absolute Laplacian over <paramref name="area"/>. Unlike a first-derivative
        /// measure it is not conserved when an edge is smeared, so it actually distinguishes
        /// a soft resample from a sharp one.
        /// </summary>
        static double Sharpness(Bitmap bitmap, Rectangle area)
        {
            using (bitmap)
            {
                var total = 0.0;
                var count = 0;

                for (var y = area.Top + 1; y < area.Bottom - 1; y++)
                {
                    for (var x = area.Left + 1; x < area.Right - 1; x++)
                    {
                        total += Math.Abs(
                            4 * bitmap.GetPixel(x, y).R
                            - bitmap.GetPixel(x - 1, y).R
                            - bitmap.GetPixel(x + 1, y).R
                            - bitmap.GetPixel(x, y - 1).R
                            - bitmap.GetPixel(x, y + 1).R);
                        count++;
                    }
                }

                return count == 0 ? 0 : total / count;
            }
        }

        /// <summary>
        /// The forms that carry a scaled bitmap have to actually route it through
        /// <see cref="SmoothImage"/>. Asserted on the pixels the control really paints, so
        /// the wiring cannot rot into a no-op: both dialogs' logos must come out identical
        /// to a high-quality bicubic resample of the source asset at the control's size.
        /// </summary>
        [TestMethod]
        public void TheFormsCarryingAScaledBitmapPaintItSmoothly()
        {
            var cases = new[]
            {
                new { Form = (Form)new OptionForm(SetunaOption.GetDefaultOption()), Source = (Image)SETUNA.Properties.Resources.OptionBG },
                new { Form = (Form)new SplashForm(), Source = (Image)SETUNA.Properties.Resources.Logo }
            };

            foreach (var one in cases)
            {
                using (one.Form)
                using (one.Source)
                {
                    LayoutSnapshot.ShowOffScreen(one.Form);

                    var box = FindPictureBox(one.Form);
                    Assert.IsNotNull(box, one.Form.GetType().Name + " has no picture box holding a bitmap.");

                    using (var painted = new Bitmap(box.Width, box.Height, PixelFormat.Format32bppArgb))
                    {
                        box.DrawToBitmap(painted, new Rectangle(0, 0, box.Width, box.Height));

                        var target = SmoothImage.Fit(one.Source.Size, box.ClientSize);
                        Assert.IsFalse(target.IsEmpty, one.Form.GetType().Name + ": nothing to draw.");

                        var expected = Render(box.ClientSize, g => SmoothImage.DrawFitted(g, one.Source, box.ClientSize));

                        AssertSamePixels(painted, expected, target, one.Form.GetType().Name);
                    }
                }
            }
        }

        /// <summary>The first picture box below <paramref name="root"/> that shows a bitmap.</summary>
        static PictureBox FindPictureBox(Control root)
        {
            foreach (Control child in root.Controls)
            {
                var box = child as PictureBox;

                // Image is null exactly because SmoothImage took the bitmap off the control,
                // so the search cannot key on it; the colour swatches this form also carries
                // are sized like swatches and never zoomed.
                if (box != null && box.SizeMode == PictureBoxSizeMode.Zoom)
                {
                    return box;
                }

                var found = FindPictureBox(child);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>
        /// Compares the interior of <paramref name="area"/> only: the outermost ring is where
        /// the control's own background shows through, and that is not what this measures.
        /// </summary>
        static void AssertSamePixels(Bitmap actual, Bitmap expected, Rectangle area, string what)
        {
            using (expected)
            {
                var differing = 0;

                for (var y = area.Top + 2; y < area.Bottom - 2; y++)
                {
                    for (var x = area.Left + 2; x < area.Right - 2; x++)
                    {
                        if (actual.GetPixel(x, y) != expected.GetPixel(x, y))
                        {
                            differing++;
                        }
                    }
                }

                Assert.AreEqual(
                    0,
                    differing,
                    what + ": " + differing + " of " + (area.Width * area.Height)
                        + " logo pixels differ from a high-quality resample, so the bitmap is not going through SmoothImage.");
            }
        }

        // PLACEHOLDER_END

        /// <summary>
        /// The rectangle <c>PictureBox</c> would draw into, read from the control itself so the
        /// expectation cannot drift away from the framework's own arithmetic.
        /// </summary>
        static Rectangle ZoomRectangleOf(PictureBox box)
        {
            var property = typeof(PictureBox).GetProperty(
                "ImageRectangle",
                System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Public);

            Assert.IsNotNull(property, "PictureBox.ImageRectangle is gone; the fit rule needs re-checking");

            return (Rectangle)property.GetValue(box, null);
        }
    }
}
