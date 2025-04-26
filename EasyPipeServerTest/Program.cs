using EasyPipes;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EasyPipeServerTest
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            try
            {
                Console.WriteLine("Creating demo data to send from the server pipe to client");

                Directory.CreateDirectory("data");

                string filePath = "data\\large_data.csv"; // Path where the file will be saved

                // Generate and save 10,000 records
                GenerateCsvFile(filePath, 10000);

                Console.WriteLine("EasyPipes.Server > starting...");

                // Initialize server with pipe name "test"
                var server = new Server("test");

                // Start listening for clients
                await server.StartAsync();

                var csvFiles = Directory.GetFiles("data", "*.csv", SearchOption.AllDirectories);


                foreach (var file in csvFiles)
                {
                    Console.WriteLine($"{DateTime.Now} - Sending rows from: {file}");
                        

                    // Using StreamReader for better performance with large files
                    using (var reader = new StreamReader(file))
                    {
                        string line;
                        // Skip the header
                        await reader.ReadLineAsync();

                        int lineCount = 0;
                        while ((line = await reader.ReadLineAsync())!= null)
                        {
                            await server.TrySendMessageAsync(line);
                            lineCount++;

                            if (lineCount % 500 == 0)  // Print progress every 500 records
                            {
                                Console.WriteLine($"Sent {lineCount} rows...");
                            }
                        }
                    }
                }

                Console.WriteLine($"{DateTime.Now} - All data sent. Press any key to stop the server...");
                Console.ReadKey();

                // Stop the server
                await server.StopAsync();
                Console.WriteLine("Server stopped. Exiting.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
            }
        }

        public static void GenerateCsvFile(string filePath, int numberOfRecords)
        {
            try
            {
                // Create a StringBuilder to store the CSV content
                StringBuilder csvContent = new StringBuilder();

                // Add CSV header
                csvContent.AppendLine("ID,Name,Age,City");

                // Generate the records
                Random rand = new Random();
                for (int i = 1; i <= numberOfRecords; i++)
                {
                    string id = i.ToString();
                    string name = $"Name_{rand.Next(1000, 9999)}";
                    string age = rand.Next(18, 80).ToString();
                    string city = $"City_{rand.Next(1, 100)}";

                    // Format and append the row to the CSV
                    csvContent.AppendLine($"{id},{name},{age},{city}");
                }

                // Write the content to a CSV file
                File.WriteAllText(filePath, csvContent.ToString());
                Console.WriteLine($"CSV file generated at {filePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error generating CSV file: {ex.Message}");
            }
        }
    }
}
