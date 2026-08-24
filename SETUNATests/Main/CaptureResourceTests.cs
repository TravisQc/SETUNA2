using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SETUNA.Main;

namespace SETUNA.Main.Tests
{
    /// <summary>
    /// Covers the cleanup contract of <c>CaptureForm.CopyFromScreen</c>.
    /// <para>
    /// The method is <c>public static</c>, so it can be driven directly.
    /// <c>Mainform.Instance</c> is null in this host, so a capture that gets as far
    /// as the cursor-overlay step fails there and returns false — which is itself a
    /// cleanup path worth exercising.
    /// </para>
    /// <para>
    /// Note on the DC pairing: <c>GetDC</c> is now released with <c>ReleaseDC</c>
    /// rather than <c>DeleteDC</c> because MSDN forbids that pairing and it does not
    /// work for window DCs. Measurement on Windows 11 showed <c>DeleteDC</c> did in
    /// fact free the *screen* DC (handles were reused across 2000 iterations), so
    /// that change is API-contract hygiene and not a leak fix — there is deliberately
    /// no "handle count stays flat" assertion here, because it would pass equally
    /// before and after the change and would only look like verification.
    /// </para>
    /// </summary>
    [TestClass]
    public class CaptureResourceTests
    {
        const uint GR_GDIOBJECTS = 0;

        [DllImport("user32.dll")]
        static extern uint GetGuiResources(IntPtr hProcess, uint uiFlags);

        static uint GdiObjectCount()
        {
            return GetGuiResources(Process.GetCurrentProcess().Handle, GR_GDIOBJECTS);
        }

        [TestMethod]
        public void ACaptureIntoAnIndexedBitmapReturnsFalseInsteadOfThrowing()
        {
            // Graphics.FromImage rejects indexed pixel formats, so the local
            // Graphics is never assigned. The old cleanup called
            // graphics.Dispose() unconditionally from a finally block, so a
            // NullReferenceException escaped the method and replaced the real
            // error. It must report failure instead.
            using (var indexed = new Bitmap(16, 16, PixelFormat.Format8bppIndexed))
            {
                var succeeded = CaptureForm.CopyFromScreen(indexed, Point.Empty);

                Assert.IsFalse(succeeded);
            }
        }

        [TestMethod]
        public void RepeatedFailedCapturesKeepReportingFailureWithoutThrowing()
        {
            using (var indexed = new Bitmap(16, 16, PixelFormat.Format8bppIndexed))
            {
                for (var i = 0; i < 200; i++)
                {
                    Assert.IsFalse(CaptureForm.CopyFromScreen(indexed, Point.Empty), "iteration " + i);
                }
            }
        }

        [TestMethod]
        public void ACaptureIntoADisposedBitmapReturnsFalseInsteadOfThrowing()
        {
            var disposed = new Bitmap(16, 16, PixelFormat.Format24bppRgb);
            disposed.Dispose();

            Assert.IsFalse(CaptureForm.CopyFromScreen(disposed, Point.Empty));
        }

        [TestMethod]
        public void ACaptureIntoANullImageReturnsFalseInsteadOfThrowing()
        {
            Assert.IsFalse(CaptureForm.CopyFromScreen(null, Point.Empty));
        }

        [TestMethod]
        public void RepeatedCapturesDoNotAccumulateGdiObjectsUnbounded()
        {
            // A weak but non-vacuous backstop: it would catch a *new* per-call leak
            // introduced by future edits to this method (for example dropping the
            // ReleaseDC call entirely, which measurement showed does leak one
            // handle per call). It is not evidence about the DeleteDC/ReleaseDC
            // change itself.
            const int Iterations = 300;

            using (var target = new Bitmap(32, 32, PixelFormat.Format24bppRgb))
            {
                for (var i = 0; i < 20; i++)
                {
                    CaptureForm.CopyFromScreen(target, Point.Empty);
                }

                Settle();
                var before = GdiObjectCount();
                Assert.IsTrue(before > 0, "GetGuiResources must report a usable count for this assertion to mean anything.");

                for (var i = 0; i < Iterations; i++)
                {
                    CaptureForm.CopyFromScreen(target, Point.Empty);
                }

                Settle();
                var after = GdiObjectCount();

                Assert.IsTrue(
                    (long)after - before < Iterations / 10,
                    $"GDI objects grew by {(long)after - before} across {Iterations} captures ({before} -> {after}).");
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
