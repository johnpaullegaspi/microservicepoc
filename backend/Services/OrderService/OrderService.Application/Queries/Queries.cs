using BuildingBlocks.Common;
using MediatR;
using OrderService.Application.Abstractions;
using OrderService.Application.Dtos;

namespace OrderService.Application.Queries;

public record GetProductsQuery : IRequest<List<ProductDto>>;
public record GetProductByIdQuery(Guid Id) : IRequest<ProductDto>;
public record GetOrderByIdQuery(Guid Id) : IRequest<OrderDto>;
public record GetOrderByNumberQuery(string OrderNumber) : IRequest<OrderDto>;
public record GetCustomerOrdersQuery(string Email) : IRequest<List<OrderDto>>;

public class GetProductsHandler(IProductRepository repo) : IRequestHandler<GetProductsQuery, List<ProductDto>>
{
    public async Task<List<ProductDto>> Handle(GetProductsQuery q, CancellationToken ct) =>
        (await repo.GetAllAsync(ct)).Select(p => p.ToDto()).ToList();
}

public class GetProductByIdHandler(IProductRepository repo) : IRequestHandler<GetProductByIdQuery, ProductDto>
{
    public async Task<ProductDto> Handle(GetProductByIdQuery q, CancellationToken ct) =>
        (await repo.GetByIdAsync(q.Id, ct) ?? throw new NotFoundException("Product not found.")).ToDto();
}

public class GetOrderByIdHandler(IOrderRepository repo) : IRequestHandler<GetOrderByIdQuery, OrderDto>
{
    public async Task<OrderDto> Handle(GetOrderByIdQuery q, CancellationToken ct) =>
        (await repo.GetByIdAsync(q.Id, ct) ?? throw new NotFoundException("Order not found.")).ToDto();
}

public class GetOrderByNumberHandler(IOrderRepository repo) : IRequestHandler<GetOrderByNumberQuery, OrderDto>
{
    public async Task<OrderDto> Handle(GetOrderByNumberQuery q, CancellationToken ct) =>
        (await repo.GetByNumberAsync(q.OrderNumber.Trim().ToUpperInvariant(), ct) ?? throw new NotFoundException("Order not found.")).ToDto();
}

public class GetCustomerOrdersHandler(IOrderRepository repo) : IRequestHandler<GetCustomerOrdersQuery, List<OrderDto>>
{
    public async Task<List<OrderDto>> Handle(GetCustomerOrdersQuery q, CancellationToken ct) =>
        (await repo.GetByCustomerAsync(q.Email.Trim().ToLowerInvariant(), ct)).Select(o => o.ToDto()).ToList();
}
