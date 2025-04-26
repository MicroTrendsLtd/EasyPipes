//-----------------------------------------------------------------------
// <copyright company="MicroTrends Ltd, https://github.com/MicroTrendsLtd">
//     Author: Tom Leeson
//     Copyright (c) 2025 MicroTrends Ltd. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using System;

namespace EasyPipes
{
    /// <summary>
    /// Defines the structure of a message for the pipe system, containing properties like Body, Bytes, DateTimeSent, and PipeName.
    /// </summary>
    public interface IMessage
    {
        string Body { get; }
        byte[] Bytes { get;  }
        DateTimeOffset DateTimeSent { get; }
        string PipeName { get; }
    }

    /// <summary>
    /// Represents a message sent through a pipe, with properties like Body (message content), Bytes (message as byte array), PipeName (pipe's identifier), and DateTimeSent. Includes an IsValid property to check the message's validity and a custom ToString() method for easy message representation.
    /// </summary>
    public class Message : IMessage
    {
        public string PipeName { get; set; }
        public string Body { get; set; }
        public byte[] Bytes { get; set; }
        public DateTimeOffset DateTimeSent { get; set; } = DateTimeOffset.UtcNow;
        public bool IsValid { get => !string.IsNullOrEmpty(Body) && Bytes != null; }
        public override string ToString()
        {
            return $"{nameof(Message)}: Pipe: {PipeName}, Sent: {DateTimeSent}, Size: {Bytes.Length} bytes, Body:\n{Body}";
        }

    }

    /// <summary>
    /// Event arguments used when a message is received. Wraps the Message object and implements IMessage for easy access to message properties like Body, Bytes, and PipeName.
    /// </summary>
    public class MessageEventArgs : EventArgs, IMessage
    {
        public Message Message{ get;}
        public MessageEventArgs(Message message)
        {
            this.Message = message;
        }
        public string Body { get => Message.Body; }
        public byte[] Bytes { get => Message.Bytes;}
        public DateTimeOffset DateTimeSent { get => Message.DateTimeSent; }
        public string PipeName { get => Message.PipeName; }
    }

    /// <summary>
    /// Event arguments for state changes in the pipe client or server, holding a state message (typically for logging or debugging purposes).
    /// </summary>
    public class StateMessageEventArgs : EventArgs
    {
        public StateMessageEventArgs(string message)
        {
            Message = message ?? string.Empty;
        }
        public string Message { get; set; }

    }

}
