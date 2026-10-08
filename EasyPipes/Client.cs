//-----------------------------------------------------------------------
// <copyright company="MicroTrends Ltd, https://github.com/MicroTrendsLtd">
//     Author: Tom Leeson
//     Copyright (c) 2025 MicroTrends Ltd. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

namespace EasyPipes
{
    /// <summary>
    /// The Client class implements a pipe client using NamedPipeClientStream for inter-process communication. It provides methods to connect asynchronously to a pipe server, read messages, and manage the connection state. The class handles message transmission, error reporting, and resource disposal. Events are raised for message reception (MessageReceived) and state updates (StateMessage). It supports asynchronous operations like connection retries and message reading while ensuring thread safety with locking mechanisms. Additionally, the class includes customizable pipe options and implements the IDisposable interface for clean resource management.
    /// </summary>
    public partial class Client : IPipeLayer, IDisposable
    {
        private readonly object objectLockPipeConnect = new object();
        private readonly object objectLockReader = new object();
        private bool isWaitingConnection;
        private bool isReading;
        private readonly SemaphoreSlim sendGate = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim lifecycleGate = new SemaphoreSlim(1, 1);
        private Task readerTask = Task.CompletedTask;
        private CancellationTokenSource lifetime = new CancellationTokenSource();
        public int MessageTimeoutMillis { get; set; } = 30000;
        public NamedPipeClientStream PipeClient { get; set; }
        public string PipeName { get; set; } = "EasyPipe1";
        public event EventHandler<MessageEventArgs> MessageReceived;
        public event EventHandler<StateMessageEventArgs> StateMessage;
        public bool IsErrors { get; private set; }
        public int TimeOut { get; set; } = 120;
        public bool IsStarted { get; set; } = false;
        public PipeTransmissionMode TransmissionMode { get; set; } = PipeTransmissionMode.Byte;//cant be changed
        public PipeDirection PipeIODirection { get; set; } = PipeDirection.In;
        public PipeOptions PipeIOOptions { get; set; } = PipeOptions.WriteThrough;


        /// <summary>
        ///  Constructor that initializes the client with a given pipe name.
        /// </summary>
        /// <param name="pipeName"></param>
        public Client(string pipeName)
        {
            PipeName = pipeName;
            Console.WriteLine(PipeName);
        }

        /// <summary>
        /// Starts the pipe client asynchronously and initiates the connection and reading process.
        /// </summary>
        /// <returns></returns>
        public async Task StartAsync()
        {
            await lifecycleGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (IsStarted) return;
                await readerTask.ConfigureAwait(false);
                lifetime.Dispose();
                lifetime = new CancellationTokenSource();
                IsStarted = true;
                readerTask = Task.Run(DoConnectAndReadInternalAsync);
            }
            finally { lifecycleGate.Release(); }
        }
        /// <summary>
        /// Stops the pipe client, disposes of resources, and logs the stop state.
        /// </summary>
        /// <returns></returns>
        public async Task StopAsync()
        {
            await lifecycleGate.WaitAsync().ConfigureAwait(false);
            try
            {
                IsStarted = false;
                lifetime.Cancel();
                PipeDispose();
                await readerTask.ConfigureAwait(false);
                IsErrors = false;
            }
            finally { lifecycleGate.Release(); }
        }
        /// <summary>
        /// Safely disposes the pipe client, handling disconnection and error states
        /// </summary>
        private void PipeDispose()
        {
            NamedPipeClientStream stream;
            lock (objectLockPipeConnect)
            {
                stream = PipeClient;
                PipeClient = null;
                IsErrors = false;
            }
            stream?.Dispose();
        }
        /// <summary>
        /// Continuously attempts to connect and read from the pipe while the client is started.
        /// </summary>
        /// <returns></returns>
        private async Task DoConnectAndReadInternalAsync()
        {
            Console.WriteLine("DoConnectAndReadInternalAsync");
            while (IsStarted)
            {
                while (IsStarted && !IsStateReady())
                {
                    if (await TryCreateAndWaitForConnectionAsync().ConfigureAwait(false)) break;
                    await Task.Delay(250).ConfigureAwait(false);
                }

                while (IsStarted && IsStateReady())
                {
                    var stream = PipeClient;
                    if (stream != null && stream.CanRead)
                        await TryReadMessagesAsync().ConfigureAwait(false);
                    else
                        await Task.Delay(100).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        ///  Attempts to create a connection to the pipe server with default parameters.
        /// </summary>
        /// <returns></returns>
        public async Task<bool> TryCreateAndWaitForConnectionAsync()
        {
            return await TryCreateAndWaitForConnectionAsync(PipeName, TimeOut, ".", PipeIODirection, PipeIOOptions);
        }

        /// <summary>
        /// Attempts to connect to the pipe server asynchronously with configurable options like direction, timeout, and server name.
        /// </summary>
        /// <param name="pipeName"></param>
        /// <param name="timeout"></param>
        /// <param name="serverName"></param>
        /// <param name="pipeDirection"></param>
        /// <param name="pipeOptions"></param>
        /// <param name="tokenImpersonationLevel"></param>
        /// <returns></returns>
        public async Task<bool> TryCreateAndWaitForConnectionAsync(
        string pipeName,
        int timeout,
        string serverName = ".", // Default server name, "." refers to local machine
        PipeDirection pipeDirection = PipeDirection.In, // Default direction, can be set to Out or InOut
        PipeOptions pipeOptions = PipeOptions.Asynchronous, // Default options, can be set to Asynchronous or WriteThrough
        TokenImpersonationLevel tokenImpersonationLevel = TokenImpersonationLevel.Impersonation)
        {
            if (isWaitingConnection)
                return false;

            lock (objectLockPipeConnect)
            {
                if (isWaitingConnection)
                    return false;
                isWaitingConnection = true;
            }

#if DEBUG
            Console.WriteLine($"TryCreateAndWaitForConnectionAsync started.");
#endif

            try
            {
                // Check if the pipe server is ready and return early if it is
                if (PipeClient != null && IsStateReady())
                {
                    Console.WriteLine($"EasyPipes.Client > {pipeName} > Client already ready, skipping reconnection.");
                    isWaitingConnection = false;
                    return true; // No need to reconnect
                }

                //reset the pipe in case of Pipe IO Errors
                PipeDispose();

                OnStateMessage(new StateMessageEventArgs($"EasyPipes.Client > {pipeName} > Try Connect"));

                // Create the NamedPipeClientStream with configurable parameters
                NamedPipeClientStream stream;
                lock (objectLockPipeConnect)
                {
                    if (!IsStarted) return false;
                    stream = new NamedPipeClientStream(serverName, pipeName, pipeDirection,
                        pipeOptions | PipeOptions.Asynchronous, tokenImpersonationLevel);
                    PipeClient = stream;
                }


                // Connect the client with an optional timeout (in milliseconds) convert from seconds
                await stream.ConnectAsync(timeout * 1000, lifetime.Token).ConfigureAwait(false);
                if (!IsStarted) { PipeDispose(); return false; }
                //PipeClient.ReadMode = TransmissionMode;
                //due to a bug in netstandard stuck in byte mode
                /*System.UnauthorizedAccessException: Access to the path is denied.
                at System.IO.Pipes.PipeStream.set_ReadMode(PipeTransmissionMode value
                can only be fixed with .net core etc
                */


                OnStateMessage(new StateMessageEventArgs($"EasyPipes.Client > {pipeName} > CreateAndWaitForConnectionAsync > Connected"));

            }
            catch (TimeoutException e)
            {
                IsErrors = true;
                OnStateMessage(new StateMessageEventArgs($"EasyPipes.Client > {pipeName} > CreateAndWaitForConnectionAsync > Timeout:\n{e.Message}"));
            }
            catch (Exception e)
            {
                IsErrors = true;
                OnStateMessage(new StateMessageEventArgs($"EasyPipes.Client > {pipeName} > CreateAndWaitForConnectionAsync > ERROR:\n{e}"));
            }

            finally
            {
                if (IsErrors) PipeDispose();
                isWaitingConnection = false;
            }
            return IsStateReady();
        }

        /// <summary>
        /// Checks if the pipe client is in a connected and error-free state.
        /// </summary>
        /// <returns></returns>
        public bool IsStateReady()
        {
            var stream = PipeClient;
            try { return !IsErrors && stream != null && stream.IsConnected; }
            catch (ObjectDisposedException) { return false; }
        }

        /// <summary>
        /// Asynchronously reads messages from the pipe, processes them, and raises the MessageReceived event.
        /// </summary>
        /// <returns></returns>
        public async Task<bool> TryReadMessagesAsync()
        {
            if (!IsStateReady() || !IsStarted) return false;

            if (isReading) return false;
            lock (objectLockReader)
            {
                if (isReading) return false;
                isReading = true;
            }
            var stream = PipeClient;
            try
            {
                if (stream == null || !stream.CanRead) return false;
                Message message = await StreamIO.ReadMessageAsync(stream, MessageTimeoutMillis).ConfigureAwait(false);

                if (message.IsValid)
                {
                    message.PipeName=PipeName;
                    MessageEventArgs args = new MessageEventArgs(message);
                    OnMessageReceived(args);
                }
                isReading = false;
                return message.IsValid;
            }
            catch (Exception e)
            {
                FailConnection(stream);
                OnStateMessage(new StateMessageEventArgs($"EasyPipes.Client > {PipeName} > ReadMessagesAsync > ERROR:\n{e}"));
            }
            finally { isReading = false; }
            return false;
        }

        /// <summary>
        /// Invokes the MessageReceived event to notify about a new message.
        /// </summary>
        /// <param name="e"></param>
        protected virtual void OnMessageReceived(MessageEventArgs e)
        {
            foreach (EventHandler<MessageEventArgs> handler in MessageReceived?.GetInvocationList() ?? Array.Empty<Delegate>())
                try { handler(this, e); } catch (Exception ex) { Console.WriteLine(ex); }
        }

        /// <summary>
        /// Raises the StateMessage event to update the current state of the pipe client.
        /// </summary>
        /// <param name="e"></param>
        protected virtual void OnStateMessage(StateMessageEventArgs e)
        {
            foreach (EventHandler<StateMessageEventArgs> handler in StateMessage?.GetInvocationList() ?? Array.Empty<Delegate>())
                try { handler(this, e); } catch (Exception ex) { Console.WriteLine(ex); }
        }

        /// <summary>
        /// Disposes of the pipe client and its resources.
        /// </summary>
        public void Dispose()
        {
            IsStarted = false;
            lifetime.Cancel();
            PipeDispose();
        }
    }
}
