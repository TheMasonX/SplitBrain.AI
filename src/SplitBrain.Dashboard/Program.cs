using Serilog;
using Serilog.Events;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using SplitBrain.Dashboard.Components;
using SplitBrain.Dashboard.Hubs;
using SplitBrain.Dashboard.Logging;
using SplitBrain.Dashboard.Services;
using Orchestrator.Core.Interfaces;

namespace SplitBrain.Dashboard
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddRazorComponents()
                .AddInteractiveServerComponents();

            builder.Services.AddSignalR();

            // Dashboard services
            builder.Services.AddSingleton<DashboardState>();
            builder.Services.AddSingleton<INodeHealthPublisher, SignalRNodeHealthPublisher>();
            builder.Services.AddSingleton<ILogEntryPublisher, SignalRLogEntryPublisher>();

            // Wire Serilog — pass IServiceProvider so the sink resolves ILogEntryPublisher
            // lazily on first Emit, avoiding re-entrant DI resolution during bootstrap.
            builder.Host.UseSerilog((ctx, sp, cfg) =>
            {
                cfg.MinimumLevel.Debug()
                   .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                   .WriteTo.Console()
                   .WriteTo.Sink(new SignalRLogSink(sp));
            });

            // OpenTelemetry — must be registered BEFORE builder.Build()
            var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] ?? "http://localhost:4317";
            builder.Services
                .AddOpenTelemetry()
                .ConfigureResource(r => r.AddService("SplitBrain.Dashboard", serviceVersion: "3.0.0"))
                .WithTracing(tracing => tracing
                    .AddAspNetCoreInstrumentation()
                    .AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint)))
                .WithMetrics(metrics => metrics
                    .AddAspNetCoreInstrumentation()
                    .AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint)));

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
                app.UseHsts();
            }

            app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
            app.UseHttpsRedirection();

            app.UseAntiforgery();

            app.MapStaticAssets();
            app.MapRazorComponents<App>()
                .AddInteractiveServerRenderMode();

            app.MapHub<DashboardHub>("/hubs/dashboard");

            app.Run();
        }
    }
}
