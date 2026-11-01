using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace BuildingBlocks.Common;

/// <summary>Ambient correlation id (flows through async calls, incl. message handlers).</summary>
public static class CorrelationContext
{
    private static readonly AsyncLocal<string?> _current = new();
    public static string Current
    {
        get => _current.Value ??= Guid.NewGuid().ToString("N");
        set => _current.Value = value;
    }
}

public class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string Header = "X-Correlation-Id";

    public async Task Invoke(HttpContext ctx)
    {
        var id = ctx.Request.Headers[Header].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(id)) id = Guid.NewGuid().ToString("N");
        CorrelationContext.Current = id;
        ctx.Request.Headers[Header] = id; // forwarded by YARP / HttpClient to downstream services
        ctx.Response.Headers[Header] = id;
        await next(ctx);
    }
}

public static class CorrelationExtensions
{
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app) =>
        app.UseMiddleware<CorrelationIdMiddleware>();
}
