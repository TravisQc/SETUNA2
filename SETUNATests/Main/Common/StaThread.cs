using System;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace SETUNA.Main.Tests
{
    /// <summary>
    /// Runs a test body on a single-threaded-apartment thread.
    /// <para>
    /// The test host's threads are MTA, and some WinForms controls need OLE: realizing a
    /// window whose <c>AllowDrop</c> is true calls <c>Control.SetAcceptDrops</c>, which
    /// throws <c>ThreadStateException</c> ("OLE 必须先被调用") outside an STA. Worse than
    /// the failure is how it surfaces — WinForms catches it on the message pump and shows
    /// a modal <c>ThreadExceptionDialog</c>, so the suite passes while a dialog waits for
    /// a click. <c>ScrapBase</c> is the one window in the application that sets
    /// <c>AllowDrop</c>.
    /// </para>
    /// <para>
    /// A dedicated thread rather than a suite-wide apartment setting: the apartment is a
    /// property of the thread a test runs on, and changing it for all 290-odd tests to
    /// serve the handful that need it would re-qualify every one of them.
    /// </para>
    /// </summary>
    static class StaThread
    {
        public static void Run(Action body)
        {
            ExceptionDispatchInfo failure = null;

            var thread = new Thread(() =>
            {
                try
                {
                    body();
                }
                catch (Exception ex)
                {
                    failure = ExceptionDispatchInfo.Capture(ex);
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            thread.Join();

            // Rethrown with its original stack, so an assertion failure inside the body
            // still reads as that assertion and not as a threading problem.
            failure?.Throw();
        }
    }
}
