using EasyPipes;
using System.IO.Pipes;

static async Task Bound(Task task) => await task.WaitAsync(TimeSpan.FromSeconds(8));
static void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS: " + name); }
static string Name() => "ATS_Regression_" + Guid.NewGuid().ToString("N");

// A non-reading client must not freeze sending or shutdown, and the same server can reconnect.
var name = Name();
var server = new Server(name);
var start = server.StartAsync();
using (var blocked = new NamedPipeClientStream(".", name, PipeDirection.In, PipeOptions.Asynchronous))
{
    await blocked.ConnectAsync(2000); await Bound(start);
    var send = server.TrySendMessageAsync(new string('x', 8 * 1024 * 1024), 200);
    await Bound(send); Check(!await send, "blocked write times out");
}
start = server.TryCreateAndWaitForConnectionAsync();
using (var reader = new NamedPipeClientStream(".", name, PipeDirection.In, PipeOptions.Asynchronous))
{
    await reader.ConnectAsync(2000); await Bound(start);
    var read = StreamIO.ReadAsync(reader);
    Check(await server.TrySendMessageAsync("recovered"), "send after reconnect without restart");
    await Bound(read); Check((await read).Body == "recovered", "recovered payload");
    var incoming = Task.Run(async () => {
        var values = new HashSet<string>();
        for (int i = 0; i < 20; i++) values.Add((await StreamIO.ReadAsync(reader)).Body);
        return values.Count;
    });
    var sends = Enumerable.Range(0, 20).Select(i => server.TrySendMessageAsync("message " + i)).ToArray();
    await Bound(Task.WhenAll(sends)); await Bound(incoming);
    Check(sends.All(t => t.Result) && await incoming == 20, "concurrent sends preserve frame boundaries");
}
await Bound(server.StopAsync());

// Incomplete frames time out; client reconnects and subscriber exceptions do not kill it.
name = Name();
var client = new Client(name) { TimeOut = 1, MessageTimeoutMillis = 200 };
var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
client.StateMessage += (_, _) => throw new Exception("bad logger");
client.MessageReceived += (_, _) => throw new Exception("bad subscriber");
client.MessageReceived += (_, e) => received.TrySetResult(e.Body);
using (var raw = new NamedPipeServerStream(name, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
{
    await client.StartAsync(); await Bound(raw.WaitForConnectionAsync());
    await raw.WriteAsync(new byte[] { 0x4c });
    await Task.Delay(450);
}
using (var raw = new NamedPipeServerStream(name, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
{
    await Bound(raw.WaitForConnectionAsync());
    await Task.Delay(450); // Longer than frame timeout, but idle connections stay alive.
    await StreamIO.WriteStringAsync(raw, "client recovered");
    await Bound(received.Task); Check(await received.Task == "client recovered", "partial frame recovery, idle, subscriber isolation");
    await Bound(client.StopAsync()); Check(!client.IsStarted, "stop while client read pending");
}
await client.StartAsync(); await client.StartAsync(); await Task.Delay(100); await Bound(client.StopAsync());
Check(true, "idempotent start and stop during connection attempt");

name = Name(); server = new Server(name); start = server.StartAsync();
await Bound(server.StopAsync()); await Bound(start); Check(true, "server stop during connection wait");
using var empty = new MemoryStream();
await StreamIO.WriteStringAsync(empty, ""); empty.Position = 0;
Check((await StreamIO.ReadAsync(empty)).Body == "", "empty frame");

name = Name(); server = new Server(name) { IsStarted = true };
var timedConnect = server.TryCreateAndWaitForConnectionAsync(name, 1);
await Bound(timedConnect); Check(!await timedConnect, "server connection timeout");
start = server.TryCreateAndWaitForConnectionAsync();
using (var reader = new NamedPipeClientStream(".", name, PipeDirection.In, PipeOptions.Asynchronous)) {
    await reader.ConnectAsync(2000); await Bound(start);
    Check(server.IsStateReady(), "server reconnect after connection timeout");
}
await Bound(server.StopAsync());
using var invalid = new MemoryStream(new byte[] { 0, 0 });
try { await StreamIO.ReadMessageAsync(invalid, 200); throw new Exception("Invalid token accepted"); }
catch (InvalidDataException) { Check(true, "invalid token rejected immediately"); }
using var oversized = new MemoryStream();
await oversized.WriteAsync(BitConverter.GetBytes((ushort)0x544c));
await oversized.WriteAsync(BitConverter.GetBytes(int.MaxValue)); oversized.Position = 0;
try { await StreamIO.ReadMessageAsync(oversized, 200); throw new Exception("Oversized frame accepted"); }
catch (InvalidDataException) { Check(true, "oversized header rejected before allocation"); }





// Duplex traffic: both readers operate while concurrent writers preserve frames.
name = Name();
server = new Server(name) { PipeIODirection = PipeDirection.InOut, MessageTimeoutMillis = 200, TimeOut = 1 };
client = new Client(name) { PipeIODirection = PipeDirection.InOut, MessageTimeoutMillis = 200, TimeOut = 1 };
var serverMessages = new System.Collections.Concurrent.ConcurrentDictionary<string, byte>();
var clientMessages = new System.Collections.Concurrent.ConcurrentDictionary<string, byte>();
var serverDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
var clientDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
server.MessageReceived += (_, _) => throw new Exception("bad server subscriber");
server.MessageReceived += (_, e) => { serverMessages.TryAdd(e.Body, 0); if (serverMessages.Count == 20) serverDone.TrySetResult(true); };
client.MessageReceived += (_, e) => { clientMessages.TryAdd(e.Body, 0); if (clientMessages.Count == 20) clientDone.TrySetResult(true); };
start = server.StartAsync(); await client.StartAsync(); await Bound(start);
var duplexSends = Enumerable.Range(0, 20).SelectMany(i => new[] {
    server.TrySendMessageAsync("server " + i), client.TrySendMessageAsync("client " + i)
}).ToArray();
await Bound(Task.WhenAll(duplexSends)); await Bound(Task.WhenAll(serverDone.Task, clientDone.Task));
Check(duplexSends.All(t => t.Result) && serverMessages.Keys.All(k => k.StartsWith("client ")) && clientMessages.Keys.All(k => k.StartsWith("server ")), "simultaneous duplex concurrent sends and server subscriber isolation");
await Bound(client.StopAsync());
var reconnected = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
server.MessageReceived += (_, e) => { if (e.Body == "duplex recovered") reconnected.TrySetResult(e.Body); };
await client.StartAsync();
var until = DateTime.UtcNow.AddSeconds(5);
while (!client.IsStateReady() && DateTime.UtcNow < until) await Task.Delay(25);
Check(await client.TryConnectSendMessageAsync("duplex recovered"), "client send after duplex reconnect");
await Bound(reconnected.Task);
await Bound(Task.WhenAll(client.StopAsync(), server.StopAsync()));
Check(true, "duplex stop while both reads pending");
client.Dispose();

// A blocked client writer closes the connection instead of hanging.
name = Name();
client = new Client(name) { PipeIODirection = PipeDirection.InOut, TimeOut = 1 };
using (var raw = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
{
    await client.StartAsync(); await Bound(raw.WaitForConnectionAsync());
    var send = client.TrySendMessageAsync(new string('x', 8 * 1024 * 1024), 200);
    await Bound(send); Check(!await send, "blocked client write times out");
    await Bound(client.StopAsync());
}
client.Dispose();

// Server partial-frame timeout recovers and accepts a replacement duplex peer.
name = Name();
server = new Server(name) { PipeIODirection = PipeDirection.InOut, MessageTimeoutMillis = 200, TimeOut = 1 };
start = server.StartAsync();
using (var raw = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous))
{
    await raw.ConnectAsync(2000); await Bound(start);
    await raw.WriteAsync(new byte[] { 0x4c }); await Task.Delay(450);
}
var serverRecovered = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
server.MessageReceived += (_, e) => serverRecovered.TrySetResult(e.Body);
using (var raw = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous))
{
    await raw.ConnectAsync(3000);
    await StreamIO.WriteStringAsync(raw, "server recovered");
    await Bound(serverRecovered.Task);
    Check(await serverRecovered.Task == "server recovered", "server partial-frame recovery");
}
await Bound(server.StopAsync());
Console.WriteLine("All pipe regression checks passed.");
