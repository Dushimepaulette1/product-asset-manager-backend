using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProductAssetManager.Api.DTOs;
using ProductAssetManager.Api.Services;

namespace ProductAssetManager.Api.Controllers;

[ApiController]
[Route("api/orders")]
[Authorize]
public class OrdersController : ControllerBase
{
    private readonly IOrderService _orderService;

    public OrdersController(IOrderService orderService)
    {
        _orderService = orderService;
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateOrderRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        var result = await _orderService.CreateAsync(userId, request);

        if (result.VariantNotFound)
        {
            return NotFound(new { message = $"Variant '{request.VariantId}' was not found." });
        }

        if (result.ValidationError is not null)
        {
            return BadRequest(new { message = result.ValidationError });
        }

        return StatusCode(201, result.Order);
    }
}
