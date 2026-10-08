# Two-way communication

Version 2026.10.8.2 adds client sending and server receiving. Existing server-Out/client-In defaults remain unchanged. Set both peers to InOut before starting:

```csharp
var server = new EasyPipes.Server("MyDuplexPipe")
{
    PipeIODirection = System.IO.Pipes.PipeDirection.InOut
};
var client = new EasyPipes.Client("MyDuplexPipe")
{
    PipeIODirection = System.IO.Pipes.PipeDirection.InOut
};

server.MessageReceived += (sender, e) => Console.WriteLine("Server received: " + e.Body);
client.MessageReceived += (sender, e) => Console.WriteLine("Client received: " + e.Body);

var listening = server.StartAsync();
await client.StartAsync(); // Starts a background connection/read loop.
await listening;          // Completes once a client connects.

await client.TryConnectSendMessageAsync("Hello from client");
await server.TryConnectSendMessageAsync("Hello from server");

await client.StopAsync();
await server.StopAsync();
```

Both peers receive automatically through MessageReceived. One reader and a serialized writer can operate simultaneously on each connection. Both expose StateMessage for diagnostics. Event callbacks run on background threads; dispatch UI updates to your UI thread and keep handlers short. Await StopAsync outside synchronous event callbacks.

TrySendMessageAsync sends on an established writable connection. TryConnectSendMessageAsync attempts to connect first when needed. Check the returned bool; failed messages are not replayed. A successful send confirms writing, not application processing. Add your own request identifiers and reply messages if acknowledgements are required.

The duplex server automatically listens again after a disconnect, malformed frame, or read timeout. The client automatically reconnects. Send-only server mode retains its existing reconnect-on-next-send behavior. Both peers retain bounded sends and partial-frame reads; idle connections have no read timeout. Configure MessageTimeoutMillis on each peer and StreamIO.MaxMessageBytes as described in RECOVERY.md.

The wire format and netstandard2.0 target are unchanged. Duplex requires both peers to be configured for InOut. The Server instance continues to serve one active peer at a time.

Regression checks are in PipeRegression/Program.cs, including simultaneous concurrent duplex sends, reconnection, subscriber exceptions, client write timeout, server partial-frame recovery, and shutdown with both readers waiting.
