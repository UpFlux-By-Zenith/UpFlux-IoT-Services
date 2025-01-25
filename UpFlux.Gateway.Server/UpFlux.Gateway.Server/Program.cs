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

            // Build the host to access DI services
            IHost host = CreateHostBuilder(args, configuration).Build();

            // Access AlertingService from the DI container
            using (IServiceScope scope = host.Services.CreateScope())
            {
                AlertingService alertingService = scope.ServiceProvider.GetRequiredService<AlertingService>();

                // Configure Serilog
                Log.Logger = new LoggerConfiguration()
                    .ReadFrom.Configuration(configuration)
                    .WriteTo.Sink(new SerilogAlertingSink(null, alertingService))
                    .CreateLogger();
            }

            try
            {
                Log.Information("Starting UpFlux Gateway Server...");

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

        public static IHostBuilder CreateHostBuilder(string[] args, IConfiguration configuration) =>
            Host.CreateDefaultBuilder(args)
                .UseSerilog()
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                });
    }
}
