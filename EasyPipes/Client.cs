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
using System.Threading.Tasks;

namespace EasyPipes
{
    /// <summary>
    /// The Client class implements a pipe client using NamedPipeClientStream for inter-process communication. It provides methods to connect asynchronously to a pipe server, read messages, and manage the connection state. The class handles message transmission, error reporting, and resource disposal. Events are raised for message reception (MessageReceived) and state updates (StateMessage). It supports asynchronous operations like connection retries and message reading while ensuring thread safety with locking mechanisms. Additionally, the class includes customizable pipe options and implements the IDisposable interface for clean resource management.
    /// </summary>
    public class Client : IPipeLayer, IDisposable
    {
        private readonly object objectLockPipeConnect = new object();
        private readonly object objectLockReader = new object();
        private bool isWaitingConnection;
        private bool isReading;
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
            Console.WriteLine("StartAsync");
            OnStateMessage(new StateMessageEventArgs($"{PipeName} > Start"));
            await Task.Delay(1000);
            //let this go so can return back to main thread
            IsStarted = true;
            _ = DoConnectAndReadInternalAsync();
        }

        /// <summary>
        /// Stops the pipe client, disposes of resources, and logs the stop state.
        /// </summary>
        /// <returns></returns>
        public async Task StopAsync()
        {
            Console.WriteLine("StopAsync");
            IsStarted = false;
            await Task.Delay(1000);
            try
            {
                OnStateMessage(new StateMessageEventArgs($"{PipeName} > Stop"));
                PipeDispose();
            }
            catch (Exception e)
            {
                OnStateMessage(new StateMessageEventArgs($"{PipeName} > Stop >  ERROR:\n{e}"));
            }
            IsErrors = false;
        }

        /// <summary>
        /// Safely disposes the pipe client, handling disconnection and error states
        /// </summary>
        private void PipeDispose()
        {
            try
            {
                if (PipeClient != null)
                {
                    Console.WriteLine("PipeDispose");

                    // Check if the pipe is still connected before attempting to flush
                    if (PipeClient.IsConnected)
                    {
                        try
                        {
                            PipeClient.Flush();
                        }
                        catch (IOException ioEx)
                        {
                            Console.WriteLine($"PipeDispose error during flush: {ioEx.Message}");
                            // It's fine if the pipe is already broken during flush, handle this gracefully
                        }
                    }

                    // Always close and dispose of the pipe
                    PipeClient.Close(); // Close the pipe
                    PipeClient.Dispose(); // Dispose of the pipe
                }
            }
            catch (Exception e)
            {
                OnStateMessage(new StateMessageEventArgs($"{PipeName} > PipeDispose > ERROR:\n{e}"));
            }
            finally
            {
                // Reset state to reflect that the client is no longer connected
                IsErrors = false;
                PipeClient = null;
            }
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
                    if (await TryCreateAndWaitForConnectionAsync())
                        break;
                }

                while (IsStarted && IsStateReady())
                {
                    await TryReadMessagesAsync();
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
                PipeClient = new NamedPipeClientStream(serverName, pipeName, pipeDirection, pipeOptions, tokenImpersonationLevel);


                // Connect the client with an optional timeout (in milliseconds) convert from seconds
                await PipeClient.ConnectAsync(timeout * 1000);
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

            if (IsErrors)
            {
                PipeDispose();  // Clean up resources in case of error
            }
            isWaitingConnection = false;
            return IsStateReady();
        }

        /// <summary>
        /// Checks if the pipe client is in a connected and error-free state.
        /// </summary>
        /// <returns></returns>
        public bool IsStateReady()
        {
            return (!IsErrors && PipeClient != null && PipeClient.IsConnected);
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
            try
            {
                Message message = await StreamIO.ReadAsync(PipeClient);

                if (message != null && message.IsValid)
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
                IsErrors = true;
                OnStateMessage(new StateMessageEventArgs($"EasyPipes.Client > {PipeName} > ReadMessagesAsync > ERROR:\n{e}"));
            }
            isReading = false;
            return false;
        }

        /// <summary>
        /// Invokes the MessageReceived event to notify about a new message.
        /// </summary>
        /// <param name="e"></param>
        protected virtual void OnMessageReceived(MessageEventArgs e)
        {
            MessageReceived?.Invoke(this, e);
        }

        /// <summary>
        /// Raises the StateMessage event to update the current state of the pipe client.
        /// </summary>
        /// <param name="e"></param>
        protected virtual void OnStateMessage(StateMessageEventArgs e)
        {
            StateMessage?.Invoke(this, e);
        }

        /// <summary>
        /// Disposes of the pipe client and its resources.
        /// </summary>
        public void Dispose()
        {
            PipeDispose();
        }
    }
}