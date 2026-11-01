using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Microservices.IntegrationTests;

/// <summary>
/// Full purchase flow against the RUNNING docker compose stack (gateway on http://localhost:8080 by default).
/// Opt-in: set E2E_BASE_URL (e.g. http://localhost:8080). Otherwise the tests are reported as skipped.
/// Steps: browse products -> checkout -> OrderCreated (SQS) -> PaymentService -> PaymentSucceeded/Failed (SNS->SQS)
/// -> order updated -> notification + logs.
/// </summary>
public class EndToEndPurchaseFlowTests
{
    private static string? BaseUrl => Environment.GetEnvironmentVariable("E2E_BASE_URL");
    private static HttpClient Client() => new() { BaseAddress = new Uri(BaseUrl!), Timeout = TimeSpan.FromSeconds(30) };

    private static async Task<JsonElement> PlaceOrder(HttpClient http, string email)
    {
        var products = await http.GetFromJsonAsync<JsonElement[]>("/api/products");
        Assert.NotNull(products);
        Assert.True(products!.Length >= 4);
        var res = await http.PostAsJsonAsync("/api/orders", new
        {
            customerName = "E2E Tester", customerEmail = email, contactNumber = "+639171234567",
            deliveryAddress = "1 Test Street", paymentMethod = "CreditCard",
            items = new[] { new { productId = products[0].GetProperty("id").GetGuid(), quantity = 1 } }
        });
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<JsonElement> WaitForFinalStatus(HttpClient http, string orderNumber)
    {
        for (var i = 0; i < 40; i++)
        {
            var o = await http.GetFromJsonAsync<JsonElement>($"/api/orders/number/{orderNumber}");
            if (o.GetProperty("orderStatus").GetString() is "Confirmed" or "PaymentFailed") return o;
            await Task.Delay(1000);
        }
        throw new TimeoutException("Order never reached a final status");
    }

    [SkippableFact]
    public async Task Purchase_WithSuccessfulPayment_ConfirmsOrder()
    {
        Skip.If(string.IsNullOrEmpty(BaseUrl), "Set E2E_BASE_URL to run against docker compose");
        using var http = Client();
        var created = await PlaceOrder(http, $"e2e-{Guid.NewGuid():N}@example.com");
        Assert.Equal("Pending", created.GetProperty("orderStatus").GetString());
        var final = await WaitForFinalStatus(http, created.GetProperty("orderNumber").GetString()!);
        Assert.Equal("Confirmed", final.GetProperty("orderStatus").GetString());
        Assert.Equal("Successful", final.GetProperty("paymentStatus").GetString());
        Assert.StartsWith("TXN-", final.GetProperty("transactionReference").GetString());
    }

    [SkippableFact]
    public async Task Purchase_WithFailingPayment_MarksPaymentFailed()
    {
        Skip.If(string.IsNullOrEmpty(BaseUrl), "Set E2E_BASE_URL to run against docker compose");
        using var http = Client();
        var created = await PlaceOrder(http, $"e2e-fail-{Guid.NewGuid():N}@example.com"); // "fail" => simulated decline
        var final = await WaitForFinalStatus(http, created.GetProperty("orderNumber").GetString()!);
        Assert.Equal("PaymentFailed", final.GetProperty("orderStatus").GetString());
        Assert.Equal("Failed", final.GetProperty("paymentStatus").GetString());
    }

    [SkippableFact]
    public async Task InvalidOrder_IsRejectedWith400()
    {
        Skip.If(string.IsNullOrEmpty(BaseUrl), "Set E2E_BASE_URL to run against docker compose");
        using var http = Client();
        var res = await http.PostAsJsonAsync("/api/orders", new { customerName = "", customerEmail = "bad", items = Array.Empty<object>() });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, res.StatusCode);
    }

    [SkippableFact]
    public async Task PaymentEndpoints_AreNotReachableThroughGateway()
    {
        Skip.If(string.IsNullOrEmpty(BaseUrl), "Set E2E_BASE_URL to run against docker compose");
        using var http = Client();
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await http.PostAsync("/api/payments/process", null)).StatusCode);
    }
}
