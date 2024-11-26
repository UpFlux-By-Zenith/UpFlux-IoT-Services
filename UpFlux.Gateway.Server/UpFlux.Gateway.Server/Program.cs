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
using UpFlux.Gateway.Server.Services;
using UpFlux.Gateway.Server.Repositories;
using UpFlux.Gateway.Server.Utilities;

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
            IConfiguration configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            // Create a temporary ServiceCollection to build a ServiceProvider for Serilog configuration
            ServiceCollection services = new ServiceCollection();

            // Register configuration
            services.AddSingleton<IConfiguration>(configuration);

            // Register necessary services for AlertingService
            services.Configure<GatewaySettings>(configuration.GetSection("GatewaySettings"));
            services.AddSingleton<CloudCommunicationService>(); // Ensure dependencies are registered
            services.AddSingleton<AlertingService>(); // Register AlertingService

            // Build the ServiceProvider
            ServiceProvider serviceProvider = services.BuildServiceProvider();

            // Retrieve AlertingService from the ServiceProvider
            AlertingService? alertingService = serviceProvider.GetService<AlertingService>();

            // Configure Serilog
            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration)
                .WriteTo.Sink(new SerilogAlertingSink(null, alertingService))
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
                                // Optionally configure TLS when supported
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
                        services.AddHostedService<DeviceDiscoveryService>();

                        services.AddSingleton<DeviceCommunicationService>();
                        services.AddSingleton<DeviceRepository>();
                        services.AddSingleton<VersionRepository>();
                        services.AddSingleton<LicenseValidationService>();
                        services.AddSingleton<DataAggregationService>();
                        services.AddSingleton<CloudCommunicationService>();
                        services.AddSingleton<UpdateManagementService>();
                        services.AddSingleton<LogCollectionService>();
                        services.AddSingleton<CommandExecutionService>();
                        services.AddSingleton<VersionControlService>();
                        services.AddSingleton<AlertingService>();
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
