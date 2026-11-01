using BuildingBlocks.Common;
using BuildingBlocks.Contracts;
using BuildingBlocks.Messaging;
using BuildingBlocks.Observability;
using LoggingService.Application;
using LoggingService.Infrastructure;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddLoggingApplication();
builder.Services.AddLoggingInfrastructure(builder.Configuration);
builder.Services.AddAwsMessaging(builder.Configuration);
builder.Services.AddObservability("LoggingService");
// Asynchronous ingestion: every service publishes to logging-queue, this consumer writes the files.
builder.Services.AddSqsConsumer<ApplicationLogEvent, LogEventHandler>(QueueNames.Logging);
builder.Services.AddHealthChecks();

var app = builder.Build();
app.UseGlobalExceptionHandling();
app.UseCorrelationId();
app.UseSwagger();
app.UseSwaggerUI();

// Direct ingestion (administrative testing / non-AWS callers)
app.MapPost("/api/logging", async ([FromBody] LogRequest request, LogIngestionService svc, CancellationToken ct) =>
{
    await svc.IngestAsync(request, ct);
    return Results.Accepted();
});
app.MapGet("/api/logging/health", () => Results.Ok(new { status = "Healthy", service = "LoggingService", utc = DateTime.UtcNow }));
app.MapHealthChecks("/health");
app.Run();

public partial class Program { }
