//-----------------------------------------------------------------------
// <copyright company="MicroTrends Ltd, https://github.com/MicroTrendsLtd">
//     Author: Tom Leeson
//     Copyright (c) 2025 MicroTrends Ltd. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace EasyPipes
{
    /// <summary>
    /// The Server class is designed for robustness in inter-process communication (IPC) scenarios using named pipes. It efficiently manages connections, retries, and message transmission while handling errors gracefully. With thread-safety mechanisms and state tracking, the server is capable of handling high-reliability communications, making it well-suited for use in both single-client and multi-client scenarios.
    /// </summary>
    public partial class Server : IPipeLayer
    {
        #region vars & props
        private readonly object objectLockPipeConnect = new object();
        private readonly SemaphoreSlim connectionGate = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim lifecycleGate = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim sendGate = new SemaphoreSlim(1, 1);

        public NamedPipeServerStream PipeServer { get; set; }
        public int ThreadId { get; private set; }
        public string PipeName { get; set; } = "PipeATS";
        public Server(string pipeName)
        {
            PipeName = pipeName;
        }
        public string Message { get; set; } = string.Empty;
        public bool IsErrors { get; private set; }

        public int TimeOut { get; set; } = 3600;
        public bool IsStarted { get; set; } = false;
        public PipeTransmissionMode TransmissionMode { get; set; } = PipeTransmissionMode.Message;
        public PipeDirection PipeIODirection { get; set; } = PipeDirection.Out;
        public PipeOptions PipeIOOptions { get; set; } = PipeOptions.WriteThrough;

        #endregion

        /// <summary>
        /// Starts the pipe server asynchronously and waits for a connection.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task StartAsync()
        {
            Task<bool> connection;
            await lifecycleGate.WaitAsync().ConfigureAwait(false);
            try
            {
                IsStarted = true;
                connection = TryCreateAndWaitForConnectionAsync();
            }
            finally { lifecycleGate.Release(); }
            await connection.ConfigureAwait(false);
        }
        /// <summary>
        /// Checks if the pipe server is ready and connected.
        /// </summary>
        /// <returns>True if the pipe server is connected and no errors occurred, otherwise false.</returns>
        public bool IsStateReady()
        {
            var stream = PipeServer;
            try { return stream != null && stream.IsConnected && !IsErrors; }
            catch (ObjectDisposedException) { return false; }
        }

        /// <summary>
        /// Attempts to create and wait for a connection to the pipe server asynchronously.
        /// </summary>
        /// <returns>A task representing the result of the connection attempt. True if successful, otherwise false.</returns>
        public async Task<bool> TryCreateAndWaitForConnectionAsync()
        {
            return await TryCreateAndWaitForConnectionAsync(PipeName, TimeOut, PipeIODirection, TransmissionMode, PipeIOOptions);
        }

        /// <summary>
        /// Attempts to create and wait for a connection to the pipe server asynchronously with configurable options.
        /// </summary>
        /// <param name="pipeName">The name of the pipe to connect to.</param>
        /// <param name="timeOutSeconds">The amount of time (in seconds) to attempt connection before timing out.</param>
        /// <param name="pipeDirection">The direction of data flow in the pipe (e.g., In, Out, or InOut).</param>
        /// <param name="mode">The transmission mode for the pipe (e.g., Byte or Message).</param>
        /// <param name="pipeOptions">Options for the pipe (e.g., Asynchronous or WriteThrough).</param>
        /// <param name="outBufferSize">The size of the output buffer.</param>
        /// <param name="inBufferSize">The size of the input buffer.</param>
        /// <param name="maxInstances">The maximum number of instances of the pipe that can be created.</param>
        /// <returns>A task representing the result of the connection attempt. True if successful, otherwise false.</returns>
        public async Task<bool> TryCreateAndWaitForConnectionAsync(
         string pipeName,
         int timeOutSeconds = 14400,
         PipeDirection pipeDirection = PipeDirection.InOut,
         PipeTransmissionMode mode = PipeTransmissionMode.Message,
         PipeOptions pipeOptions = PipeOptions.WriteThrough | PipeOptions.Asynchronous,
         int outBufferSize = 512,
         int inBufferSize = 512,
         int maxInstances = 10)
        {
            if (!IsStarted)
            {
                return false;
            }

            await connectionGate.WaitAsync().ConfigureAwait(false);
#if DEBUG
            Console.WriteLine($"TryCreateAndWaitForConnectionAsync {pipeName}.");
#endif


            try
            {
                // Check if the pipe server is ready and return early if it is
                if (PipeServer != null && IsStateReady())
                {
                    Console.WriteLine($"TryCreateAndWaitForConnectionAsync > {pipeName} > Connected! Skipping reconnection.");
                    return true; // Already connected
                }

                // Dispose of any existing pipe server instance before creating a new one
                await PipeDisposeAsync().ConfigureAwait(false);

                Console.WriteLine($"TryCreateAndWaitForConnectionAsync > {pipeName} > Starting New PipeServer");

                // Create the NamedPipeServerStream with provided parameters
                NamedPipeServerStream stream;
                lock (objectLockPipeConnect)
                {
                if (!IsStarted) return false;
                stream = new NamedPipeServerStream(
                    pipeName,
                    pipeDirection,
                    maxInstances,
                    mode,
                    pipeOptions | PipeOptions.Asynchronous,
                    outBufferSize,
                    inBufferSize
                );
                PipeServer = stream;
                }

                ThreadId = Thread.CurrentThread.ManagedThreadId;


                // Use CancellationTokenSource to cancel connection attempt on timeout
                using var connectCts = new CancellationTokenSource();
                using var abort = connectCts.Token.Register(() => stream.Dispose());
                var connectionTask = stream.WaitForConnectionAsync(connectCts.Token);
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeOutSeconds));

                var completed = await Task.WhenAny(connectionTask, timeoutTask).ConfigureAwait(false);
                if (completed == connectionTask)
                {
                    // Ensure exceptions are observed
                    await connectionTask.ConfigureAwait(false);

                    StartReader(stream);
                    Console.WriteLine($"{pipeName} connection succeeded.");
                    return true;
                }
                else
                {
                    connectCts.Cancel();
                    try { await connectionTask.ConfigureAwait(false); } catch { }
                    throw new TimeoutException($"Connection to pipe '{pipeName}' timed out after {timeOutSeconds} seconds.");
                }


            }
            catch (Exception ex)
            {
                Console.WriteLine($"TryCreateAndWaitForConnectionAsync > {pipeName} > ERROR:\n{ex}");
                IsErrors = true;
                await PipeDisposeAsync().ConfigureAwait(false);
                return false;
            }
            finally
            {
                connectionGate.Release();
            }
        }

        private Task PipeDisposeAsync()
        {
            // Never drain during shutdown: a connected but stalled reader can
            // prevent a drain from finishing indefinitely.
            NamedPipeServerStream stream;
            lock (objectLockPipeConnect) { stream = PipeServer; PipeServer = null; }
            stream?.Dispose();
            IsErrors = false;
            return Task.CompletedTask;
        }
        /// <summary>
        /// Attempts to connect to the pipe server and send a message.
        /// If not connected, it tries to reconnect before sending the message.
        /// </summary>
        /// <param name="message">The message to send to the pipe server.</param>
        /// <returns>A task representing the result of the send operation. True if successful, otherwise false.</returns>
        public async Task<bool> TryConnectSendMessageAsync(string message)
        {
            if (!IsStarted)
            {
                return false;
            }

            //cache so the last message is always sent
            Message = message;
#if DEBUG
            //Console.WriteLine($"TryConnectSendMessage");
#endif

            if (!IsStateReady())
            {
                if (!await TryCreateAndWaitForConnectionAsync())
                    return false;
            }
            return await TrySendMessageAsync(message).ConfigureAwait(false);
        }

        /// <summary>
        /// Sends a message through the pipe server if a connection is established.
        /// </summary>
        /// <param name="message">The message to send.</param>
        /// <returns>A task representing the result of the send operation. True if successful, otherwise false.</returns>
        public async Task<bool> TrySendMessageAsync(string message, int timeoutMillis = 5000)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            if (timeoutMillis <= 0) throw new ArgumentOutOfRangeException(nameof(timeoutMillis));
            if (!IsStarted || !await sendGate.WaitAsync(timeoutMillis).ConfigureAwait(false)) return false;
            NamedPipeServerStream stream = null;
            try
            {
                if (!IsStarted || !IsStateReady()) return false;
                stream = PipeServer;
                if (stream == null || !stream.CanWrite) return false;
                await PipeOperation.RunAsync(stream,
                    () => StreamIO.WriteStringAsync(stream, message), timeoutMillis).ConfigureAwait(false);
                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine($"{PipeName} > Send > ERROR: {e.Message}");
                FailConnection(stream);
                return false;
            }
            finally { sendGate.Release(); }
        }
        /// <summary>
        /// Stops the pipe server asynchronously, disconnecting and disposing of the resources.
        /// </summary>
        /// <returns>A task representing the asynchronous stop operation.</returns>
        public async Task StopAsync()
        {
            await lifecycleGate.WaitAsync().ConfigureAwait(false);
            try
            {
                IsStarted = false;
                await PipeDisposeAsync().ConfigureAwait(false);
                await connectionGate.WaitAsync().ConfigureAwait(false);
                connectionGate.Release();
                await sendGate.WaitAsync().ConfigureAwait(false);
                sendGate.Release();
                IsErrors = false;
            }
            finally { lifecycleGate.Release(); }
        }
    }
}
