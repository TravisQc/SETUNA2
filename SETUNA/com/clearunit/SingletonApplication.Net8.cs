using System;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace com.clearunit
{
    /// <summary>
    /// Current-user single-instance election and startup forwarding for the SDK/.NET 8 build.
    /// The wire format is deliberately small and explicit so no object references or binary
    /// serializers cross the process boundary.
    /// </summary>
    public sealed class SingletonApplication : IDisposable
    {
        internal const int ClientTimeoutMilliseconds = 2000;

        static readonly object Sync = new object();
        static SingletonApplication instance;

        readonly string version;
        readonly string[] args;
        readonly string mutexName;
        readonly string pipeName;
        Mutex mutex;
        CancellationTokenSource serverCancellation;
        Task serverTask;
        ISingletonForm listener;
        SynchronizationContext listenerContext;
        readonly object listenerSync = new object();
        readonly Queue<SingletonProtocol.StartupMessage> pendingMessages = new Queue<SingletonProtocol.StartupMessage>();
        bool disposed;

        SingletonApplication(string version, string[] args)
            : this(version, args, string.Empty)
        {
        }

        /// <summary>
        /// Test seam. <paramref name="channelSuffix"/> moves the mutex and pipe off the names a
        /// real SETUNA owns, so running the suite on a machine with SETUNA in the tray cannot
        /// elect against it or forward arguments into it.
        /// </summary>
        internal SingletonApplication(string version, string[] args, string channelSuffix)
        {
            this.version = version ?? string.Empty;
            this.args = args == null ? Array.Empty<string>() : (string[])args.Clone();

            var sid = WindowsIdentity.GetCurrent().User?.Value ?? "unknown";
            var safeSid = sid.Replace('-', '_');
            mutexName = "Local\\SETUNA2-" + safeSid + (channelSuffix ?? string.Empty);
            pipeName = "SETUNA2-" + safeSid + (channelSuffix ?? string.Empty);
        }

        /// <summary>Test seam: the pipe a second instance connects to.</summary>
        internal string PipeName => pipeName;

        /// <summary>Test seam: the mutex the election competes for.</summary>
        internal string MutexName => mutexName;

        public static SingletonApplication GetInstance(string version, string[] args)
        {
            lock (Sync)
            {
                if (instance == null || instance.disposed)
                {
                    instance = new SingletonApplication(version, args);
                }

                return instance;
            }
        }

        public void AddSingletonFormListener(ISingletonForm implement)
        {
            if (implement == null)
            {
                throw new ArgumentNullException(nameof(implement));
            }

            SingletonProtocol.StartupMessage[] pending;
            lock (listenerSync)
            {
                listener = implement;
                listenerContext = SynchronizationContext.Current;
                pending = pendingMessages.ToArray();
                pendingMessages.Clear();
            }

            foreach (var message in pending)
            {
                DispatchMessage(message);
            }
        }

        public bool Register()
        {
            ThrowIfDisposed();

            bool createdNew;
            try
            {
                mutex = new Mutex(true, mutexName, out createdNew);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Singleton mutex creation failed: " + ex.Message);
                return false;
            }

            if (createdNew)
            {
                serverCancellation = new CancellationTokenSource();
                serverTask = RunServerAsync(serverCancellation.Token);
                Application.ApplicationExit += ApplicationExit;
                return true;
            }

            SendToPrimaryInstance();
            mutex.Dispose();
            mutex = null;
            return false;
        }

        async Task RunServerAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    using (var server = new NamedPipeServerStream(
                        pipeName,
                        PipeDirection.In,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
                    {
                        await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                        await ReceiveFromSecondaryInstanceAsync(server, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (IOException ex)
                {
                    Console.WriteLine("Singleton pipe server stopped a client: " + ex.Message);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Singleton pipe server error: " + ex.Message);
                    await Task.Yield();
                }
            }
        }

        async Task ReceiveFromSecondaryInstanceAsync(Stream stream, CancellationToken cancellationToken)
        {
            var message = await SingletonProtocol.ReadAsync(stream, cancellationToken).ConfigureAwait(false);

            lock (listenerSync)
            {
                if (listener == null)
                {
                    // Register() starts the pipe before the first form exists. Keep
                    // an early second launch instead of silently dropping its args.
                    pendingMessages.Enqueue(message);
                    return;
                }
            }

            DispatchMessage(message);
        }

        void DispatchMessage(SingletonProtocol.StartupMessage message)
        {
            ISingletonForm currentListener;
            SynchronizationContext context;
            lock (listenerSync)
            {
                currentListener = listener;
                context = listenerContext;
            }

            if (currentListener == null)
            {
                return;
            }

            void Dispatch()
            {
                try
                {
                    currentListener.DetectExternalStartup(message.ProductVersion, message.Args);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Singleton startup dispatch failed: " + ex.Message);
                }
            }

            if (context == null)
            {
                Dispatch();
            }
            else
            {
                context.Post(_ => Dispatch(), null);
            }
        }

        void SendToPrimaryInstance()
        {
            try
            {
                using (var client = new NamedPipeClientStream(
                    ".",
                    pipeName,
                    PipeDirection.Out,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
                {
                    client.Connect(ClientTimeoutMilliseconds);
                    SingletonProtocol.WriteAsync(client, version, args, CancellationToken.None)
                        .GetAwaiter()
                        .GetResult();
                }
            }
            catch (InvalidDataException ex)
            {
                Console.WriteLine("Singleton startup arguments exceed the 64 KiB limit: " + ex.Message);
            }
            catch (TimeoutException)
            {
                Console.WriteLine("Singleton primary instance did not accept the connection within 2 seconds.");
            }
            catch (IOException ex)
            {
                Console.WriteLine("Singleton startup forwarding failed: " + ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.WriteLine("Singleton startup forwarding was rejected: " + ex.Message);
            }
        }

        void ApplicationExit(object sender, EventArgs e)
        {
            Dispose();
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            Application.ApplicationExit -= ApplicationExit;

            if (serverCancellation != null)
            {
                var source = serverCancellation;
                source.Cancel();
                var task = serverTask;
                serverTask = null;
                serverCancellation = null;

                if (task != null && !task.IsCompleted)
                {
                    try
                    {
                        task.Wait(1000);
                    }
                    catch (AggregateException)
                    {
                        // RunServerAsync observes protocol/client errors itself;
                        // cancellation during process exit is expected.
                    }
                }

                source.Dispose();
            }

            if (mutex != null)
            {
                try
                {
                    mutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                }

                mutex.Dispose();
                mutex = null;
            }
        }

        void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(SingletonApplication));
            }
        }

    }
}
