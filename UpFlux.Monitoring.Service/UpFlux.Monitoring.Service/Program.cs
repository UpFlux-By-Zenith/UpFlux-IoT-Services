using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using UpFlux.Monitoring.Library;
using UpFlux.Monitoring.Library.Interfaces;
using UpFlux.Monitoring.Library.Services;
using Serilog;
using Serilog.Events;
using System;
using System.IO;

namespace UpFlux.Monitoring.Service
{
    /// <summary>
    /// The main program class that configures and runs the host for the Worker Service.
    /// </summary>
    public class Program
    {
        /// <summary>
        /// The main entry point of the application.
        /// </summary>
        public static void Main(string[] args)
        {
            // Build configuration
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory()) // Set the base path for configuration files
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true) // Add appsettings.json
                .AddEnvironmentVariables() // Include environment variables
                .Build();

            // Configure Serilog
            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration) // Read configuration from appsettings.json
                .Enrich.FromLogContext()
                .CreateLogger();

            try
            {
                Log.Information("Starting UpFlux Monitoring Service...");

                CreateHostBuilder(args, configuration).Build().Run();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Host terminated unexpectedly");
            }
            finally
            {
                // Ensure to flush before application-exit
                Log.CloseAndFlush();
            }
        }

        /// <summary>
        /// Configures the IHostBuilder with required services and settings.
        /// </summary>
        public static IHostBuilder CreateHostBuilder(string[] args, IConfiguration configuration) =>
            Host.CreateDefaultBuilder(args)
                .UseSerilog() // Use Serilog for logging
                .ConfigureServices((hostContext, services) =>
                {
                    // Bind configuration from appsettings.json to ServiceSettings
                    services.Configure<ServiceSettings>(configuration.GetSection("ServiceSettings"));

                    // Register the Worker service
                    services.AddHostedService<Worker>();

                    // Register the UpFlux Monitoring Library services
                    services.AddSingleton<ICpuMetricsService, CpuMetricsService>();
                    services.AddSingleton<IMemoryMetricsService, MemoryMetricsService>();
                    services.AddSingleton<ISystemUptimeService, SystemUptimeService>();
                    services.AddSingleton<ICpuTemperatureService, CpuTemperatureService>();
                    services.AddSingleton<IDiskMetricsService, DiskMetricsService>();

                    // Register NetworkMetricsService with network interface from settings
                    services.AddSingleton<INetworkMetricsService>(sp =>
                    {
                        var settings = sp.GetRequiredService<IOptions<ServiceSettings>>().Value;
                        return new NetworkMetricsService(settings.NetworkInterface);
                    });

                    // Register MetricsCollector
                    services.AddSingleton<MetricsCollector>();

                    // Register PythonScriptService
                    services.AddSingleton<PythonScriptService>();

                    // Register TcpClientService
                    services.AddSingleton<TcpClientService>();
                });
    }
}