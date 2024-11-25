using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting.Systemd;
using UpFlux.Gateway.Server.Models;

namespace UpFlux.Gateway.Server
{
    /// <summary>
    /// The main program class that configures and runs the UpFlux Gateway Server.
    /// </summary>
    public class Program
    {
        /// <summary>
        /// The main entry point of the application.
        /// </summary>
        public static void Main(string[] args)
        {
            // Build configuration
            IConfigurationBuilder configurationBuilder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

            IConfiguration configuration = configurationBuilder.Build();

            // Configure Serilog
            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration)
                .CreateLogger();

            try
            {
                Log.Information("Starting UpFlux Gateway Server...");

                IHost host = Host.CreateDefaultBuilder(args)
                    .UseSystemd() // Enable systemd integration
                    .UseSerilog() // Use Serilog for logging
                    .ConfigureServices((hostContext, services) =>
                    {
                        // Bind configuration to GatewaySettings
                        services.Configure<GatewaySettings>(configuration.GetSection("GatewaySettings"));

                        // Register the Worker service
                        services.AddHostedService<Worker>();

                        // Register other services here eventually

                    })
                    .Build();

                host.Run();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "UpFlux Gateway Server terminated unexpectedly");
            }
            finally
            {
                // Flush and close logs.
                Log.CloseAndFlush();
            }
        }
    }
}
