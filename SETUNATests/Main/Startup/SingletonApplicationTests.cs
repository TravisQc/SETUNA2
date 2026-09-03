using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using com.clearunit;

namespace SETUNATests.Main.Startup
{
    /// <summary>
    /// Election and forwarding behaviour of the named-pipe single instance. Every instance
    /// here is built with a unique channel suffix so the suite never elects against — or
    /// forwards arguments into — a real SETUNA sitting in the tray.
    /// </summary>
    [TestClass]
    public class SingletonApplicationTests
    {
        readonly List<IDisposable> owned = new List<IDisposable>();
        SynchronizationContext ambientContext;

        /// <summary>
        /// Forwarding is posted onto whatever <see cref="SynchronizationContext"/> was
        /// current when the listener registered. Constructing any WinForms control installs
        /// a <c>WindowsFormsSynchronizationContext</c> on the thread and leaves it there, so
        /// once another test class has built a form these tests would be posting into a
        /// message queue that no test pumps — delivery would depend on test order. Null it
        /// out so dispatch is inline and deterministic; the captured-context behaviour has
        /// its own test with a context this class controls.
        /// </summary>
        [TestInitialize]
        public void SuppressTheAmbientSynchronizationContext()
        {
            ambientContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);
        }

        [TestCleanup]
        public void DisposeInstances()
        {
            for (var i = owned.Count - 1; i >= 0; i--)
            {
                owned[i].Dispose();
            }

            owned.Clear();
            SynchronizationContext.SetSynchronizationContext(ambientContext);
        }

        SingletonApplication Create(string suffix, params string[] args)
        {
            var instance = new SingletonApplication("3.1.0-test", args, suffix);
            owned.Add(instance);
            return instance;
        }

        static string Suffix()
        {
            return "-test-" + Guid.NewGuid().ToString("N");
        }

        [TestMethod]
        public void TheFirstRegistrationWinsTheMutexAndTheSecondDoesNot()
        {
            var suffix = Suffix();

            Assert.IsTrue(Create(suffix).Register(), "The first instance must become the server.");
            Assert.IsFalse(
                Create(suffix, "second").Register(),
                "A second instance must not become a server, so it never creates a duplicate UI.");
        }

        [TestMethod]
        public void ADifferentChannelIsAnIndependentElection()
        {
            Assert.IsTrue(Create(Suffix()).Register());
            Assert.IsTrue(Create(Suffix()).Register());
        }

        [TestMethod]
        public async Task TheServerForwardsArgumentsInOrderToTheListener()
        {
            var suffix = Suffix();
            var server = Create(suffix);
            Assert.IsTrue(server.Register());

            var listener = new RecordingListener();
            server.AddSingletonFormListener(listener);

            var expected = new[] { "capture", "-x", "-120", "with space.png" };
            await SendAsync(server.PipeName, expected).ConfigureAwait(false);

            Assert.IsTrue(listener.WaitForCall(4000), "The listener was never called.");
            CollectionAssert.AreEqual(expected, listener.Args);
            Assert.AreEqual("3.1.0-test", listener.Version);
        }

        /// <summary>
        /// Register() starts the pipe before Mainform exists, so a launch that lands in that
        /// window must be held rather than dropped.
        /// </summary>
        [TestMethod]
        public async Task AMessageArrivingBeforeTheListenerIsDeliveredOnceItRegisters()
        {
            var suffix = Suffix();
            var server = Create(suffix);
            Assert.IsTrue(server.Register());

            await SendAsync(server.PipeName, new[] { "early" }).ConfigureAwait(false);

            // Give the server loop time to read and queue the message.
            await Task.Delay(250).ConfigureAwait(false);

            var listener = new RecordingListener();
            server.AddSingletonFormListener(listener);

            Assert.IsTrue(listener.WaitForCall(4000), "The queued message was dropped.");
            CollectionAssert.AreEqual(new[] { "early" }, listener.Args);
        }

        [TestMethod]
        public void AMalformedMessageDoesNotStopTheServer()
        {
            var suffix = Suffix();
            var server = Create(suffix);
            Assert.IsTrue(server.Register());

            var listener = new RecordingListener();
            server.AddSingletonFormListener(listener);

            using (var client = new NamedPipeClientStream(".", server.PipeName, PipeDirection.Out, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
            {
                client.Connect(2000);
                // A length prefix that promises more than the 64 KiB limit allows.
                client.Write(BitConverter.GetBytes(int.MaxValue), 0, sizeof(int));
                client.Flush();
            }

            Assert.IsFalse(listener.WaitForCall(500), "A malformed message must not reach the UI.");

            // The server survived and still accepts a well-formed message.
            SendAsync(server.PipeName, new[] { "after" }).GetAwaiter().GetResult();
            Assert.IsTrue(listener.WaitForCall(4000), "The server stopped accepting clients after a bad message.");
            CollectionAssert.AreEqual(new[] { "after" }, listener.Args);
        }

        [TestMethod]
        public void ForwardingToAnAbsentServerFailsWithinTheTwoSecondBudget()
        {
            var suffix = Suffix();
            var orphan = Create(suffix, "orphan");

            // Hold the mutex without starting a server, so the instance believes an owner
            // exists and takes the forwarding path with nothing listening on the pipe.
            using (new Mutex(true, orphan.MutexName))
            {
                var stopwatch = Stopwatch.StartNew();
                Assert.IsFalse(orphan.Register(), "Without the mutex it must not become a server.");
                stopwatch.Stop();

                Assert.IsTrue(
                    stopwatch.ElapsedMilliseconds < 6000,
                    "Forwarding blocked for " + stopwatch.ElapsedMilliseconds + "ms; the connect timeout is 2s.");
            }
        }

        [TestMethod]
        public void DisposingTheServerReleasesTheChannelForANewElection()
        {
            var suffix = Suffix();

            var first = new SingletonApplication("3.1.0-test", Array.Empty<string>(), suffix);
            Assert.IsTrue(first.Register());
            first.Dispose();

            Assert.IsTrue(
                Create(suffix).Register(),
                "After the owner exits, the next launch must be able to become the server.");
        }

        /// <summary>
        /// The production path: <c>Mainform</c> registers on the UI thread, so forwarding
        /// must hand the call to that thread's context rather than run it on the pipe's
        /// worker. Uses a context this test owns, because the ambient one is suppressed.
        /// </summary>
        [TestMethod]
        public async Task TheListenerIsInvokedThroughTheContextCapturedAtRegistration()
        {
            var suffix = Suffix();
            var server = Create(suffix);
            Assert.IsTrue(server.Register());

            var uiThread = Thread.CurrentThread.ManagedThreadId;
            var context = new RecordingSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(context);

            var listener = new RecordingListener();
            server.AddSingletonFormListener(listener);

            await SendAsync(server.PipeName, new[] { "posted" }).ConfigureAwait(false);

            Assert.IsTrue(listener.WaitForCall(4000), "The listener was never called.");
            Assert.AreEqual(1, context.Posts, "Forwarding bypassed the captured synchronization context.");
            Assert.AreNotEqual(
                uiThread,
                context.PostedFromThreadId,
                "The post came from the registering thread, so nothing was handed across threads.");
        }

        static async Task SendAsync(string pipeName, string[] args)
        {
            using (var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
            {
                await client.ConnectAsync(2000).ConfigureAwait(false);
                await SingletonProtocol.WriteAsync(client, "3.1.0-test", args, CancellationToken.None).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Stands in for the WinForms UI context: records that it was used and from where,
        /// then runs the callback immediately so the test needs no message pump.
        /// </summary>
        sealed class RecordingSynchronizationContext : SynchronizationContext
        {
            int posts;

            public int Posts => posts;

            public int PostedFromThreadId { get; private set; }

            public override void Post(SendOrPostCallback d, object state)
            {
                PostedFromThreadId = Thread.CurrentThread.ManagedThreadId;
                Interlocked.Increment(ref posts);
                d(state);
            }
        }

        sealed class RecordingListener : ISingletonForm
        {
            readonly ManualResetEventSlim received = new ManualResetEventSlim(false);

            public string Version { get; private set; }

            public string[] Args { get; private set; }

            public bool WaitForCall(int milliseconds)
            {
                return received.Wait(milliseconds);
            }

            public void DetectExternalStartup(string version, string[] args)
            {
                Version = version;
                Args = args;
                received.Set();
            }
        }
    }
}
