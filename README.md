# EasyPipes

**Target framework:** .NET Standard 2.0

**Platform:** Windows

EasyPipes is a named-pipe client and server library for asynchronous inter-process communication using `System.IO.Pipes`. It provides UTF-8 messaging, configurable pipe settings, bounded sends, and connection recovery.

Version **2026.10.8.1** adds hang prevention and recovery improvements. Version **2026.10.8.2** adds simultaneous two-way communication.

> The source on `master` is currently version 2026.10.8.1. The 2026.10.8.2 duplex implementation has been built and tested locally and must be published before the two-way example below can be used with a GitHub checkout.

![EasyPipes example](https://github.com/user-attachments/assets/d2f707f8-0628-47d1-9ddf-3a2468c14026)

## Features

- Simple `Server` and `Client` classes with asynchronous lifecycle and messaging methods.
- One-way server-to-client messaging by default.
- Simultaneous send and receive in version 2026.10.8.2 when both peers use `PipeDirection.InOut`.
- Serialized sends preserve message boundaries during concurrent writes.
- Send timeouts close stalled connections.
- Shutdown closes streams directly and interrupts pending I/O without waiting for the peer to drain the pipe.
- Incomplete or invalid frames trigger receive-side reconnection.
- Event-handler exceptions are isolated so other subscribers can continue.
- Configurable pipe names, directions, options, server transmission mode, and buffer sizes.

The .NET Standard 2.0 target supports compatible .NET Framework and modern .NET consumers. The library's public operations are asynchronous; await them from the calling application.

## Message framing and limits

Messages contain:

1. A 2-byte `TL` token.
2. A 4-byte payload length.
3. The UTF-8 payload.

The configurable maximum payload size, `StreamIO.MaxMessageBytes`, defaults to **64 MiB**. Oversized frames are rejected before allocation.

The length is read as a signed 32-bit integer and payloads use .NET byte arrays, so the format does **not** provide 4 GB message support. Actual usable sizes also depend on available memory and runtime limits. Configure the same size limit in both communicating applications if larger messages are required.

## API overview

| API | Server | Client |
| --- | --- | --- |
| Constructor | `Server(string pipeName)` | `Client(string pipeName)` |
| `StartAsync()` | Starts listening and waits for a connection | Starts the background connection/read loop |
| `StopAsync()` | Stops and interrupts pending I/O | Stops and waits for the background reader |
| `TrySendMessageAsync(string message, int timeoutMillis = 5000)` | Sends on an established writable connection | Available in 2026.10.8.2 |
| `TryConnectSendMessageAsync(string message)` | Connects if necessary before sending | Available in 2026.10.8.2; also accepts a send timeout |
| `MessageReceived` | Available in 2026.10.8.2 | Receives complete messages |
| `StateMessage` | Available in 2026.10.8.2 | Receives diagnostic messages |

Read received text through `MessageEventArgs.Body` or `MessageEventArgs.Message.Body`.

## Quick start: tester applications

1. Download or clone the repository and open `EasyPipes.sln` in Visual Studio.
2. Build the library and tester projects.
3. Set `EasyPipeServerTest` as the startup project and run it.
4. Open a second Visual Studio instance, set `EasyPipeClientTest` as the startup project, and run it.
5. With the 2026.10.8.2 tester updates, both applications send a greeting and display incoming messages. Type into either console to send to the other; use `/quit` or Ctrl+C to stop.
6. In the updated server tester, enter `/csv` to send 10,000 demo rows while continuing to receive client messages.

The duplex tester updates are local until the 2026.10.8.2 source is published. The 2026.10.8.1 testers demonstrate one-way server-to-client traffic.

The library targets .NET Standard 2.0. The supplied tester projects target .NET 8.

## Two-way communication: version 2026.10.8.2

Set both peers to `PipeDirection.InOut` before starting. Both peers then receive automatically through `MessageReceived` while sending independently.

```csharp
using System;
using System.IO.Pipes;
using EasyPipes;

var server = new Server("MyDuplexPipe")
{
    PipeIODirection = PipeDirection.InOut
};

var client = new Client("MyDuplexPipe")
{
    PipeIODirection = PipeDirection.InOut
};

server.MessageReceived += (_, e) =>
    Console.WriteLine("Server received: " + e.Body);

client.MessageReceived += (_, e) =>
    Console.WriteLine("Client received: " + e.Body);

var listening = server.StartAsync();
await client.StartAsync();
await listening;

bool sentToServer =
    await client.TryConnectSendMessageAsync("Hello from client");

bool sentToClient =
    await server.TryConnectSendMessageAsync("Hello from server");

// Keep both peers running for your application's lifetime.
// Stop them when the application is finished:
await client.StopAsync();
await server.StopAsync();
```

This example shows both peers in one process; they can also run in separate applications using the same pipe name. A `Server` instance serves one active peer at a time.

Event callbacks run on background threads. Dispatch UI changes to the UI thread, keep callbacks short, and await shutdown outside synchronous event callbacks.

## Timeouts and recovery

| Setting | Default | Behavior |
| --- | --- | --- |
| `TrySendMessageAsync` timeout | 5 seconds | Bounds a stalled write and closes the failed connection |
| `MessageTimeoutMillis` | 30 seconds | Bounds frame assembly after the first byte arrives |
| `StreamIO.MaxMessageBytes` | 64 MiB | Limits payload size |

The client exposes `MessageTimeoutMillis`; version 2026.10.8.2 exposes it on the server as well. Healthy idle connections can wait indefinitely for the first byte.

The client reconnects automatically after a receive failure. In duplex mode, the server also listens again after a receive failure or disconnect. In the default send-only server mode, use `TryConnectSendMessageAsync` for subsequent sends that should reconnect.

Check the boolean result of send methods. Failed messages are **not automatically replayed**, and a successful write does not acknowledge that the receiving application processed the message. Add application-level request identifiers and replies when acknowledgements are needed.

Deploy the rebuilt `EasyPipes.dll` to consuming applications to receive source updates.

## Validation

The recovery implementation passed 13 regression checks. The locally built 2026.10.8.2 implementation passed 18 checks, including simultaneous duplex traffic, concurrent frame writes, reconnecting without restarting the server, stalled writes, partial frames, subscriber exceptions, and shutdown during pending I/O.

## Contributing

1. Fork the repository.
2. Create a feature branch.
3. Implement and test your changes.
4. Commit and push the branch.
5. Open a pull request.

See [LICENSE.txt](LICENSE.txt) for license details.
