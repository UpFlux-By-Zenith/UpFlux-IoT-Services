using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Serilog;
using Microsoft.Extensions.Hosting.Systemd;
using UpFlux.Update.Service.Services;
using UpFlux.Update.Service.Utilities;

namespace UpFlux.Update.Service
{
    /// <summary>
    /// The main program class that configures and runs the host for the UpFlux Update Service.
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
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddEnvironmentVariables() // Include environment variables
                .Build();

            // Configure Serilog
            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration)
                .CreateLogger();

            try
            {
                Log.Information("Starting UpFlux Update Service...");

                CreateHostBuilder(args, configuration).Build().Run();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "UpFlux Update Service terminated unexpectedly");
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }

        /// <summary>
        /// Configures the IHostBuilder with required services and settings.
        /// </summary>
        public static IHostBuilder CreateHostBuilder(string[] args, IConfiguration configuration) =>
            Host.CreateDefaultBuilder(args)
                .UseSystemd()
                .UseSerilog()
                .ConfigureServices((hostContext, services) =>
                {
                    // Bind configuration to the Configuration model
                    services.Configure<Models.Configuration>(configuration.GetSection("UpdateService"));

                    // Register services
                    services.AddHostedService<UpdateService>();

                    services.AddSingleton<TcpListenerService>();
                    services.AddSingleton<FileWatcherService>();
                    services.AddSingleton<VersionManager>();
                    services.AddSingleton<SimulationService>();
                    services.AddSingleton<InstallationService>();
                    services.AddSingleton<LogMonitoringService>();
                    services.AddSingleton<RollbackService>();
                });
    }
}
