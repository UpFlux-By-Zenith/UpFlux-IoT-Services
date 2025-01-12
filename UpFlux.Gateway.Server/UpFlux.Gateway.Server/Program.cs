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

            // Configure Serilog
            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration)
                .CreateLogger();

            try
            {
                Log.Information("Starting UpFlux Gateway Server...");

                // 3. Create and configure the Host
                IHost host = Host.CreateDefaultBuilder(args)
                    .UseSystemd()
                    .UseSerilog()

                    .ConfigureServices((hostContext, services) =>
                    {
                        // Bind "GatewaySettings" section from appsettings
                        services.Configure<GatewaySettings>(configuration.GetSection("GatewaySettings"));

                        services.AddSingleton<DeviceRepository>();
                        services.AddSingleton<VersionRepository>();

                        services.AddSingleton<CloudCommunicationService>();
                        services.AddSingleton<AlertingService>();
                        services.AddSingleton<DeviceCommunicationService>();
                        services.AddSingleton<ILicensePusher, LicensePusher>();
                        services.AddSingleton<LicenseValidationService>();
                        services.AddSingleton<DataAggregationService>();
                        services.AddSingleton<UpdateManagementService>();
                        services.AddSingleton<LogCollectionService>();
                        services.AddSingleton<CommandExecutionService>();
                        services.AddSingleton<VersionControlService>();

                        services.AddHostedService<Worker>();
                        services.AddHostedService<DeviceDiscoveryService>();
                    })

                    // Configure the Kestrel-based gRPC server
                    .ConfigureWebHostDefaults(webBuilder =>
                    {
                        // Kestrel server on port 5001 with TLS
                        webBuilder.ConfigureKestrel(serverOptions =>
                        {
                            serverOptions.ListenAnyIP(5001, listenOptions =>
                            {
                                listenOptions.Protocols = HttpProtocols.Http2;
                                // Enable TLS with the certificate from appsettings
                                string certPath = configuration["GatewaySettings:CertificatePath"];
                                string certPassword = configuration["GatewaySettings:CertificatePassword"];
                                listenOptions.UseHttps(certPath, certPassword);
                            });
                        });
                        webBuilder.UseStartup<Startup>();
                    })
                    .Build();

                host.Run();
            }
            catch (Exception ex)
            {
                // Fatal error
                Log.Fatal(ex, "UpFlux Gateway Server terminated unexpectedly");
            }
            finally
            {
                // Flush Serilog
                Log.CloseAndFlush();
            }
        }
    }
}
