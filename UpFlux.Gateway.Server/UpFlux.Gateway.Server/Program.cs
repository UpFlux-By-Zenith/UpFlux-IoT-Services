using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting.Systemd;
using UpFlux.Gateway.Server.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Grpc.Core;
using UpFlux.Gateway.Server.Services;
using UpFlux.Gateway.Server.Repositories;

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
                    .ConfigureWebHostDefaults(webBuilder =>
                    {
                        webBuilder.ConfigureKestrel(serverOptions =>
                        {
                            // Configure Kestrel server options
                            serverOptions.ListenAnyIP(5001, listenOptions =>
                            {
                                listenOptions.Protocols = HttpProtocols.Http2;
                                // Eventually use TLS to configure
                                // listenOptions.UseHttps("path_to_certificate.pfx", "password");
                            });
                        });
                        webBuilder.UseStartup<Startup>();
                    })
                    .ConfigureServices((hostContext, services) =>
                    {
                        // Bind configuration to GatewaySettings
                        services.Configure<GatewaySettings>(configuration.GetSection("GatewaySettings"));

                        // Register the Worker service
                        services.AddHostedService<Worker>();

                        // Register other services as needed
                        // Register the DeviceDiscoveryService
                        services.AddHostedService<DeviceDiscoveryService>();

                        services.AddSingleton<DeviceCommunicationService>();
                        services.AddSingleton<DeviceRepository>();
                        services.AddSingleton<LicenseValidationService>();
                        services.AddSingleton<DataAggregationService>();
                        services.AddSingleton<CloudCommunicationService>();
                        services.AddSingleton<UpdateManagementService>();
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