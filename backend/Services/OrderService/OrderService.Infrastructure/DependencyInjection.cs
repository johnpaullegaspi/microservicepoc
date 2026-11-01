using BuildingBlocks.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrderService.Application.Abstractions;
using OrderService.Infrastructure.Persistence;

namespace OrderService.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<OrderDbContext>(o => o.UseSqlServer(config.GetConnectionString("OrderDb"),
            sql => sql.EnableRetryOnFailure(5)));
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IEventOutbox, EfEventOutbox>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IInboxStore, EfInboxStore>();
        services.AddOutboxDispatcher<EfOutboxStore>();
        return services;
    }
}
