using Microsoft.Extensions.Options;
using PaymentService.Application.Abstractions;
using PaymentService.Domain.Entities;

namespace PaymentService.Infrastructure.Persistence;

/// <summary>
/// Configuration for the mock gateway (section "PaymentGateway").
/// Mode: Rules (default) | AlwaysSucceed | AlwaysFail | Random.
/// Rules: customer e-mail containing FailEmailKeyword (default "fail") is declined; everything else succeeds.
/// </summary>
public class PaymentGatewayOptions
{
    public const string Section = "PaymentGateway";
    public string Mode { get; set; } = "Rules";
    public string FailEmailKeyword { get; set; } = "fail";
    public double FailureRate { get; set; } = 0.2;   // used by Random
    public int DelayMs { get; set; } = 1500;         // simulated latency
}

public class SimulatedPaymentGateway(IOptions<PaymentGatewayOptions> options) : IPaymentGateway
{
    private readonly PaymentGatewayOptions _o = options.Value;

    public async Task<GatewayResult> ChargeAsync(Payment payment, CancellationToken ct)
    {
        if (_o.DelayMs > 0) await Task.Delay(_o.DelayMs, ct);

        var fail = _o.Mode switch
        {
            "AlwaysSucceed" => false,
            "AlwaysFail" => true,
            "Random" => Random.Shared.NextDouble() < _o.FailureRate,
            _ => payment.CustomerEmail.Contains(_o.FailEmailKeyword, StringComparison.OrdinalIgnoreCase)
        };

        return fail
            ? new GatewayResult(false, null, "Card declined by issuer (simulated)")
            : new GatewayResult(true, $"TXN-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..10].ToUpperInvariant()}", null);
    }
}
