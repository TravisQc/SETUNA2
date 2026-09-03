using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SETUNATests.Main.Window
{
    /// <summary>
    /// Sends the message Windows sends when a window's DPI changes.
    /// <para>
    /// Shared by every DPI test in this host because a synthetic <c>WM_DPICHANGED</c> is the
    /// only way to reach that code path here: a second monitor cannot be attached, and the
    /// suggested rectangle is honoured by <c>DefWindowProc</c> even in a DPI-unaware process,
    /// which is exactly what the physical-surface guard has to defeat.
    /// </para>
    /// <para>
    /// What this host still cannot reproduce is the framework <em>relayout</em> of a logical
    /// control tree — WinForms only scales a control tree on this message when the process is
    /// per-monitor-v2 aware, and the test host carries no manifest. That half lives in
    /// <c>probes/DialogRelayoutProbe</c>.
    /// </para>
    /// </summary>
    static class SyntheticDpiChange
    {
        const int WM_DPICHANGED = 0x02E0;

        /// <summary>
        /// Drives <paramref name="form"/> to <paramref name="newDpi"/>, suggesting the
        /// rectangle Windows would suggest: the window's current position with its bounds
        /// scaled by the DPI ratio. A negative origin scales too — that is how a monitor
        /// left of or above the primary reports it, and a physical surface must survive it.
        /// </summary>
        public static void Send(Form form, int newDpi)
        {
            var ratio = (double)newDpi / Math.Max(1, CurrentDpiOf(form));
            var current = form.Bounds;

            Send(
                form.Handle,
                newDpi,
                new Rectangle(
                    SETUNA.Main.Window.DpiContext.Scale(current.Left, ratio),
                    SETUNA.Main.Window.DpiContext.Scale(current.Top, ratio),
                    SETUNA.Main.Window.DpiContext.Scale(current.Width, ratio),
                    SETUNA.Main.Window.DpiContext.Scale(current.Height, ratio)));
        }

        /// <summary>
        /// The DPI the form currently believes it is on, which is what the suggested rectangle
        /// is scaled from.
        /// <para>
        /// <c>Control.DeviceDpi</c> cannot answer this after a synthetic transition: outside a
        /// per-monitor-v2 process it reports the process-wide startup DPI and never moves, so
        /// scaling from it would make a return trip suggest the rectangle it already has.
        /// <c>BaseForm</c> records the DPI it last synced to, which does follow.
        /// </para>
        /// </summary>
        static int CurrentDpiOf(Form form)
        {
            var tracked = form as BaseForm;
            if (tracked != null && SETUNA.Main.Window.DpiContext.IsUsableDpi(tracked.CurrentDpiContext.DpiX))
            {
                return tracked.CurrentDpiContext.DpiX;
            }

            return form.DeviceDpi;
        }

        /// <summary>
        /// Both words of wParam carry the new DPI; lParam points at the rectangle Windows
        /// suggests for the new scale.
        /// </summary>
        public static void Send(IntPtr handle, int newDpi, Rectangle suggested)
        {
            var buffer = Marshal.AllocHGlobal(16);
            try
            {
                Marshal.WriteInt32(buffer, 0, suggested.Left);
                Marshal.WriteInt32(buffer, 4, suggested.Top);
                Marshal.WriteInt32(buffer, 8, suggested.Right);
                Marshal.WriteInt32(buffer, 12, suggested.Bottom);

                SETUNA.Main.WindowsAPI.SendMessage(
                    handle,
                    WM_DPICHANGED,
                    new IntPtr((newDpi << 16) | newDpi),
                    buffer);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }

            Application.DoEvents();
        }
    }
}
