using MediatR;
using Microsoft.AspNetCore.Mvc;
using OrderService.Application.Dtos;
using OrderService.Application.Queries;

namespace OrderService.API.Controllers;

[ApiController, Route("api/products")]
public class ProductsController(ISender mediator) : ControllerBase
{
    [HttpGet] public async Task<ActionResult<List<ProductDto>>> GetAll(CancellationToken ct) => await mediator.Send(new GetProductsQuery(), ct);
    [HttpGet("{id:guid}")] public async Task<ActionResult<ProductDto>> Get(Guid id, CancellationToken ct) => await mediator.Send(new GetProductByIdQuery(id), ct);
}
