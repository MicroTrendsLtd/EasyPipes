using EasyPipes;

namespace EasyPipeClientTest
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            // Small delay to ensure the server is ready
            await Task.Delay(100);

            Console.WriteLine("EasyPipesClient > starting...");

            // Initialize client with the same pipe name "test"
            var client = new Client("test");

            await client.StartAsync();

            //option 1 inline
            //client.MessageReceived += (sender, e) =>
            //{
            //    Console.WriteLine(e.Message.Body);
            //};

            //option2 use a method
            client.MessageReceived += Client_MessageReceived;


           Console.WriteLine("Waiting for data... Press any key to exit.");
            Console.ReadKey();


            client.MessageReceived -= Client_MessageReceived;

            await client.StopAsync();

        }

        private static void Client_MessageReceived(object? sender, MessageEventArgs e)
        {
            Console.WriteLine(e.Message.Body);

        }
    }
}
