using BuildingBlocks.Common;
using MediatR;
using PaymentService.Application.Abstractions;
using PaymentService.Application.Dtos;

namespace PaymentService.Application.Queries;

public record GetPaymentByIdQuery(Guid Id) : IRequest<PaymentDto>;
public record GetPaymentByOrderIdQuery(Guid OrderId) : IRequest<PaymentDto>;

public class GetPaymentByIdHandler(IPaymentRepository repo) : IRequestHandler<GetPaymentByIdQuery, PaymentDto>
{
    public async Task<PaymentDto> Handle(GetPaymentByIdQuery q, CancellationToken ct) =>
        (await repo.GetByIdAsync(q.Id, ct) ?? throw new NotFoundException("Payment not found.")).ToDto();
}

public class GetPaymentByOrderIdHandler(IPaymentRepository repo) : IRequestHandler<GetPaymentByOrderIdQuery, PaymentDto>
{
    public async Task<PaymentDto> Handle(GetPaymentByOrderIdQuery q, CancellationToken ct) =>
        (await repo.GetByOrderIdAsync(q.OrderId, ct) ?? throw new NotFoundException("Payment not found for order.")).ToDto();
}
