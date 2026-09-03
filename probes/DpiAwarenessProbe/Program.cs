using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DpiAwarenessProbe
{
    /// <summary>
    /// Answers "does the runtime actually report Per-Monitor V2?" for SETUNA's exact
    /// configuration.
    /// <para>
    /// The unit-test suite cannot answer it: DPI awareness is a property of the
    /// process, established from the linked manifest at creation time, and the test
    /// host has no application manifest. So this probe links
    /// <c>SETUNA\app.manifest</c> itself and makes the same three calls the
    /// source-generated <c>ApplicationConfiguration.Initialize()</c> makes, in the
    /// same order, printing the values that call sequence produces.
    /// </para>
    /// </summary>
    internal static class Program
    {
        // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2. Read-only OS state, queried
        // before any WinForms type is touched: GetAwarenessFromDpiAwarenessContext
        // cannot tell V1 from V2, so compare the context handles instead.
        static readonly IntPtr PerMonitorAwareV2 = new IntPtr(-4);

        [STAThread]
        static int Main()
        {
            var manifestGrantedPerMonitorV2 = AreDpiAwarenessContextsEqual(
                GetThreadDpiAwarenessContext(), PerMonitorAwareV2);

            // Deliberately no Application.HighDpiMode read before this point: SETUNA
            // does not make one either, and reading it early can complete WinForms'
            // scaling initialization and change what SetHighDpiMode does.
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var setHighDpiModeAccepted = Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            var reportedMode = Application.HighDpiMode;

            Console.WriteLine("Manifest granted PerMonitorV2 at process creation: " + manifestGrantedPerMonitorV2);
            Console.WriteLine("SetHighDpiMode(PerMonitorV2) accepted:             " + setHighDpiModeAccepted);
            Console.WriteLine("Application.HighDpiMode before the first form:     " + reportedMode);

            using (var form = new Form())
            {
                // Reading Handle forces creation, which is what binds the form to a
                // monitor and fills DeviceDpi. An unaware process is always told 96
                // here regardless of the monitor it lands on.
                _ = form.Handle;
                Console.WriteLine("First form DeviceDpi:                              " + form.DeviceDpi);
            }

            foreach (var screen in Screen.AllScreens)
            {
                Console.WriteLine("Screen " + screen.DeviceName + " bounds " + screen.Bounds
                    + (screen.Primary ? " (primary)" : string.Empty));
            }

            if (reportedMode != HighDpiMode.PerMonitorV2)
            {
                Console.WriteLine("FAIL: expected " + HighDpiMode.PerMonitorV2 + ", got " + reportedMode + ".");
                return 1;
            }

            Console.WriteLine("PASS: the process reports " + HighDpiMode.PerMonitorV2
                + " before the first form is created.");

            return 0;
        }

        [DllImport("user32.dll")]
        static extern IntPtr GetThreadDpiAwarenessContext();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool AreDpiAwarenessContextsEqual(IntPtr contextA, IntPtr contextB);
    }
}
