using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQConsumer.Services;

namespace RabbitMQConsumer
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            Console.WriteLine("Starting RabbitMQ Consumer Application...");
            Console.WriteLine("==========================================");

            var host = CreateHostBuilder(args).Build();

            try
            {
                await host.RunAsync();
            }
            catch (Exception ex)
            {
                var logger = host.Services.GetRequiredService<ILogger<Program>>();
                logger.LogError(ex, "Application terminated unexpectedly");
            }
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((context, config) =>
                {
                    config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                })
                .ConfigureServices((context, services) =>
                {
                    // Register RabbitMQ settings
                    services.Configure<RabbitMQConsumer.Models.RabbitMQSettings>(
                        context.Configuration.GetSection("RabbitMQ"));

                    // Register the consumer service
                    services.AddHostedService<RabbitMQConsumerService>();

                    // Add logging
                    services.AddLogging(builder =>
                    {
                        builder.AddConsole();
                        builder.SetMinimumLevel(LogLevel.Information);
                    });
                })
                .UseConsoleLifetime();
    }
}
