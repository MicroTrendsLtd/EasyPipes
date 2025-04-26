//-----------------------------------------------------------------------
// <copyright company="MicroTrends Ltd, https://github.com/MicroTrendsLtd">
//     Author: Tom Leeson
//     Copyright (c) 2025 MicroTrends Ltd. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using System.IO.Pipes;

namespace EasyPipes
{
    /// <summary>
    /// Represents a pipe layer interface, providing key properties for managing pipe connections and configurations:
    /// - IsStarted: Indicates whether the pipe connection is active.
    /// - PipeIODirection: Defines the direction of data flow (e.g., In, Out, InOut).
    /// - PipeName: The name or identifier of the pipe.
    /// - TimeOut: Specifies the timeout duration for pipe operations.
    /// - PipeIOOptions: Defines pipe operation options (e.g., Asynchronous, WriteThrough).
    /// - TransmissionMode: Specifies the data transmission mode (e.g., Byte, Message).
    /// </summary>
    public interface IPipeLayer
    {
        bool IsStarted { get; set; }
        PipeDirection PipeIODirection { get; set; }
        string PipeName { get; set; }
        int TimeOut { get; set; }
        PipeOptions PipeIOOptions { get; set; }
        PipeTransmissionMode TransmissionMode { get; set; }
    }
}
