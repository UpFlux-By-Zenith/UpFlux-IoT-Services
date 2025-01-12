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
            services.AddGrpc();
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
                endpoints.MapGrpcService<LicenseServiceMock>();
                endpoints.MapGrpcService<MonitoringServiceMock>();
                endpoints.MapGrpcService<AlertServiceMock>();
                endpoints.MapGrpcService<CloudLogServiceMock>();
            });
        }
    }
}
