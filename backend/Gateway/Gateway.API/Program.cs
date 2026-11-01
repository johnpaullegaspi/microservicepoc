using BuildingBlocks.Common;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));
var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().WithExposedHeaders("X-Correlation-Id")));
builder.Services.AddHealthChecks();

var app = builder.Build();
app.UseCors();
app.UseCorrelationId(); // generates X-Correlation-Id for every browser request, forwarded to services
app.MapHealthChecks("/health");
app.MapReverseProxy();
app.Run();
