using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProductAssetManager.Api.DTOs;
using ProductAssetManager.Api.Services;

namespace ProductAssetManager.Api.Controllers;

[ApiController]
[Route("api/variants")]
public class VariantsController : ControllerBase
{
    private readonly IVariantService _variantService;

    public VariantsController(IVariantService variantService)
    {
        _variantService = variantService;
    }

    [HttpPatch("{sku}/stock")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UpdateStock(string sku, UpdateStockRequest request)
    {
        var result = await _variantService.UpdateStockAsync(sku, request.Quantity);

        if (result.VariantNotFound)
        {
            return NotFound(new { message = $"Variant with SKU '{sku}' was not found." });
        }

        if (result.ValidationError is not null)
        {
            return BadRequest(new { message = result.ValidationError });
        }

        return Ok(result.Variant);
    }
}
