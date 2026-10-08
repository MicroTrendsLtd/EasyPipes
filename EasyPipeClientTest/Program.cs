using EasyPipes;
using System.IO.Pipes;

namespace EasyPipeClientTest
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            using var shutdown = new CancellationTokenSource();
            using var client = new Client("test") { PipeIODirection = PipeDirection.InOut, TimeOut = 5 };
            client.MessageReceived += (_, e) => Console.WriteLine($"Server > {e.Body}");
            client.StateMessage += (_, e) => Console.WriteLine($"[Client] {e.Message}");
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; shutdown.Cancel(); };

            try
            {
                Console.WriteLine("Duplex client: connecting to pipe 'test'...");
                await client.StartAsync();
                while (!client.IsStateReady())
                    await Task.Delay(100, shutdown.Token);

                Console.WriteLine("Connected. Type a message to send, or /quit to stop. Ctrl+C also stops.");
                Console.WriteLine(await client.TrySendMessageAsync("Hello from client")
                    ? "Sent greeting." : "Greeting failed; connection may have closed.");
                while (!shutdown.IsCancellationRequested)
                {
                    var line = await Console.In.ReadLineAsync(shutdown.Token);
                    if (line == null || line.Equals("/quit", StringComparison.OrdinalIgnoreCase)) break;
                    if (line.Length == 0) continue;
                    Console.WriteLine(await client.TryConnectSendMessageAsync(line)
                        ? "Sent." : "Send failed; message was not replayed. Try again when connected.");
                }
            }
            catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
            catch (Exception ex) { Console.Error.WriteLine(ex.Message); Environment.ExitCode = 1; }
            finally
            {
                await client.StopAsync();
                Console.WriteLine("Client stopped.");
            }
        }
    }
}
