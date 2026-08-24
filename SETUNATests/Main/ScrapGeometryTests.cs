using System.Drawing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SETUNA.Main;

namespace SETUNA.Main.Tests
{
    /// <summary>
    /// Pins the scrap-window draw region and scale bounds. The non-transparent
    /// paint branch used to pass the full client size as the destination while the
    /// transparent branch subtracted the padding, so a non-zero margin scaled the
    /// image up and clipped its bottom-right corner. The scale floor was -200,
    /// which yields negative widths and heights.
    /// </summary>
    [TestClass]
    public class ScrapGeometryTests
    {
        [TestMethod]
        public void TheScaleFloorIsPositive()
        {
            Assert.IsTrue(ScrapGeometry.MinimumScale > 0);
        }

        [TestMethod]
        public void NegativeAndZeroScalesAreClampedToTheFloor()
        {
            Assert.AreEqual(ScrapGeometry.MinimumScale, ScrapGeometry.ClampScale(0));
            Assert.AreEqual(ScrapGeometry.MinimumScale, ScrapGeometry.ClampScale(-1));
            Assert.AreEqual(ScrapGeometry.MinimumScale, ScrapGeometry.ClampScale(-200));
            Assert.AreEqual(ScrapGeometry.MinimumScale, ScrapGeometry.ClampScale(int.MinValue));
        }

        [TestMethod]
        public void OversizedScalesAreClampedToTheCeiling()
        {
            Assert.AreEqual(ScrapGeometry.MaximumScale, ScrapGeometry.ClampScale(ScrapGeometry.MaximumScale + 1));
            Assert.AreEqual(ScrapGeometry.MaximumScale, ScrapGeometry.ClampScale(int.MaxValue));
        }

        [TestMethod]
        public void InRangeScalesAreLeftAlone()
        {
            Assert.AreEqual(1, ScrapGeometry.ClampScale(1));
            Assert.AreEqual(100, ScrapGeometry.ClampScale(100));
            Assert.AreEqual(ScrapGeometry.MaximumScale, ScrapGeometry.ClampScale(ScrapGeometry.MaximumScale));
        }

        [TestMethod]
        public void NoScaleEverProducesANonPositiveOuterSize()
        {
            var image = new Size(120, 80);

            foreach (var padding in new[] { 0, 1, 4, 16 })
            {
                foreach (var scale in new[] { int.MinValue, -1000, -200, -1, 0, 1, 50, 100, 200, 1000, int.MaxValue })
                {
                    var size = ScrapGeometry.ScaledOuterSize(image, scale, padding);

                    Assert.IsTrue(size.Width > 0, "width " + size.Width + " at scale " + scale + " padding " + padding);
                    Assert.IsTrue(size.Height > 0, "height " + size.Height + " at scale " + scale + " padding " + padding);
                }
            }
        }

        [TestMethod]
        public void AHundredPercentScaleReproducesTheImagePlusPadding()
        {
            var size = ScrapGeometry.ScaledOuterSize(new Size(120, 80), 100, 5);

            Assert.AreEqual(120 + 10, size.Width);
            Assert.AreEqual(80 + 10, size.Height);
        }

        [TestMethod]
        public void EvenAOnePixelImageAtTheFloorStaysPositive()
        {
            var size = ScrapGeometry.ScaledOuterSize(new Size(1, 1), ScrapGeometry.MinimumScale, 1);

            Assert.IsTrue(size.Width > 0);
            Assert.IsTrue(size.Height > 0);
        }

        [TestMethod]
        public void TheDrawDestinationSubtractsPaddingOnBothSides()
        {
            var destination = ScrapGeometry.ImageDestination(200, 150, 4);

            Assert.AreEqual(4, destination.X);
            Assert.AreEqual(4, destination.Y);
            Assert.AreEqual(200 - 8, destination.Width);
            Assert.AreEqual(150 - 8, destination.Height);
        }

        [TestMethod]
        public void ZeroPaddingFillsTheWholeClientArea()
        {
            var destination = ScrapGeometry.ImageDestination(200, 150, 0);

            Assert.AreEqual(new Rectangle(0, 0, 200, 150), destination);
        }

        [TestMethod]
        public void TheDrawDestinationNeverExtendsPastTheClientArea()
        {
            // The regression: the non-transparent branch passed Width/Height as the
            // destination size at offset (all, all), reaching Width + all.
            foreach (var padding in new[] { 0, 1, 4, 16 })
            {
                var destination = ScrapGeometry.ImageDestination(200, 150, padding);

                Assert.IsTrue(destination.Right <= 200, "right edge " + destination.Right + " at padding " + padding);
                Assert.IsTrue(destination.Bottom <= 150, "bottom edge " + destination.Bottom + " at padding " + padding);
            }
        }
    }
}
