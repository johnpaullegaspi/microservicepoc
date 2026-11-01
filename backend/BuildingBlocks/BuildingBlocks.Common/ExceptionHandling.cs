using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Common;

public class NotFoundException(string message) : Exception(message);
public class BusinessRuleException(string message) : Exception(message);
public class ValidationFailedException(IDictionary<string, string[]> errors) : Exception("Validation failed")
{
    public IDictionary<string, string[]> Errors { get; } = errors;
}

/// <summary>Global exception handler producing RFC7807-style problem details.</summary>
public class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task Invoke(HttpContext ctx)
    {
        try { await next(ctx); }
        catch (Exception ex)
        {
            (HttpStatusCode status, string title, IDictionary<string, string[]>? errors) = ex switch
            {
                NotFoundException => (HttpStatusCode.NotFound, ex.Message, null),
                BusinessRuleException => (HttpStatusCode.Conflict, ex.Message, null),
                ValidationFailedException v => (HttpStatusCode.BadRequest, "Validation failed", v.Errors),
                _ => (HttpStatusCode.InternalServerError, "An unexpected error occurred.", null)
            };
            if (status == HttpStatusCode.InternalServerError)
                logger.LogError(ex, "Unhandled exception. CorrelationId={CorrelationId}", CorrelationContext.Current);

            if (ctx.Response.HasStarted) throw;
            ctx.Response.StatusCode = (int)status;
            ctx.Response.ContentType = "application/problem+json";
            await ctx.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                title, status = (int)status, errors, correlationId = CorrelationContext.Current
            }));
        }
    }
}

public static class ExceptionHandlingExtensions
{
    public static IApplicationBuilder UseGlobalExceptionHandling(this IApplicationBuilder app) =>
        app.UseMiddleware<ExceptionHandlingMiddleware>();
}
