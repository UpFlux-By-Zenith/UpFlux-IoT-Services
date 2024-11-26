using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using UpFlux.Gateway.Server.Repositories;
using UpFlux.Gateway.Server.Services;

namespace UpFlux.Gateway.Server
{
    /// <summary>
    /// Configures services and the application's request pipeline.
    /// </summary>
    public class Startup
    {
        /// <summary>
        /// Configures services required by the application.
        /// </summary>
        public void ConfigureServices(IServiceCollection services)
        {
            // Add gRPC services
            services.AddGrpc();
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

            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGrpcService<UpdateServiceGrpc>();
                endpoints.MapGrpcService<CommandServiceGrpc>();
                endpoints.MapGrpcService<VersionDataServiceGrpc>();
                endpoints.MapGrpcService<LogRequestServiceGrpc>();
                // Map gRPC services here 
            });
        }
    }
}
