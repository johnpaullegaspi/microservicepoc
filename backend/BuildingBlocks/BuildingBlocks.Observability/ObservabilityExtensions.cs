using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace BuildingBlocks.Observability;

public static class ObservabilityExtensions
{
    /// <summary>OpenTelemetry tracing (console exporter for the POC; swap for OTLP/X-Ray in AWS).</summary>
    public static IServiceCollection AddObservability(this IServiceCollection services, string serviceName, bool aspNet = true)
    {
        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName))
            .WithTracing(t =>
            {
                if (aspNet) t.AddAspNetCoreInstrumentation();
                t.AddHttpClientInstrumentation();
                // Console exporter is noisy; enable with OTEL_CONSOLE=true
                if (string.Equals(Environment.GetEnvironmentVariable("OTEL_CONSOLE"), "true", StringComparison.OrdinalIgnoreCase))
                    t.AddConsoleExporter();
            });
        return services;
    }
}
