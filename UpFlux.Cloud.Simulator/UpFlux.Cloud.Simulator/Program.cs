﻿using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace UpFlux.Cloud.Simulator
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            // Build configuration from appsettings.json
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            // Set up Serilog
            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration)
                .CreateLogger();

            try
            {
                Log.Information("Starting UpFlux Cloud Simulator...");

                // Build the host for our gRPC server
                IHost host = CreateHostBuilder(args, configuration).Build();

                // Start the host in background
                await host.StartAsync();

                Log.Information("Cloud Simulator started. Press CTRL+C or use menu to exit.");

                // Now retrieve the necessary services from DI (pure DI approach)
                ControlChannelService controlChannelService = host.Services.GetRequiredService<ControlChannelService>();
                CloudSettings? cloudSettings = configuration.GetSection("CloudSettings").Get<CloudSettings>();

                // Create the console menu
                ConsoleMenu menu = new ConsoleMenu(configuration, cloudSettings, controlChannelService);
                await menu.RunMenuLoop();

                // After menu ends, stop the host
                await host.StopAsync();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Cloud Simulator crashed unexpectedly.");
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }

        public static IHostBuilder CreateHostBuilder(string[] args, IConfiguration configuration) =>
            Host.CreateDefaultBuilder(args)
                .UseSerilog()
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.ConfigureKestrel(options =>
                    {
                        IConfigurationSection cloudSection = configuration.GetSection("CloudSettings");
                        CloudSettings? cloudSettings = cloudSection.Get<CloudSettings>();

                        options.ListenAnyIP(cloudSettings.ListeningPort, listenOptions =>
                        {
                            listenOptions.Protocols = HttpProtocols.Http2;
                        });

                        options.ListenAnyIP(cloudSettings.WebSocketPort, listenOptions =>
                        {
                            listenOptions.Protocols = HttpProtocols.Http1;
                        });
                    });
                    webBuilder.UseStartup<Startup>();
                });
    }
}
