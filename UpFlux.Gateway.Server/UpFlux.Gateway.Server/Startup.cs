using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Configuration;
using UpFlux.Gateway.Server.Models;
using UpFlux.Gateway.Server.Repositories;
using UpFlux.Gateway.Server.Services;

namespace UpFlux.Gateway.Server
{
    /// <summary>
    /// Configures services and the application's request pipeline.
    /// </summary>
    public class Startup
    {
        private readonly IConfiguration _configuration;

        /// <summary>
        /// Initializes a new instance of the <see cref="Startup"/> class.
        /// </summary>
        /// <param name="configuration"></param>
        public Startup(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        /// <summary>
        /// Configures services required by the application.
        /// </summary>
        public void ConfigureServices(IServiceCollection services)
        {
            // Bind GatewaySettings
            services.Configure<GatewaySettings>(_configuration.GetSection("GatewaySettings"));

            // Register the repository
            services.AddSingleton<DeviceRepository>();

            // Services for local device communication
            services.AddSingleton<DeviceCommunicationService>();
            services.AddSingleton<LogCollectionService>();
            services.AddSingleton<UpdateManagementService>();
            services.AddSingleton<CommandExecutionService>();
            services.AddSingleton<AlertingService>();
            services.AddSingleton<DeviceUsageAggregator>();
            services.AddSingleton<AiCommunicationService>();

            // The single control channel worker that dials the Cloud
            // 1) Explicitly register ControlChannelWorker as a singleton
            services.AddSingleton<ControlChannelWorker>();

            // 2) Also register it as a hosted service so that its ExecuteAsync runs
            services.AddHostedService(provider => provider.GetRequiredService<ControlChannelWorker>());

            services.AddHostedService<Worker>();
            services.AddHostedService<DeviceDiscoveryService>();
            services.AddHostedService<AiIntegrationWorker>();
            services.AddHostedService<ScheduledUpdateWorker>();
        }

        /// <summary>
        /// Configures the HTTP request pipeline.
        /// </summary>
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            // Configure request pipeline
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
        }
    }
}
