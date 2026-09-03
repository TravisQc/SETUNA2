using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SETUNA.Main;
using SETUNA.Main.Tests;
using SETUNA.Main.Window;

namespace SETUNATests.Main.Window
{
    /// <summary>
    /// A DPI change must not alter one pixel of a physical surface.
    /// <para>
    /// The metric assertions elsewhere (<c>BaseFormDpiNotificationTests</c>, and
    /// <c>probes/SurfaceGeometryProbe</c> on the real desktop) compare sizes; this class
    /// compares the pixels themselves, because equal outer bounds are necessary but not
    /// sufficient. A surface could keep its window rectangle and still resample its bitmap
    /// into it — the scrap's paint path runs the image through
    /// <c>InterpolationMode.HighQualityBicubic</c>, so a destination rectangle that moved by
    /// one pixel would come back visibly softer with every size unchanged.
    /// </para>
    /// <para>
    /// The comparison is made through <c>ScrapBase.GetViewImage()</c>, the same call the
    /// application uses to hand a scrap to the clipboard and the cache, so what is pinned is
    /// what a user actually receives rather than a re-implementation of it.
    /// </para>
    /// </summary>
    [TestClass]
    public class PhysicalSurfacePixelTests
    {
        const int LowDpi = 96;
        const int HighDpi = 168;

        /// <summary>
        /// Off the side of every monitor but at a positive origin, matching the rest of the
        /// suite. <see cref="NegativeOrigin"/> is the other case this class needs.
        /// </summary>
        static readonly Point OffScreen = new Point(30000, 30000);

        /// <summary>
        /// A monitor left of and above the primary reports negative virtual-screen
        /// coordinates, and the rectangle Windows suggests on a DPI change scales them —
        /// so the guard has to write back a negative origin, not merely a size.
        /// </summary>
        static readonly Point NegativeOrigin = new Point(-30000, -30000);

        [TestMethod]
        public void AScrapKeepsEveryPixelAcrossA96To168TransitionWhileADialogDoesNot()
        {
            StaThread.Run(() =>
            {
                using (var scrap = ScrapShowing(OffScreen))
                using (var dialog = new SETUNA.Main.StyleItems.ToolBoxForm())
                {
                    LayoutSnapshot.ShowOffScreen(dialog);

                    var before = ViewPixels(scrap);
                    var dialogBefore = ClientPixels(dialog);

                    SyntheticDpiChange.Send(scrap, HighDpi);
                    SyntheticDpiChange.Send(dialog, HighDpi);

                    AssertPixelsEqual(before, ViewPixels(scrap), "the scrap's rendered pixels");
                    Assert.AreNotEqual(
                        dialogBefore.Length,
                        ClientPixels(dialog).Length,
                        "The logical control group was not resized, so this host is ignoring the suggested "
                            + "rectangle and the assertion above proves nothing.");
                }
            });
        }

        [TestMethod]
        public void AScrapComesBackUnchangedFromA168To96RoundTrip()
        {
            StaThread.Run(() =>
            {
                using (var scrap = ScrapShowing(OffScreen))
                {
                    SyntheticDpiChange.Send(scrap, HighDpi);
                    var atHighDpi = ViewPixels(scrap);

                    SyntheticDpiChange.Send(scrap, LowDpi);

                    AssertPixelsEqual(atHighDpi, ViewPixels(scrap), "the scrap's pixels after the return trip");
                }
            });
        }

        /// <summary>
        /// The scenario <c>scrap-window-rendering</c> spells out: a surface on a monitor whose
        /// origin is negative. Scaling a negative coordinate moves it further from zero, so a
        /// guard that only restored the size would leave the window somewhere else entirely —
        /// and a capture taken from it would read the wrong part of the desktop.
        /// </summary>
        [TestMethod]
        public void AScrapOnAMonitorWithANegativeOriginKeepsItsPlaceAndItsPixels()
        {
            StaThread.Run(() =>
            {
                using (var scrap = ScrapShowing(NegativeOrigin))
                {
                    var placeBefore = scrap.Bounds;
                    var before = ViewPixels(scrap);

                    Assert.IsTrue(placeBefore.Left < 0 && placeBefore.Top < 0, "Precondition: a negative origin.");

                    SyntheticDpiChange.Send(scrap, HighDpi);

                    Assert.AreEqual(placeBefore, scrap.Bounds, "The suggested rectangle moved a physical surface.");
                    AssertPixelsEqual(before, ViewPixels(scrap), "the scrap's pixels at a negative origin");

                    SyntheticDpiChange.Send(scrap, LowDpi);

                    Assert.AreEqual(placeBefore, scrap.Bounds);
                    AssertPixelsEqual(before, ViewPixels(scrap), "the scrap's pixels after the return trip");
                }
            });
        }

        /// <summary>
        /// The source bitmap is the capture the user took: a DPI change may not resample it,
        /// nor may the window's scale value move. Checked separately from the rendered pixels
        /// because the two fail independently — the paint path could stretch the image into an
        /// unchanged window, or the image could be rewritten under an unchanged rendering.
        /// </summary>
        [TestMethod]
        public void ADpiChangeNeverResamplesTheCapturedBitmapItself()
        {
            StaThread.Run(() =>
            {
                using (var scrap = ScrapShowing(OffScreen))
                {
                    var before = Pixels((Bitmap)scrap.Image);
                    var scaleBefore = scrap.Scale;

                    SyntheticDpiChange.Send(scrap, HighDpi);
                    AssertPixelsEqual(before, Pixels((Bitmap)scrap.Image), "the captured bitmap");

                    SyntheticDpiChange.Send(scrap, LowDpi);
                    AssertPixelsEqual(before, Pixels((Bitmap)scrap.Image), "the captured bitmap after the return trip");

                    Assert.AreEqual(
                        scaleBefore, scrap.Scale, "The scrap's scale value is the user's, not the monitor's.");
                }
            });
        }

        /// <summary>
        /// The capture overlay's own decorations, on the same message. It draws 1px lines and a
        /// text readout at fixed pixel offsets, so it has no bitmap to protect — what would
        /// break is the window being resized under drawing that is not.
        /// </summary>
        [TestMethod]
        public void ACaptureReadoutIsPixelIdenticalAcrossATransition()
        {
            using (var info = new CaptureInfo())
            {
                LayoutSnapshot.ShowOffScreen(info);

                var before = ClientPixels(info);

                SyntheticDpiChange.Send(info, HighDpi);
                AssertPixelsEqual(before, ClientPixels(info), "the capture readout's pixels");

                SyntheticDpiChange.Send(info, LowDpi);
                AssertPixelsEqual(before, ClientPixels(info), "the capture readout's pixels after the return trip");
            }
        }

        /// <summary>
        /// A scrap showing <see cref="SampleImage"/>, realised at <paramref name="origin"/>.
        /// The image is what makes the comparison mean something: a flat fill would compare
        /// equal however badly it was resampled.
        /// <para>
        /// Every caller runs inside <see cref="StaThread"/>: <c>ScrapBase</c>'s designer sets
        /// <c>AllowDrop</c>, so realizing its handle calls <c>Control.SetAcceptDrops</c>, which
        /// needs OLE and therefore an STA. On the host's MTA threads it throws, and WinForms
        /// turns that into a modal dialog on the message pump — the suite would report a pass
        /// while waiting for someone to click Continue.
        /// </para>
        /// </summary>
        static ScrapBase ScrapShowing(Point origin)
        {
            Assert.AreEqual(
                System.Threading.ApartmentState.STA,
                System.Threading.Thread.CurrentThread.GetApartmentState(),
                "A scrap must be realised on an STA thread; see StaThread. Without this the failure"
                    + " arrives as a modal dialog instead of a failed assertion.");

            var scrap = new ScrapBase();
            try
            {
                using (var image = SampleImage())
                {
                    scrap.Image = image;
                }

                scrap.StartPosition = FormStartPosition.Manual;
                scrap.Location = origin;
                scrap.Show();
                Application.DoEvents();
                scrap.Hide();

                Assert.AreEqual(
                    LowDpi,
                    scrap.CurrentDpiContext.DpiX,
                    "This host carries no manifest, so a window is born at 96 DPI and the transitions"
                        + " below really are 96↔168. If that ever changes, the constants have to.");

                return scrap;
            }
            catch
            {
                scrap.Dispose();
                throw;
            }
        }

        /// <summary>
        /// 137x89 — the size measured on the real desktop in task 7.3, where it became 78x51.
        /// Every pixel differs from its neighbours in at least one channel so that any
        /// resampling, however slight, changes bytes.
        /// </summary>
        static Bitmap SampleImage()
        {
            var image = new Bitmap(137, 89, PixelFormat.Format24bppRgb);

            for (var y = 0; y < image.Height; y++)
            {
                for (var x = 0; x < image.Width; x++)
                {
                    image.SetPixel(x, y, Color.FromArgb((x * 7) % 256, (y * 11) % 256, (x + y * 3) % 256));
                }
            }

            return image;
        }

        /// <summary>The pixels the application itself hands to the clipboard and the cache.</summary>
        static byte[] ViewPixels(ScrapBase scrap)
        {
            using (var rendered = (Bitmap)scrap.GetViewImage())
            {
                return Pixels(rendered);
            }
        }

        static byte[] ClientPixels(Form form)
        {
            var size = form.ClientSize;
            Assert.IsTrue(size.Width > 0 && size.Height > 0, form.GetType().Name + " has an empty client area.");

            using (var rendered = new Bitmap(size.Width, size.Height, PixelFormat.Format24bppRgb))
            {
                form.DrawToBitmap(rendered, new Rectangle(Point.Empty, size));

                return Pixels(rendered);
            }
        }

        /// <summary>
        /// The raw bytes, dimensions first so a size change reads as a difference rather than
        /// as an out-of-range comparison.
        /// </summary>
        static byte[] Pixels(Bitmap bitmap)
        {
            var data = bitmap.LockBits(
                new Rectangle(Point.Empty, bitmap.Size), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            try
            {
                var bytes = new byte[8 + Math.Abs(data.Stride) * data.Height];
                BitConverter.GetBytes(bitmap.Width).CopyTo(bytes, 0);
                BitConverter.GetBytes(bitmap.Height).CopyTo(bytes, 4);

                for (var row = 0; row < data.Height; row++)
                {
                    System.Runtime.InteropServices.Marshal.Copy(
                        data.Scan0 + row * data.Stride,
                        bytes,
                        8 + row * Math.Abs(data.Stride),
                        Math.Abs(data.Stride));
                }

                return bytes;
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        /// <summary>
        /// Compares two renders, refusing to pass on a pair of blank ones. A hidden form whose
        /// <c>WM_PRINTCLIENT</c> produced nothing would otherwise satisfy every assertion in
        /// this class.
        /// </summary>
        static void AssertPixelsEqual(byte[] expected, byte[] actual, string what)
        {
            AssertNotBlank(expected, what);

            if (expected.Length != actual.Length)
            {
                Assert.Fail(
                    what + " changed size: " + Describe(expected) + " became " + Describe(actual));
            }

            for (var i = 8; i < expected.Length; i++)
            {
                if (expected[i] != actual[i])
                {
                    Assert.Fail(
                        what + " changed at byte " + (i - 8) + " of " + Describe(expected)
                            + ": " + expected[i] + " became " + actual[i]);
                }
            }
        }

        static void AssertNotBlank(byte[] pixels, string what)
        {
            for (var i = 9; i < pixels.Length; i++)
            {
                if (pixels[i] != pixels[8])
                {
                    return;
                }
            }

            Assert.Fail(
                what + " is a single flat colour (" + Describe(pixels) + "), so comparing it to"
                    + " anything proves nothing — the render did not happen.");
        }

        static string Describe(byte[] pixels)
        {
            return BitConverter.ToInt32(pixels, 0) + "x" + BitConverter.ToInt32(pixels, 4);
        }
    }
}
