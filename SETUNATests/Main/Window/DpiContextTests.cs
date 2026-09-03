using System;
using System.Drawing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SETUNA.Main.Window;

namespace SETUNA.Main.Window.Tests
{
    [TestClass]
    public class DpiContextTests
    {
        [TestMethod]
        public void SnapshotCarriesPhysicalBoundsAndIndependentAxisScale()
        {
            var snapshot = new MonitorSnapshot(
                new IntPtr(42),
                "\\\\.\\DISPLAY2",
                new Rectangle(-1920, -200, 1920, 1080),
                new Rectangle(-1920, -160, 1920, 1040),
                120,
                144,
                false);

            Assert.AreEqual(new IntPtr(42), snapshot.Handle);
            Assert.AreEqual(new Rectangle(-1920, -200, 1920, 1080), snapshot.NativeBounds);
            Assert.AreEqual(new Rectangle(-1920, -160, 1920, 1040), snapshot.WorkingArea);
            Assert.AreEqual(1.25d, snapshot.ScaleX, 0.0001d);
            Assert.AreEqual(1.5d, snapshot.ScaleY, 0.0001d);
            Assert.IsTrue(snapshot.IsAvailable);
        }

        [TestMethod]
        public void LogicalAndPhysicalConversionsUseBothDpiAxes()
        {
            var context = DpiContext.FromDpi(120, 144);

            Assert.AreEqual(new Point(13, 15), context.LogicalToPhysical(new Point(10, 10)));
            Assert.AreEqual(new Size(13, 15), context.LogicalToPhysical(new Size(10, 10)));
            Assert.AreEqual(new Rectangle(13, 15, 13, 15), context.LogicalToPhysical(new Rectangle(10, 10, 10, 10)));
            Assert.AreEqual(new Point(8, 7), context.PhysicalToLogical(new Point(10, 10)));
        }

        [TestMethod]
        public void NegativeCoordinatesAndHalfPixelsRoundAwayFromZero()
        {
            var context = DpiContext.FromDpi(120, 120);

            Assert.AreEqual(new Point(-19, -13), context.LogicalToPhysical(new Point(-15, -10)));
            Assert.AreEqual(-1, DpiContext.Scale(-1, 0.5d));
            Assert.AreEqual(1, DpiContext.Scale(1, 0.5d));
        }

        [TestMethod]
        public void InvalidDpiIsExplicitAndDoesNotInventA96Scale()
        {
            var unavailable = MonitorSnapshot.Unavailable;
            var context = new DpiContext(unavailable);

            Assert.IsFalse(context.IsAvailable);
            Assert.AreEqual(17, context.LogicalToPhysicalLengthX(17));
            Assert.AreEqual(-23, context.PhysicalToLogicalLengthY(-23));
            Assert.AreEqual(0d, unavailable.ScaleX);
            Assert.AreEqual(0d, unavailable.ScaleY);
            Assert.IsFalse(context.TryLogicalToPhysical(Point.Empty, out _));
        }

        [TestMethod]
        public void TryConversionsRejectNonPositiveRectangles()
        {
            var context = DpiContext.FromDpi(168, 168);

            Assert.IsFalse(context.TryLogicalToPhysical(new Rectangle(0, 0, 0, 10), out _));
            Assert.IsFalse(context.TryPhysicalToLogical(new Rectangle(0, 0, 10, -1), out _));
        }

        [TestMethod]
        public void APhysicalLogicalRoundTripStaysWithinOnePixel()
        {
            var context = DpiContext.FromDpi(168, 168);
            var original = new Rectangle(-1001, -777, 641, 359);

            var physical = context.LogicalToPhysical(original);
            var roundTrip = context.PhysicalToLogical(physical);

            Assert.IsTrue(Math.Abs(roundTrip.X - original.X) <= 1);
            Assert.IsTrue(Math.Abs(roundTrip.Y - original.Y) <= 1);
            Assert.IsTrue(Math.Abs(roundTrip.Width - original.Width) <= 1);
            Assert.IsTrue(Math.Abs(roundTrip.Height - original.Height) <= 1);
        }

        /// <summary>
        /// 覆盖 Windows 显示设置能选到的整档缩放（100% 到 300%）。96 DPI 必须是恒等变换——
        /// 逻辑单位的定义就是 96 DPI 下的像素，那一档上多做任何换算都是错的。
        /// </summary>
        [TestMethod]
        public void EverySupportedScaleStepRoundTripsWithinOnePixel()
        {
            var original = new Rectangle(-1600, -900, 415, 180);

            foreach (var dpi in new[] { 96, 120, 144, 168, 192, 240, 288 })
            {
                var context = DpiContext.FromDpi(dpi, dpi);
                var physical = context.LogicalToPhysical(original);
                var roundTrip = context.PhysicalToLogical(physical);

                if (dpi == DpiContext.BaseDpi)
                {
                    Assert.AreEqual(original, physical, "96 DPI must be an identity conversion.");
                }

                Assert.IsTrue(
                    Math.Abs(roundTrip.X - original.X) <= 1
                    && Math.Abs(roundTrip.Y - original.Y) <= 1
                    && Math.Abs(roundTrip.Width - original.Width) <= 1
                    && Math.Abs(roundTrip.Height - original.Height) <= 1,
                    "Round trip drifted more than one pixel at " + dpi + " DPI: " + roundTrip);
            }
        }
        /// <summary>
        /// <see cref="DpiContext.ScaleSize"/> 换算的是「两档 DPI 之比」，不是逻辑/物理转换：
        /// 调用方手里是某一档 DPI 下量出来的像素尺寸，要的正是两档之间的比例。任一档不可用或
        /// 两档相同时必须原样返回，让调用方保留已有的值而不是拿一个猜测去换算。
        /// </summary>
        [TestMethod]
        public void ScalingASizeBetweenTwoDpisUsesTheirRatio()
        {
            var size = new Size(20, 39);

            Assert.AreEqual(new Size(35, 68), DpiContext.ScaleSize(size, 168, 96));
            Assert.AreEqual(new Size(11, 22), DpiContext.ScaleSize(size, 96, 168));
            Assert.AreEqual(size, DpiContext.ScaleSize(size, 168, 168), "Equal DPIs must not convert.");
            Assert.AreEqual(size, DpiContext.ScaleSize(size, 0, 168), "An unreadable target DPI must not convert.");
            Assert.AreEqual(size, DpiContext.ScaleSize(size, 168, 0), "An unreadable source DPI must not convert.");
        }
    }
}
