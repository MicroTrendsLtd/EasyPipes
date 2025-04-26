//-----------------------------------------------------------------------
// <copyright company="MicroTrends Ltd, https://github.com/MicroTrendsLtd">
//     Author: Tom Leeson
//     Copyright (c) 2025 MicroTrends Ltd. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EasyPipes
{
    /// <summary>
    /// Provides functionality for reading and writing data to and from a stream using byte arrays and UTF-8 encoded strings.
    /// Supports robust reading and writing up to 2GB using messages with 2byte token and 4byte headers
    /// </summary>
    public class StreamIO
    {
        //2 byte Header token to identify start of message
        internal const ushort MessageToken = (ushort)((0x54 << 8) | 0x4C); // Equivalent to "TL" coder ;-)



        /// <summary>
        /// Reads data from a stream asynchronously and returns a Message object containing the raw byte data and the string representation.
        /// </summary>
        /// <param name="ioStream">The input stream from which to read the data.</param>
        /// <returns>A Message object containing the byte array and the string data.</returns>
        public static async Task<Message> ReadAsync(Stream ioStream)
        {
            if (ioStream == null)
                throw new ArgumentNullException(nameof(ioStream));

            byte[] inBuffer = await ReadBytesChunkAsync(ioStream).ConfigureAwait(false);
            Message message = new Message
            {
                Bytes = inBuffer,
                DateTimeSent = DateTimeOffset.UtcNow,
                Body = Encoding.UTF8.GetString(inBuffer)
            };

            return message;
        }

        /// <summary>
        /// Reads a chunked byte array from the stream using a 4-byte length header.
        /// First, it reads a 2-byte token to validate the message; then, it reads the 4-byte length header and the full message data.
        /// </summary>
        /// <param name="ioStream">The input stream from which to read the data.</param>
        /// <param name="expectedToken">The expected 2-byte token (default is MessageToken).</param>
        /// <returns>A byte array containing the fully read data.</returns>
        public static async Task<byte[]> ReadBytesChunkAsync(Stream ioStream, ushort expectedToken = MessageToken)
        {
            if (ioStream == null)
                throw new ArgumentNullException(nameof(ioStream));

            // Step 1: Read the 2-byte token from the stream.
            byte[] tokenBuffer = new byte[2];
            await ReadExactlyAsync(ioStream, tokenBuffer, 0, 2).ConfigureAwait(false);

            // Convert the first two bytes to a token and validate.
            ushort token = BitConverter.ToUInt16(tokenBuffer, 0);
            if (token != expectedToken)
            {
                Console.WriteLine($"Invalid token received: {token}. Expected: {expectedToken}. Flushing the stream...");
                await FlushStreamAsync(ioStream).ConfigureAwait(false);
                throw new InvalidDataException("Invalid token. Stream flushed.");
            }

            // Step 2: Read the 4-byte length header to get the message size.
            byte[] lengthBuffer = new byte[4];
            await ReadExactlyAsync(ioStream, lengthBuffer, 0, 4).ConfigureAwait(false);

            int messageSize = BitConverter.ToInt32(lengthBuffer, 0);
            if (messageSize < 0)
                throw new InvalidDataException("Negative message size encountered.");

            // Step 3: Read the full message based on the message size.
            return await ReadBytesChunkedAsync(ioStream, messageSize).ConfigureAwait(false);
        }


        /// <summary>
        /// Flushes the input stream by reading and discarding available data.
        /// This helps to resynchronize the stream after detecting a corrupted or invalid message.
        /// </summary>
        /// <param name="ioStream">The input stream to flush.</param>
        /// <param name="cancellationToken">A cancellation token (default is none).</param>
        /// <returns>A task representing the asynchronous flush operation.</returns>
        public static async Task FlushStreamAsync(Stream ioStream, CancellationToken cancellationToken = default)
        {
            if (ioStream == null)
                throw new ArgumentNullException(nameof(ioStream));

            byte[] buffer = new byte[1024];
            try
            {
                if (ioStream.CanSeek)
                {
                    // Track the position to detect if it doesn't change after a read.
                    long previousPosition = ioStream.Position;
                    while (ioStream.Length - ioStream.Position > 0)
                    {
                        int bytesRead = await ioStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                        if (bytesRead == 0)
                            break;
                        if (ioStream.Position == previousPosition)
                            break;
                        previousPosition = ioStream.Position;
                    }
                }
                else
                {
                    // For non-seekable streams, perform a single read attempt.
                    await ioStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (NotSupportedException)
            {
                // If the stream does not support the required operations, simply exit.
            }
        }



        /// <summary>
        /// Reads a chunked byte array from the stream, ensuring all data is read.
        /// </summary>
        /// <param name="ioStream">The input stream from which to read the data.</param>
        /// <param name="messageSize">The total number of bytes to read (the message size).</param>
        /// <returns>A byte array containing the fully read data.</returns>
        public static async Task<byte[]> ReadBytesChunkedAsync(Stream ioStream, int messageSize)
        {
            if (ioStream == null)
                throw new ArgumentNullException(nameof(ioStream));
            if (messageSize < 0)
                throw new ArgumentOutOfRangeException(nameof(messageSize), "Message size cannot be negative.");

            byte[] buffer = new byte[messageSize]; // Allocate buffer based on the message size.
            await ReadExactlyAsync(ioStream, buffer, 0, messageSize).ConfigureAwait(false);
            return buffer;
        }


        /// <summary>
        /// Reads exactly the specified number of bytes from the stream asynchronously.
        /// </summary>
        /// <param name="stream">The stream from which to read.</param>
        /// <param name="buffer">The buffer into which the data will be read.</param>
        /// <param name="startIndex">The starting index in the buffer to begin reading data.</param>
        /// <param name="count">The number of bytes to read.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public static async Task ReadExactlyAsync(Stream stream, byte[] buffer, int startIndex, int count)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));
            if (startIndex < 0 || startIndex >= buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(startIndex));
            if (count < 0)
                throw new ArgumentException("The number of bytes to read cannot be negative.", nameof(count));
            if (startIndex + count > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(count), $"'{nameof(count)}' is greater than the length of '{nameof(buffer)}'.");
            if (!stream.CanRead)
                throw new InvalidOperationException("Stream is not readable.");

            int offset = 0;
            while (offset < count)
            {
                // Note: After connecting, an immediate read is attempted; so a 4-byte header is expected here.
                int readCount = await stream.ReadAsync(buffer, startIndex + offset, count - offset).ConfigureAwait(false);
                if (readCount == 0)
                    throw new EndOfStreamException("End of the stream reached.");
                offset += readCount;
            }
        }


        /// <summary>
        /// Converts a string to a UTF-8 byte array and writes it to the stream.
        /// This method handles writing the byte array in either small or large chunks.
        /// </summary>
        /// <param name="ioStream">The output stream to which the data will be written.</param>
        /// <param name="outString">The string to convert to UTF-8 and write.</param>
        /// <returns>The total number of bytes written, including the chunk length headers.</returns>
        public static async Task<int> WriteStringAsync(Stream ioStream, string outString)
        {
            if (ioStream == null)
                throw new ArgumentNullException(nameof(ioStream));
            if (outString == null)
                throw new ArgumentNullException(nameof(outString));

            byte[] outBuffer = Encoding.UTF8.GetBytes(outString);
            return await WriteBytesAsync(ioStream, outBuffer).ConfigureAwait(false);
        }




        /// <summary>
        /// Writes a byte array to the stream using a 2-byte token followed by a 4-byte length header.
        /// The token helps to identify the start of a valid message.
        /// The size of the byte array must not exceed 2GB due to .NET's array size limit.
        /// </summary>
        /// <param name="ioStream">The output stream to which the data will be written.</param>
        /// <param name="outBuffer">The byte array to write.</param>
        /// <param name="token">A 2-byte identifier token to mark the start of the message.</param>
        /// <returns>The total number of bytes written, including the token and the 4-byte length header.</returns>
        public static async Task<int> WriteBytesAsync(Stream ioStream, byte[] outBuffer, ushort token = 0x544C)
        {
            if (ioStream == null)
                throw new ArgumentNullException(nameof(ioStream));
            if (outBuffer == null)
                throw new ArgumentNullException(nameof(outBuffer));

            int len = outBuffer.Length;

            // Write the 2-byte token (little-endian by default).
            byte[] tokenBytes = BitConverter.GetBytes(token);
            await ioStream.WriteAsync(tokenBytes, 0, 2).ConfigureAwait(false);

            // Write the length header as the next 4 bytes.
            byte[] lengthHeader = BitConverter.GetBytes((uint)len); // Using uint for clarity.
            await ioStream.WriteAsync(lengthHeader, 0, 4).ConfigureAwait(false);

            // Write the actual data to the stream.
            await ioStream.WriteAsync(outBuffer, 0, len).ConfigureAwait(false);

            // Flush the stream to ensure all data is sent.
            await ioStream.FlushAsync().ConfigureAwait(false);

            // Return the total number of bytes written (data + 2-byte token + 4-byte length header).
            return len + 6;
        }
    }
}