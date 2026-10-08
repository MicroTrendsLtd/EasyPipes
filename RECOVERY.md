# Pipe recovery

Ported the ATS.Messaging.Pipes recovery fix while retaining the EasyPipes namespace, netstandard2.0 target, public methods, and TL token / four-byte length framing.

- All pipe streams enable asynchronous I/O.
- Sends are serialized. A stalled write times out (default 5 seconds), closes its connection, and releases the send gate.
- Stop closes streams directly without waiting for the peer to drain them, interrupting pending I/O.
- Client start is idempotent; stop waits for the tracked reader before restarting.
- Idle clients wait for the first byte without a timeout. Once a frame begins, Client.MessageTimeoutMillis limits assembly (default 30 seconds).
- Invalid or incomplete frames cause client reconnection. Subscriber exceptions are isolated and logged.
- StreamIO.MaxMessageBytes defaults to 64 MiB; configure it on both peers for larger messages.

TrySendMessageAsync returns false on failure. Use TryConnectSendMessageAsync for later messages that should reconnect. Failed messages are not automatically replayed; a successful write does not acknowledge application processing. Concurrent calls send their own supplied message.

Run regression checks from the repository root:

    dotnet run --project PipeRegression/PipeRegression.csproj -c Release -p:RestoreFallbackFolders= -p:NuGetAudit=false

The harness uses real Windows named pipes for stalled writes, recovery without restart, concurrent sends, partial frames, idle connections, subscriber failures, shutdown during pending I/O, and connection timeouts. Memory streams cover empty, invalid, and oversized frames.

Build with:

    dotnet build EasyPipes/EasyPipes.csproj -c Release -p:RestoreFallbackFolders= -p:NuGetAudit=false

Deploy EasyPipes/bin/Release/netstandard2.0/EasyPipes.dll to consuming applications to receive these fixes.


Two-way send/receive is available in version 2026.10.8.2; see DUPLEX.md for setup and usage.
