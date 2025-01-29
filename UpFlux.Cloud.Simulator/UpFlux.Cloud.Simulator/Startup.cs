using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace UpFlux.Cloud.Simulator
{
    public class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            // Add gRPC with large message sizes
            services.AddGrpc(options =>
            {
                // 200 MB limit
                const int limitBytes = 200 * 1024 * 1024;
                options.MaxReceiveMessageSize = limitBytes;
                options.MaxSendMessageSize = limitBytes;
            });

            services.AddSingleton<ControlChannelService>();
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGrpcService<ControlChannelService>();
            });
        }
    }
}
