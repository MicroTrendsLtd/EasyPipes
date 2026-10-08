using EasyPipes;
using System.IO.Pipes;
using System.Text;

namespace EasyPipeServerTest
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            using var shutdown = new CancellationTokenSource();
            var server = new Server("test") { PipeIODirection = PipeDirection.InOut, TimeOut = 5 };
            server.MessageReceived += (_, e) => Console.WriteLine($"Client > {e.Body}");
            server.StateMessage += (_, e) => Console.WriteLine($"[Server] {e.Message}");
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; shutdown.Cancel(); };

            try
            {
                Console.WriteLine("Duplex server: listening on pipe 'test'...");
                while (!server.IsStateReady())
                {
                    // Stop interrupts a connection wait when Ctrl+C is pressed.
                    var listening = server.StartAsync();
                    try { await listening.WaitAsync(shutdown.Token); }
                    catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
                    {
                        await server.StopAsync();
                        await listening;
                        throw;
                    }
                    if (!server.IsStateReady()) await Task.Delay(250, shutdown.Token);
                }
                Console.WriteLine("Connected. Type a message, /csv for 10,000 demo rows, or /quit. Ctrl+C also stops.");
                Console.WriteLine(await server.TrySendMessageAsync("Hello from server")
                    ? "Sent greeting." : "Greeting failed; connection may have closed.");
                while (!shutdown.IsCancellationRequested)
                {
                    var line = await Console.In.ReadLineAsync(shutdown.Token);
                    if (line == null || line.Equals("/quit", StringComparison.OrdinalIgnoreCase)) break;
                    if (line.Length == 0) continue;
                    if (line.Equals("/csv", StringComparison.OrdinalIgnoreCase))
                        await SendCsvAsync(server, shutdown.Token);
                    else
                        Console.WriteLine(await server.TryConnectSendMessageAsync(line)
                            ? "Sent." : "Send failed; message was not replayed. Try again when connected.");
                }
            }
            catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
            catch (Exception ex) { Console.Error.WriteLine(ex.Message); Environment.ExitCode = 1; }
            finally
            {
                await server.StopAsync();
                Console.WriteLine("Server stopped.");
            }
        }

        private static async Task SendCsvAsync(Server server, CancellationToken token)
        {
            Directory.CreateDirectory("data");
            var path = Path.Combine("data", "large_data.csv");
            GenerateCsvFile(path, 10000);
            using var reader = new StreamReader(path);
            await reader.ReadLineAsync(token); // Skip header.
            int count = 0;
            while (await reader.ReadLineAsync(token) is string line)
            {
                token.ThrowIfCancellationRequested();
                if (!await server.TrySendMessageAsync(line))
                {
                    Console.WriteLine($"CSV send stopped after {count} rows. Failed row was not replayed.");
                    return;
                }
                count++;
                if (count % 500 == 0) Console.WriteLine($"Sent {count} rows...");
            }
            Console.WriteLine($"Sent all {count} CSV rows. Client messages can be received during transfer.");
        }

        public static void GenerateCsvFile(string filePath, int numberOfRecords)
        {
            var csv = new StringBuilder("ID,Name,Age,City\n");
            var random = new Random();
            for (int i = 1; i <= numberOfRecords; i++)
                csv.AppendLine($"{i},Name_{random.Next(1000, 9999)},{random.Next(18, 80)},City_{random.Next(1, 100)}");
            File.WriteAllText(filePath, csv.ToString());
        }
    }
}
