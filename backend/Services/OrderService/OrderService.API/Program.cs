using BuildingBlocks.Common;
using BuildingBlocks.Contracts;
using BuildingBlocks.Logging.Client;
using BuildingBlocks.Messaging;
using BuildingBlocks.Observability;
using Microsoft.EntityFrameworkCore;
using OrderService.Application;
using OrderService.Application.Messaging;
using OrderService.Infrastructure;
using OrderService.Infrastructure.Persistence;
using System.Text.Json;

const string ServiceName = "OrderService";
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers().AddJsonOptions(o => o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddAwsMessaging(builder.Configuration);
builder.Services.AddBusinessLogging(ServiceName);
builder.Services.AddObservability(ServiceName);
builder.Services.AddSqsConsumer<JsonElement, PaymentResultHandler>(QueueNames.PaymentResult);
builder.Services.AddHealthChecks().AddDbContextCheck<OrderDbContext>("sqlserver");

var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().WithExposedHeaders("X-Correlation-Id")));

var app = builder.Build();

// Safe DB initialisation: retry until SQL Server accepts connections
await Retry.ExecuteAsync(async () =>
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
    if ((await db.Database.GetPendingMigrationsAsync()).Any() || (await db.Database.GetAppliedMigrationsAsync()).Any())
        await db.Database.MigrateAsync();
    else
        await db.Database.EnsureCreatedAsync(); // no migrations generated yet: create schema from the model
}, 20, 2000, (ex, n) => app.Logger.LogWarning("Waiting for SQL Server (attempt {N}): {Msg}", n, ex.Message));

app.UseGlobalExceptionHandling();
app.UseCorrelationId();
app.UseCors();
app.UseSwagger();
app.UseSwaggerUI();
app.MapControllers();
app.MapHealthChecks("/health");
app.Run();

public partial class Program { }
