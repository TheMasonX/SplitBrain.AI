using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Orchestrator.Core.Interfaces;
using Orchestrator.Hosting.DependencyInjection;
using Serilog;
using Serilog.Events;
using SplitBrain.Dashboard.Components;
using SplitBrain.Dashboard.Hubs;
using SplitBrain.Dashboard.Logging;
using SplitBrain.Dashboard.Services;

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
            builder.Services.AddSingleton<SignalRDashboardPublisher>();

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

            // Node topology (hot-reload from nodes.json)
            builder.Configuration.AddJsonFile(
                Path.Combine(AppContext.BaseDirectory, "nodes.json"),
                optional: true, reloadOnChange: true);
            builder.Configuration.AddJsonFile("nodes.json", optional: true, reloadOnChange: true);

            // -----------------------------------------------------------
            // Shared infrastructure: options, HTTP clients, nodes,
            // queues, routing, model registry, health checks, etc.
            // -----------------------------------------------------------
            builder.Services.AddSplitBrainInfrastructure(builder.Configuration);

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
