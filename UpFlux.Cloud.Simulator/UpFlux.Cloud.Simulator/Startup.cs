using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net.WebSockets;
using System.Threading;

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

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ControlChannelService controlService)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            // creating a public endpoint
            app.UseWebSockets();

            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGrpcService<ControlChannelService>();
            });

            app.Map("/ws/ai", builder =>
            {
                builder.Run(async context =>
                {
                    if (!context.WebSockets.IsWebSocketRequest)
                    {
                        context.Response.StatusCode = StatusCodes.Status400BadRequest;
                        return;
                    }

                    using WebSocket ws = await context.WebSockets.AcceptWebSocketAsync();
                    Guid id = Guid.NewGuid();
                    controlService.RegisterWebSocket(id, ws);

                    // keep the socket open until the client closes it
                    byte[] buffer = new byte[4];
                    while (ws.State == WebSocketState.Open)
                    {
                        await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    }

                    controlService.UnregisterWebSocket(id);
                });
            });

        }
    }
}
