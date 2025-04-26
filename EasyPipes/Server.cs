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
    public class Server : IPipeLayer
    {
        #region vars & props
        private readonly object objectLockPipeConnect = new object();
        private readonly object objectLockPipeSender = new object();
        private bool isWaitingConnection;
        private bool isSendMessage;

        public NamedPipeServerStream PipeServer { get; set; }
        public int ThreadId { get; private set; }
        public string PipeName { get; set; } = "PipeATS";
        public Server(string pipeName)
        {
            PipeName = pipeName;
        }
        public string Message { get; set; }
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
            Console.WriteLine($"EasyPipes.Server > {PipeName} > TryCreateAndWaitForConnectionAsync > Start");
            IsStarted = true;
            await TryCreateAndWaitForConnectionAsync(PipeName);
        }

        /// <summary>
        /// Checks if the pipe server is ready and connected.
        /// </summary>
        /// <returns>True if the pipe server is connected and no errors occurred, otherwise false.</returns>
        public bool IsStateReady()
        {
            return (PipeServer != null && PipeServer.IsConnected && !IsErrors);
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

            if (isWaitingConnection)
                return false;

            lock (objectLockPipeConnect)
            {
                if (isWaitingConnection)
                    return false;
                isWaitingConnection = true;
            }

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
                await PipeDisposeAsync();

                Console.WriteLine($"TryCreateAndWaitForConnectionAsync > {pipeName} > Starting New PipeServer");

                // Create the NamedPipeServerStream with provided parameters
                PipeServer = new NamedPipeServerStream(
                    pipeName,
                    pipeDirection,
                    maxInstances,
                    mode,
                    pipeOptions,
                    outBufferSize,
                    inBufferSize
                );

                ThreadId = Thread.CurrentThread.ManagedThreadId;

                // Use CancellationTokenSource to cancel connection attempt on timeout
                using (var cts = new CancellationTokenSource())
                {
                    var connectionTask = PipeServer.WaitForConnectionAsync(cts.Token);
                    var timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeOutSeconds), cts.Token);

                    // Wait for either the connection or the timeout
                    if (await Task.WhenAny(connectionTask, timeoutTask) == connectionTask)
                    {
                        // Connection succeeded
                        await connectionTask;
                        PipeServer.WaitForPipeDrain();
                        Console.WriteLine($"TryCreateAndWaitForConnectionAsync > {pipeName} > Connection Success");
                        return true;
                    }
                    else
                    {
                        // Timeout occurred, cancel the connection attempt
                        cts.Cancel();
                        throw new Exception($"Connection TimeOut");
                    }
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine($"TryCreateAndWaitForConnectionAsync > {pipeName} > ERROR:\n{ex}");
                IsErrors = true;
                await PipeDisposeAsync();
                return false;
            }
            finally
            {
                isWaitingConnection = false;
            }
        }

        private async Task PipeDisposeAsync()
        {
            try
            {
                if (PipeServer != null)
                {
                    Console.WriteLine("PipeDisposeAsync");

                    // Check if the pipe is broken or disconnected, and handle gracefully
                    if (PipeServer.IsConnected)
                    {
                        try
                        {
                            await Task.Run(() =>
                            {
                                PipeServer.Flush();
                                PipeServer.WaitForPipeDrain();
                                PipeServer.Disconnect(); // Only disconnect if the pipe is still connected
                            });
                        }
                        catch (IOException ioEx)
                        {
                            Console.WriteLine($"PipeDisposeAsync error during flush/disconnect: {ioEx.Message}");
                        }
                    }

                    // Always dispose, even if the pipe is broken or disconnected
                    PipeServer.Dispose();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"PipeDisposeAsync error: {e}");
            }
            finally
            {
                IsErrors = false;
                PipeServer = null;
            }
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
            return await TrySendMessageAsync(this.Message);
        }

        /// <summary>
        /// Sends a message through the pipe server if a connection is established.
        /// </summary>
        /// <param name="message">The message to send.</param>
        /// <returns>A task representing the result of the send operation. True if successful, otherwise false.</returns>
        public async Task<bool> TrySendMessageAsync(string message, int timeoutMillis = 5000)
        {
            if (!IsStarted)
            {
                return false;
            }

            if (!IsStateReady()) return false;

            // Wait until the previous message sending completes
            var startTime = DateTime.UtcNow;
            while (isSendMessage)
            {
                await Task.Delay(100);
                // Timeout to avoid deadlock in case the flag gets stuck
                if ((DateTime.UtcNow - startTime).TotalMilliseconds > timeoutMillis)
                {
                    Console.WriteLine("TrySendMessageAsync: Timeout waiting for previous message to finish.");
                    return false;
                }
            }

            if (isSendMessage)
                return false;

            if (!IsStarted)
            {
                return false;
            }


            lock (objectLockPipeSender)
            {
                if (isSendMessage)
                    return false;
                isSendMessage = true;
            }



            try
            {
#if DEBUG
                //Console.WriteLine($"TrySendMessage:>{message}");
#endif

                // Ensure that the pipe has completed the last message -- didnt do anything in this mode
                //while (!PipeServer.IsMessageComplete)
                //{
                //    await Task.Delay(100);
                //}

                PipeServer.WaitForPipeDrain();

                await StreamIO.WriteStringAsync(PipeServer, message);
                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine($"EasyPipes.Server > {PipeName} > TrySendMessage > ERROR:{e.Message}");
                IsErrors = true;
                return false;
            }
            finally
            {
                isSendMessage = false;
            }


        }

        /// <summary>
        /// Stops the pipe server asynchronously, disconnecting and disposing of the resources.
        /// </summary>
        /// <returns>A task representing the asynchronous stop operation.</returns>
        public async Task StopAsync()
        {
            IsStarted = false;
            await PipeDisposeAsync();
            IsErrors = false;
        }

    }
}
