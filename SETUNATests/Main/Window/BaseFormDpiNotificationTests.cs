using System;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SETUNA.Main.Tests;
using SETUNA.Main.Window;

namespace SETUNATests.Main.Window
{
    /// <summary>
    /// The contract of <c>BaseForm</c>'s DPI hook: it fires once per transition, never for
    /// the DPI a form was born on, and never re-enters itself.
    /// <para>
    /// Driven by a synthetic <c>WM_DPICHANGED</c>, which is the same message the OS sends,
    /// so nothing here needs a second monitor or a per-monitor test host. What this host
    /// cannot reproduce is a monitor whose scale factor changes while a form is hidden —
    /// that entry point shares this same sync path, but observing it needs the physical
    /// dual-monitor matrix (task 10.2).
    /// </para>
    /// </summary>
    [TestClass]
    public class BaseFormDpiNotificationTests
    {
        sealed class CountingForm : BaseForm
        {
            public int Corrections { get; private set; }

            public int LastReportedDpi { get; private set; }

            public int LastPreviousDpi { get; private set; }

            public Action OnCorrection { get; set; }

            public CountingForm()
            {
                FormBorderStyle = FormBorderStyle.FixedDialog;
                ClientSize = new Size(300, 160);
            }

            protected override void OnDpiContextChanged(int previousDpi)
            {
                base.OnDpiContextChanged(previousDpi);

                Corrections++;
                LastReportedDpi = CurrentDpiContext.DpiX;
                LastPreviousDpi = previousDpi;
                OnCorrection?.Invoke();
            }
        }

        [TestMethod]
        public void AFormIsNeverToldAboutTheDpiItWasBornOn()
        {
            using (var form = new CountingForm())
            {
                LayoutSnapshot.ShowOffScreen(form);

                Assert.AreEqual(
                    0,
                    form.Corrections,
                    "The framework already laid the form out at this DPI; a correction here would run "
                        + "against caches the form has not built yet.");
                Assert.IsTrue(
                    DpiContext.IsUsableDpi(form.CurrentDpiContext.DpiX),
                    "The context must still be established, just not announced.");
            }
        }

        [TestMethod]
        public void ShowingAndHidingAtAConstantDpiRaisesNothing()
        {
            using (var form = new CountingForm())
            {
                LayoutSnapshot.ShowOffScreen(form);
                form.Show();
                Application.DoEvents();
                form.Hide();
                form.Show();
                Application.DoEvents();
                form.Hide();

                Assert.AreEqual(0, form.Corrections, "Re-showing on the same monitor is not a transition.");
            }
        }

        [TestMethod]
        public void ATransitionRaisesExactlyOneCorrectionCarryingTheNewDpi()
        {
            using (var form = new CountingForm())
            {
                LayoutSnapshot.ShowOffScreen(form);
                var born = form.CurrentDpiContext.DpiX;

                SendDpiChanged(form, born * 2);

                Assert.AreEqual(1, form.Corrections);
                Assert.AreEqual(born * 2, form.LastReportedDpi);
                Assert.AreEqual(born, form.LastPreviousDpi, "The hook must be told which DPI to scale from.");
                Assert.AreEqual(born * 2, form.CurrentMonitor.DpiX, "The snapshot must carry the new DPI too.");
            }
        }

        [TestMethod]
        public void RepeatingTheSameDpiRaisesNothingFurther()
        {
            using (var form = new CountingForm())
            {
                LayoutSnapshot.ShowOffScreen(form);
                var raised = form.CurrentDpiContext.DpiX * 2;

                SendDpiChanged(form, raised);
                SendDpiChanged(form, raised);

                Assert.AreEqual(1, form.Corrections, "Only the change is a transition, not every message.");
            }
        }

        /// <summary>
        /// The scenario a hidden tray window lands in: the monitor's scale factor changed
        /// while it was hidden, so no message ever reached it and its idea of the DPI is
        /// stale. Reproduced by driving the form to a DPI the OS does not agree with — the
        /// synthetic message desynchronises exactly the state a real hidden transition does.
        /// </summary>
        [TestMethod]
        public void ShowingAFormWhoseDpiMovedWhileHiddenCorrectsItOnce()
        {
            using (var form = new CountingForm())
            {
                LayoutSnapshot.ShowOffScreen(form);
                var real = form.CurrentDpiContext.DpiX;

                SendDpiChanged(form, real * 2);
                Assert.AreEqual(1, form.Corrections, "Precondition: the form now believes a stale DPI.");

                form.Show();
                Application.DoEvents();

                Assert.AreEqual(2, form.Corrections, "Becoming visible must reconcile the stale DPI.");
                Assert.AreEqual(real, form.LastReportedDpi, "The reconciliation must use the DPI the window really has.");

                form.Hide();
                form.Show();
                Application.DoEvents();

                Assert.AreEqual(2, form.Corrections, "Once reconciled, showing again is not a transition.");
            }
        }

        [TestMethod]
        public void ReturningToTheOriginalDpiIsItsOwnTransition()
        {
            using (var form = new CountingForm())
            {
                LayoutSnapshot.ShowOffScreen(form);
                var born = form.CurrentDpiContext.DpiX;

                SendDpiChanged(form, born * 2);
                SendDpiChanged(form, born);

                Assert.AreEqual(2, form.Corrections);
                Assert.AreEqual(born, form.LastReportedDpi);
            }
        }

        [TestMethod]
        public void TheHookIsNotReEnteredByWorkItDoesItself()
        {
            using (var form = new CountingForm())
            {
                LayoutSnapshot.ShowOffScreen(form);
                var born = form.CurrentDpiContext.DpiX;

                // A hook that resizes controls can push the window onto another monitor and
                // bring a second message straight back. Re-entering would let a correction
                // run against half-updated state.
                form.OnCorrection = () => SendDpiChanged(form, born * 3);

                SendDpiChanged(form, born * 2);

                Assert.AreEqual(1, form.Corrections, "The hook re-entered itself.");
            }
        }

        [TestMethod]
        public void ADisposedFormIsNotCorrected()
        {
            var form = new CountingForm();
            LayoutSnapshot.ShowOffScreen(form);
            var handle = form.Handle;
            var born = form.CurrentDpiContext.DpiX;
            form.Dispose();

            SyntheticDpiChange.Send(handle, born * 2, form.Bounds);

            Assert.AreEqual(0, form.Corrections);
        }

        /// <summary>
        /// A physical surface keeps its pixel bounds across a DPI change; a logical dialog does
        /// not. Both are driven here so the assertion cannot pass on a message that did nothing.
        /// <para>
        /// <c>AutoScaleMode.None</c> is not enough on its own: <c>WM_DPICHANGED</c> carries a
        /// suggested rectangle and <c>DefWindowProc</c> applies it, so a scrap window would be
        /// resized by the scale factor while its bitmap stayed put. <c>BaseForm.WndProc</c>
        /// writes the bounds back for a physical surface. Measured on the real desktop by
        /// <c>probes/SurfaceGeometryProbe</c>: 137x89 became 78x51 before the guard.
        /// </para>
        /// </summary>
        [TestMethod]
        public void APhysicalSurfaceKeepsItsPixelBoundsWhileALogicalDialogDoesNot()
        {
            using (var surface = new PhysicalSurfaceForm())
            using (var dialog = new CountingForm())
            {
                LayoutSnapshot.ShowOffScreen(surface);
                LayoutSnapshot.ShowOffScreen(dialog);

                var surfaceBefore = surface.Bounds;
                var dialogBefore = dialog.Bounds;

                SendDoubledDpi(surface);
                SendDoubledDpi(dialog);

                Assert.AreEqual(
                    surfaceBefore,
                    surface.Bounds,
                    "A physical surface must come out of a DPI change at exactly the pixels it went "
                        + "in with, or the bitmap it shows no longer matches the window.");

                Assert.AreNotEqual(
                    dialogBefore,
                    dialog.Bounds,
                    "The logical control group did not move, so this host is ignoring the suggested "
                        + "rectangle and the assertion above proves nothing.");
            }
        }

        /// <summary>
        /// The instruction Windows gives when a window crosses onto a monitor at twice the
        /// scale: double the DPI, and suggest a rectangle twice as large in the same place.
        /// </summary>
        static void SendDoubledDpi(Form form)
        {
            var current = form.Bounds;

            SyntheticDpiChange.Send(
                form.Handle,
                form.DeviceDpi * 2,
                new Rectangle(current.Left, current.Top, current.Width * 2, current.Height * 2));
        }

        /// <summary>
        /// A window that owns its pixels: no autoscaling, and the physical-surface policy. The
        /// registry holds application types only, so a test form declares its policy locally
        /// and <c>BaseForm.Policy</c> falls through to it.
        /// </summary>
        sealed class PhysicalSurfaceForm : BaseForm
        {
            public PhysicalSurfaceForm()
            {
                FormBorderStyle = FormBorderStyle.None;
                ClientSize = new Size(137, 89);
            }

            protected override DpiPolicy DpiPolicy => DpiPolicy.PhysicalSurface;
        }

        static void SendDpiChanged(Form form, int newDpi)
        {
            // The current bounds, not a scaled rectangle: these tests are about how often the
            // hook fires and with which DPI, so leaving the geometry alone keeps a relayout out
            // of the picture. The scaled form of the message is what
            // <see cref="SendDoubledDpi"/> and the pixel tests use.
            SyntheticDpiChange.Send(form.Handle, newDpi, form.Bounds);
        }
    }
}
