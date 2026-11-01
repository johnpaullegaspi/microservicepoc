using BuildingBlocks.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PaymentService.Application.Abstractions;
using PaymentService.Infrastructure.Persistence;

namespace PaymentService.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<PaymentDbContext>(o => o.UseNpgsql(config.GetConnectionString("PaymentDb"),
            n => n.EnableRetryOnFailure(5)));
        services.Configure<PaymentGatewayOptions>(config.GetSection(PaymentGatewayOptions.Section));
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IEventOutbox, EfEventOutbox>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IInboxStore, EfInboxStore>();
        services.AddScoped<IPaymentGateway, SimulatedPaymentGateway>();
        services.AddOutboxDispatcher<EfOutboxStore>();
        return services;
    }
}
