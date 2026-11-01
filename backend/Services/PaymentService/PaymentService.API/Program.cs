using BuildingBlocks.Common;
using BuildingBlocks.Contracts;
using BuildingBlocks.Logging.Client;
using BuildingBlocks.Messaging;
using BuildingBlocks.Observability;
using Microsoft.EntityFrameworkCore;
using PaymentService.Application;
using PaymentService.Application.Messaging;
using PaymentService.Infrastructure;
using PaymentService.Infrastructure.Persistence;
using System.Text.Json;

const string ServiceName = "PaymentService";
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers().AddJsonOptions(o => o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddAwsMessaging(builder.Configuration);
builder.Services.AddBusinessLogging(ServiceName);
builder.Services.AddObservability(ServiceName);
builder.Services.AddSqsConsumer<OrderCreatedEvent, OrderCreatedHandler>(QueueNames.OrderCreated);
builder.Services.AddHealthChecks().AddDbContextCheck<PaymentDbContext>("postgres");

var app = builder.Build();

await Retry.ExecuteAsync(async () =>
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
    if ((await db.Database.GetPendingMigrationsAsync()).Any() || (await db.Database.GetAppliedMigrationsAsync()).Any())
        await db.Database.MigrateAsync();
    else
        await db.Database.EnsureCreatedAsync();
}, 20, 2000, (ex, n) => app.Logger.LogWarning("Waiting for PostgreSQL (attempt {N}): {Msg}", n, ex.Message));

app.UseGlobalExceptionHandling();
app.UseCorrelationId();
app.UseSwagger();
app.UseSwaggerUI();
app.MapControllers();
app.MapHealthChecks("/health");
app.Run();

public partial class Program { }
